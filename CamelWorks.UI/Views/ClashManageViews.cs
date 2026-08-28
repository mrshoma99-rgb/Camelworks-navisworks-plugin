using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The results, the way the engine holds them — and the pen to work them.
    ///
    /// The Board derives issues; this tab manages results. They are different jobs. A coordinator
    /// in a Friday meeting is asked "what is the status of clash 214 in test A2B4", and the answer
    /// has to be read, changed, commented and assigned <i>in the model's own clash data</i>, so
    /// that the native clash window, the exports and every other tool see the same thing.
    ///
    /// <b>Two sources, one workbench.</b> From the model, edits write straight through the seam.
    /// From a file, the same verbs mark up the file's own data — which is the whole review round:
    /// a Manage seat exports the baseline, every Simulate seat opens it here, marks, comments and
    /// assigns, and saves a markup beside it; the Manage seat then reads the markups back in and
    /// the model carries everyone's answers. Simulate has no clash engine, and for the file leg it
    /// does not need one.
    /// </summary>
    public static class ClashManageViews
    {
        private static readonly string[] Statuses = { "New", "Active", "Reviewed", "Approved", "Resolved" };

        private const string FromModel = "The model";
        private const string FromFile = "A file";

        // The reads survive tab switches and window closes, like the board does: re-reading
        // eighteen thousand results because somebody glanced at the Rules tab would make the tab
        // switch cost more than the work.
        private static List<TestEntry>? _read;
        private static FileRound? _file;
        private static string _source = FromModel;

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Manage(Session session) => new Screen(session).Build();

        /// <summary>Forget the cached model read, so the next visit reads afresh.</summary>
        internal static void Forget() => _read = null;

        // -----------------------------------------------------------------------------------

        /// <summary>One file, read for markup, with where it came from.</summary>
        private sealed class FileRound
        {
            internal FileRound(string path) => Path = path;

            internal string Path { get; }

            internal List<TestEntry> Entries { get; } = new List<TestEntry>();

            internal int Unkeyed { get; set; }
        }

        private sealed class TestEntry
        {
            internal TestEntry(ClashTestInfo test) => Test = test;

            internal ClashTestInfo Test { get; set; }

            internal List<Row> Rows { get; } = new List<Row>();

            internal int Of(string status) =>
                Rows.Count(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class Row
        {
            internal Row(string testId, string testName, ClashResultInfo info)
            {
                TestId = testId;
                TestName = testName;
                Info = info;
                Status = info.HostStatus ?? "New";
                Assigned = info.HostAssignedTo ?? string.Empty;
                Group = info.GroupName ?? string.Empty;
                Comments = info.CommentCount;
                LastComment = info.LastComment ?? string.Empty;

                foreach (var comment in info.Comments) Trail.Add(comment);
            }

            internal string TestId { get; }
            internal string TestName { get; }
            internal ClashResultInfo Info { get; }
            internal string Status { get; set; }
            internal string Assigned { get; set; }
            internal string Group { get; set; }
            internal int Comments { get; set; }
            internal string LastComment { get; set; }

            /// <summary>The file's own item, kept so a markup save writes back what was read.</summary>
            internal ClashItem? FileItem { get; set; }

            /// <summary>The authored comment trail, held in full only for file rows.</summary>
            internal List<ClashComment> Trail { get; } = new List<ClashComment>();
        }

        // -----------------------------------------------------------------------------------

        private sealed class Screen
        {
            private readonly Session _session;

            private readonly StackPanel _tests = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _headline = Ui.Stack();
            private readonly StackPanel _sync = Ui.Stack();

            private readonly TextBox _find;
            private readonly TextBox _comment;
            private readonly TextBox _group;
            private readonly TextBox _renameTest;
            private readonly TextBox _renameResults;
            private readonly TextBox _folder;
            private readonly TextBox _path;
            private readonly TextBox _markups;
            private readonly ComboBox _assign;

            private readonly HashSet<string> _show =
                new HashSet<string>(Statuses, StringComparer.OrdinalIgnoreCase);

            private string _mark = "Reviewed";
            private bool _hideEmpty;
            private bool _onlyCommented;
            private double _markerAcross = 300;

            private readonly HashSet<string> _pickedTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>What the results grid currently shows, for the spreadsheet writer.</summary>
            private List<Row> _shown = new List<Row>();

            internal Screen(Session session)
            {
                _session = session;

                // A seat with no clash engine has exactly one source; the switch would be a lie.
                if (!session.Clash.IsAvailable) _source = FromFile;

                _find = Ui.Text(string.Empty, null, 150);
                _comment = Ui.Text(string.Empty, null, 170);
                _group = Ui.Text(string.Empty, null, 170);
                _renameTest = Ui.Text(string.Empty, null, 170);
                _renameResults = Ui.Text(string.Empty, null, 170);
                _folder = Ui.Text(string.Empty, null, 170);
                _path = Ui.Text(_file?.Path ?? string.Empty, null, 190);
                _markups = Ui.Text(string.Empty, null, 190);
                _assign = Suggesting(Assignees());
                _find.TextChanged += (s, e) => RedrawTests();
            }

            private bool OnFile => _source == FromFile;

            internal UIElement Build()
            {
                // One caption for both sources: the results are what gets read either way, and
                // the registry promises this exact action.
                var read = Ui.Runner("Read the results", _log, Read);

                // The log sits above the tables: a verb's outcome must land where the eye already
                // is, not below two thousand rows of what it just acted on.
                var stage = Ui.Stack(
                    _headline,
                    _log,
                    _tests,
                    Ui.Rule(),
                    _results);

                if (Current() == null)
                {
                    _headline.Children.Add(Bench.Ready(
                        OnFile
                            ? "Reading opens an exported clash file for markup: every verb below "
                              + "works on the file's own results, and the markup saves beside it "
                              + "for whoever runs the sync."
                            : "Reading pulls every test and every result out of the document, with "
                              + "status, assignee, comments and grouping — then the whole board of "
                              + "verbs works on whatever you pick.",
                        OnFile
                            ? "This is the review round: the file came from a Manage seat, and "
                              + "your saved markup goes back into the model when they sync."
                            : "Every edit writes back into the model's own clash data, so the "
                              + "native clash window and every export see the same thing."));

                    if (!_session.Clash.IsAvailable)
                        _headline.Children.Add(Ui.Info("This Navisworks edition has no clash "
                            + "engine, so the model source is off — but the review round is not: "
                            + "open the exported file, mark it up, save the markup. "
                            + (_session.ClashProblem ?? string.Empty)));
                }
                else
                {
                    Redraw();
                }

                return Bench.Surface(Setup(), read, stage);
            }

            private List<TestEntry>? Current() => OnFile ? _file?.Entries : _read;

            // -------------------------------------------------------------------------------
            // Reading
            // -------------------------------------------------------------------------------

            private void Read(Job job)
            {
                if (OnFile) ReadFile(job);
                else ReadModel(job);
            }

            private void ReadModel(Job job)
            {
                var entries = new List<TestEntry>();
                var seen = 0;

                foreach (var test in _session.Clash.Tests())
                {
                    var entry = new TestEntry(test);

                    foreach (var result in _session.Clash.Results(test.Id))
                    {
                        if (!job.Step(++seen, "results read")) return;
                        entry.Rows.Add(new Row(test.Id, test.Name, result));
                    }

                    entries.Add(entry);
                }

                _read = entries;
                Redraw();

                _log.Children.Add(Ui.Line(Ui.Count(entries.Count, "test") + ", "
                    + Ui.Count(entries.Sum(e => e.Rows.Count), "result") + " read.", 1, true));
            }

            private void ReadFile(Job job)
            {
                var path = _path.Text.Trim();

                if (path.Length == 0)
                {
                    _log.Children.Add(Ui.Problem("Say which file to open first."));
                    return;
                }

                string text;

                try
                {
                    text = System.IO.File.ReadAllText(path);
                }
                catch (Exception e)
                {
                    _log.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                var read = ClashXml.Read(text);

                if (!read.IsUsable)
                {
                    _log.Children.Add(Ui.Problem(read.Explain()));
                    return;
                }

                var round = new FileRound(path);
                var byTest = new Dictionary<string, TestEntry>(StringComparer.OrdinalIgnoreCase);

                foreach (var test in read.Tests)
                {
                    if (byTest.ContainsKey(test.Name)) continue;

                    var entry = new TestEntry(new ClashTestInfo(test.Name, test.Name,
                                                                test.Folder, 0, 0));
                    byTest[test.Name] = entry;
                    round.Entries.Add(entry);
                }

                var seen = 0;

                foreach (var result in read.Results)
                {
                    if (!job.Step(++seen, "results read")) return;

                    if (!byTest.TryGetValue(result.Test, out var entry))
                    {
                        entry = new TestEntry(new ClashTestInfo(result.Test, result.Test, null, 0, 0));
                        byTest[result.Test] = entry;
                        round.Entries.Add(entry);
                    }

                    if (result.Item == null) round.Unkeyed++;

                    var last = result.Comments.Count > 0
                        ? result.Comments[result.Comments.Count - 1]
                        : null;

                    var info = new ClashResultInfo(
                        result.FileGuid ?? result.Test + "/" + result.Name,
                        result.Item?.Key.A ?? default,
                        result.Item?.Key.B ?? default,
                        result.X, result.Y, result.Z,
                        result.Status.ToString(),
                        result.AssignedTo)
                    {
                        Name = result.Name,
                        GroupName = result.Group,
                        NameA = result.Item?.ModelA,
                        NameB = result.Item?.ModelB,
                        CategoryA = result.Item?.CategoryA,
                        CategoryB = result.Item?.CategoryB,
                        CommentCount = result.Comments.Count,
                        LastComment = last?.Body,
                        Comments = result.Comments,
                        Distance = result.Item?.Distance,
                    };

                    entry.Rows.Add(new Row(result.Test, result.Test, info)
                    {
                        FileItem = result.Item,
                    });
                }

                _file = round;
                Redraw();

                foreach (var warning in read.Warnings) _log.Children.Add(Ui.Note(warning));

                _log.Children.Add(Ui.Line(read.Explain() + " Mark it up below, then save the "
                                          + "markup.", 1, true));
            }

            // -------------------------------------------------------------------------------
            // The options panel
            // -------------------------------------------------------------------------------

            private UIElement Setup()
            {
                var panel = Ui.Stack();

                // The source, first: everything under it reads differently for a file.
                if (_session.Clash.IsAvailable)
                    panel.Children.Add(Bench.Group(string.Empty,
                        Ui.Segmented(new[] { FromModel, FromFile }, _source, pick =>
                        {
                            _source = pick;
                            Ui.Told(Shell.AppWindow.Show("clash", "manage"));
                        })));

                if (OnFile)
                    panel.Children.Add(Bench.Group("The file",
                        Ui.PathField("Open", _path, PathKind.Open,
                                     "Clash XML|*.xml|All files|*.*",
                                     "the baseline a Manage seat exported, or a saved markup"),
                        Ui.Actions(Ui.Primary("Save the markup", SaveMarkup)),
                        Ui.Small("The markup lands in a \"markups\" folder beside the file, named "
                                 + "after you — it never touches the baseline or anyone else's "
                                 + "markup.")));

                var showing = Statuses.Select(status => (UIElement)Ui.Check(status,
                    _show.Contains(status), on =>
                    {
                        if (on) _show.Add(status); else _show.Remove(status);
                        RedrawResults();
                    })).ToList();

                var mark = Ui.Choice(Statuses, _mark, pick => _mark = pick, 150);

                panel.Children.Add(Bench.Group("Show results that are",
                    Ui.Stack(showing.ToArray()),
                    Ui.Check("Only ones with comments", _onlyCommented, on =>
                    {
                        _onlyCommented = on;
                        RedrawResults();
                    })));

                var tests = Bench.Group("Tests",
                    Ui.Field("Find", _find, "matches the test's name"),
                    Ui.Check("Hide tests with no results", _hideEmpty, on =>
                    {
                        _hideEmpty = on;
                        RedrawTests();
                    }),
                    OnFile ? null : Ui.Field("Rename to", _renameTest, "for one picked test"),
                    OnFile ? null : Ui.Field("Move into", _folder, "a clash folder; blank is the root"),
                    OnFile ? null : Ui.Field("Results prefix", _renameResults,
                                             "renames a test's results prefix1, prefix2, ..."));

                panel.Children.Add(tests);

                panel.Children.Add(Bench.Group("On the picked results",
                    Ui.Field("Mark as", mark),
                    Ui.Field("Assign to", _assign, "the project's parties, or type a name"),
                    Ui.Field("Comment", _comment),
                    Ui.Field("Group into", _group, "blank moves them out of any group")));

                panel.Children.Add(Bench.Group("In the model",
                    Ui.Field("Marker size", Ui.Text(
                        _markerAcross.ToString(CultureInfo.InvariantCulture), text =>
                        {
                            if (double.TryParse(text, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var mm) && mm > 0)
                                _markerAcross = mm;
                        }, 90), "millimetres across"),
                    Ui.Actions(Ui.Quiet("Show everything again", () =>
                    {
                        _session.View.ShowAll();
                        Said(Ui.Done("Everything is visible again."));
                    }))));

                panel.Children.Add(Bench.Group("Beyond the engine",
                    Ui.Actions(
                        Ui.Quiet("Find cross-test duplicates", Duplicates),
                        Ui.Quiet("Write the grid to a spreadsheet...", Sheet)),
                    Ui.Actions(Ui.Quiet("Save a baseline file at full fidelity...", SaveBaseline)),
                    Ui.Small("Duplicates are the same conflict reported by two tests - marked "
                             + "Approved with a comment naming the one that stays open. The "
                             + "baseline carries statuses, assignments, comment trails and "
                             + "groups, ready for reviewers.")));

                if (!OnFile)
                {
                    panel.Children.Add(Bench.Group("The review round",
                        Ui.Actions(Ui.Quiet("Send the picked tests out as a file...", () =>
                        {
                            ClashExportWindow.Preferred = _pickedTests.ToList();
                            Ui.Told(Shell.AppWindow.Show("issues", "clashxml"));
                        })),
                        Ui.PathField("Markups", _markups, PathKind.Open,
                                     "Clash XML|*.xml|All files|*.*",
                                     "a markup file, or the baseline — every markup beside it is read"),
                        Ui.Runner("Bring the markups into the model", _sync, primary: false,
                                  work: Sync),
                        _sync,
                        Ui.Small("Reviewers on Simulate open the exported file on this tab, mark "
                                 + "it up, and save. This reads their markups back and writes the "
                                 + "statuses, comments and assignments into the model's own clash "
                                 + "data.")));
                }

                return panel;
            }

            // -------------------------------------------------------------------------------
            // Drawing
            // -------------------------------------------------------------------------------

            private void Redraw()
            {
                RedrawTests();
            }

            private void RedrawTests()
            {
                _tests.Children.Clear();

                var read = Current();
                if (read == null) return;

                var find = _find.Text.Trim();

                var wanted = read
                    .Where(e => find.Length == 0
                                || e.Test.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(e => !_hideEmpty || e.Rows.Count > 0)
                    .ToList();

                _headline.Children.Clear();
                _headline.Children.Add(Bench.Verdict(
                    (OnFile && _file != null
                        ? System.IO.Path.GetFileName(_file.Path) + " — "
                        : string.Empty)
                    + Ui.Count(read.Count, "test") + ", "
                    + Ui.Count(read.Sum(e => e.Rows.Count), "result") + " — "
                    + Ui.Count(read.Sum(e => e.Of("New")), "new") + ", "
                    + read.Sum(e => e.Of("Active")).ToString("N0", CultureInfo.InvariantCulture) + " active, "
                    + read.Sum(e => e.Of("Resolved")).ToString("N0", CultureInfo.InvariantCulture)
                    + " resolved."));

                var rows = wanted.Select(e => new TableRow(e,
                    e.Test.Name,
                    e.Test.Folder ?? string.Empty,
                    Skin.Num(e.Rows.Count),
                    Skin.Num(e.Of("New")),
                    Skin.Num(e.Of("Active")),
                    Skin.Num(e.Of("Reviewed")),
                    Skin.Num(e.Of("Approved")),
                    Skin.Num(e.Of("Resolved")))
                {
                    Tone = e.Of("New") > 0 ? Tone.Warn : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                    ("Test", 220), ("Folder", 90), ("Results", 70), ("New", 55), ("Active", 55),
                    ("Reviewed", 70), ("Approved", 70), ("Resolved", 0));

                table.SelectionChanged += (s, e) =>
                {
                    _pickedTests.Clear();

                    foreach (var picked in table.PickedMany())
                        if (picked is TestEntry entry) _pickedTests.Add(entry.Test.Id);

                    RedrawResults();
                };

                _tests.Children.Add(Ui.Eyebrow("The tests"));

                // A file is a record of a run: tests cannot be run, renamed, moved or deleted in
                // one, and verbs that would say otherwise are not shown rather than shown grey.
                _tests.Children.Add(OnFile
                    ? Ui.Picking(table,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "test")))
                    : Ui.Picking(table,
                        new Ui.OnPicked("Run {n}", RunTests, primary: true),
                        new Ui.OnPicked("Rename {n}", RenameTests),
                        new Ui.OnPicked("Rename {n}'s results", RenameResults),
                        new Ui.OnPicked("Move {n}", MoveTests),
                        new Ui.OnPicked("Delete {n}", DeleteTests, danger: true),
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "test"))));

                RedrawResults();
            }

            private void RedrawResults()
            {
                _results.Children.Clear();

                var read = Current();
                if (read == null) return;

                var from = _pickedTests.Count == 0
                    ? read
                    : read.Where(e => _pickedTests.Contains(e.Test.Id)).ToList();

                var rows = from
                    .SelectMany(e => e.Rows)
                    .Where(r => _show.Contains(r.Status))
                    .Where(r => !_onlyCommented || r.Comments > 0)
                    .ToList();

                _results.Children.Add(Ui.Eyebrow(_pickedTests.Count == 0
                    ? "Every result"
                    : "Results of " + Ui.Count(_pickedTests.Count, "picked test")));

                if (rows.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing matches.",
                        "Every result of "
                        + (_pickedTests.Count == 0 ? "every test" : "the picked tests")
                        + " is filtered out. Untick a status on the right, or pick other tests."));
                    return;
                }

                // The window stays honest about what a table can hold: the verbs below act on
                // what is picked in what is SHOWN, and a cap this size scrolls rather than hangs.
                const int most = 2500;
                var shown = rows.Count > most ? rows.Take(most).ToList() : rows;

                _shown = shown;

                var table = Ui.Table(shown.Select(r => new TableRow(r,
                    r.Info.Name ?? string.Empty,
                    r.TestName,
                    r.Group,
                    r.Status,
                    Worst(r),
                    r.Assigned,
                    r.Comments == 0 ? string.Empty : r.LastComment,
                    r.Info.NameA ?? string.Empty,
                    r.Info.NameB ?? string.Empty)
                {
                    Tone = string.Equals(r.Status, "New", StringComparison.OrdinalIgnoreCase) ? Tone.Bad
                         : string.Equals(r.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(r.Status, "Approved", StringComparison.OrdinalIgnoreCase) ? Tone.Good
                         : Tone.Plain,
                }).ToList(),
                    ("Result", 110), ("Test", 150), ("Group", 90), ("Status", 70),
                    ("Worst", 65), ("Assigned", 90), ("Comment", 140), ("Item 1", 120),
                    ("Item 2", 0));

                _results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Focus {n}", Focus, primary: true),
                    new Ui.OnPicked("Isolate {n}", Isolate),
                    new Ui.OnPicked("Mark {n}", Mark),
                    new Ui.OnPicked("Assign {n}", AssignPicked),
                    new Ui.OnPicked("Comment {n}", CommentPicked),
                    new Ui.OnPicked("Group {n}", GroupPicked),
                    new Ui.OnPicked("Sphere {n}", Spheres),
                    new Ui.OnPicked("Snapshot {n}", Snapshots),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "result"))));

                if (rows.Count > most)
                    _results.Children.Add(Ui.Note(Ui.Count(rows.Count, "result") + " match, and the "
                        + "first " + most.ToString("N0", CultureInfo.InvariantCulture) + " are on "
                        + "screen. Pick a test above, or tighten the status filter, to see the rest."));
            }

            // -------------------------------------------------------------------------------
            // Test verbs (model source only)
            // -------------------------------------------------------------------------------

            private static List<TestEntry> Entries(IReadOnlyList<object> picked) =>
                picked.OfType<TestEntry>().ToList();

            private void RunTests(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                var problems = new List<string>();
                var ran = 0;

                foreach (var entry in tests)
                {
                    if (!_session.Clash.Run(entry.Test.Id))
                    {
                        problems.Add(_session.Clash.LastProblem
                                     ?? "The host would not run \"" + entry.Test.Name + "\".");
                        continue;
                    }

                    ran++;
                }

                if (ran == 0)
                {
                    foreach (var problem in problems) say.Children.Add(Ui.Problem(problem));
                    return;
                }

                _session.Record(ActivityKind.Regroup, "ran " + Ui.Count(ran, "clash test"));

                // The run changed what the document holds, so the cache is stale by definition.
                Reread(Ui.Count(ran, "test") + " ran.", problems);
            }

            private void RenameTests(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                var name = _renameTest.Text.Trim();

                if (tests.Count != 1)
                {
                    say.Children.Add(Ui.Problem("Renaming names one test. Pick exactly one, or use "
                        + "the results prefix for the rows inside one."));
                    return;
                }

                if (name.Length == 0)
                {
                    say.Children.Add(Ui.Problem("Type the new name in \"Rename to\" first."));
                    return;
                }

                if (!_session.Clash.RenameTest(tests[0].Test.Id, name))
                {
                    say.Children.Add(Ui.Problem(_session.Clash.LastProblem ?? "The host refused."));
                    return;
                }

                _session.Record(ActivityKind.Regroup,
                                "renamed the test \"" + tests[0].Test.Name + "\" to \"" + name + "\"");
                Reread("Renamed.", null);
            }

            private void RenameResults(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                var prefix = _renameResults.Text.Trim();

                if (prefix.Length == 0)
                {
                    say.Children.Add(Ui.Problem("Type the prefix in \"Results prefix\" first."));
                    return;
                }

                var done = 0;

                foreach (var entry in tests)
                {
                    if (!_session.Clash.RenameResults(entry.Test.Id, prefix))
                    {
                        say.Children.Add(Ui.Problem(_session.Clash.LastProblem ?? "The host refused."));
                        return;
                    }

                    done++;
                }

                _session.Record(ActivityKind.Regroup, "renamed the results of "
                    + Ui.Count(done, "test") + " to \"" + prefix + "...\"");
                Reread("Renamed the results of " + Ui.Count(done, "test") + ".", null);
            }

            private void MoveTests(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                var folder = _folder.Text.Trim();

                if (!_session.Clash.MoveTests(tests.Select(t => t.Test.Id).ToList(),
                                              folder.Length == 0 ? null : folder))
                {
                    say.Children.Add(Ui.Problem(_session.Clash.LastProblem ?? "The host refused."));
                    return;
                }

                _session.Record(ActivityKind.Regroup, "moved " + Ui.Count(tests.Count, "clash test")
                    + (folder.Length == 0 ? " to the root" : " into \"" + folder + "\""));
                Reread("Moved " + Ui.Count(tests.Count, "test") + ".", null);
            }

            private void DeleteTests(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                if (tests.Count == 0) return;

                // Two presses for the one verb here that cannot be undone.
                say.Children.Add(Ui.Danger("Yes, delete " + Ui.Count(tests.Count, "test")
                    + " and every result in them", () =>
                {
                    say.Children.Clear();

                    if (!_session.Clash.DeleteTests(tests.Select(t => t.Test.Id).ToList()))
                    {
                        say.Children.Add(Ui.Problem(_session.Clash.LastProblem ?? "The host refused."));
                        return;
                    }

                    _session.Record(ActivityKind.Regroup,
                                    "deleted " + Ui.Count(tests.Count, "clash test"));
                    Reread("Deleted " + Ui.Count(tests.Count, "test") + ".", null);
                }));
            }

            // -------------------------------------------------------------------------------
            // Result verbs
            // -------------------------------------------------------------------------------

            private static List<Row> Rows(IReadOnlyList<object> picked) =>
                picked.OfType<Row>().ToList();

            /// <summary>The element keys behind the picked rows, minus the ones a file could not name.</summary>
            private static List<ElementKey> Keys(List<Row> rows) =>
                rows.SelectMany(r => new[] { r.Info.A, r.Info.B })
                    .Where(k => !k.IsEmpty)
                    .Distinct()
                    .ToList();

            private void Focus(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = Keys(rows);

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Those results name no elements this federation "
                        + "has, so there is nothing to look at."));
                    return;
                }

                _session.Model.Select(keys);

                // One result from the model gets the host's own composed view of it - the frame
                // the engine saved when the test ran, which reads like the native clash window.
                // More than one, or a file row, gets a fit around everything picked.
                if (!OnFile && rows.Count == 1
                    && _session.Clash.ApplyResultView(rows[0].TestId, rows[0].Info.Id))
                {
                    say.Children.Add(Ui.Done("Showing the engine's own view of \""
                        + (rows[0].Info.Name ?? "the result") + "\"."));
                    return;
                }

                _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done("Selected and framed "
                    + Ui.Count(keys.Count, "element") + "."));
            }

            private void Isolate(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = Keys(rows);

                if (keys.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Those results name no elements this federation "
                        + "has, so there is nothing to isolate."));
                    return;
                }

                _session.View.Isolate(keys);
                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done(Ui.Count(keys.Count, "element") + " isolated. "
                    + "\"Show everything again\" on the right puts the rest back."));
            }

            private void Mark(IReadOnlyList<object> picked, Panel say) =>
                Write(picked, say, "Marked {0} as " + _mark + ".",
                      (testId, ids) => _session.Clash.SetStatus(testId, ids, _mark),
                      row => row.Status = _mark);

            private void AssignPicked(IReadOnlyList<object> picked, Panel say)
            {
                var who = (_assign.Text ?? string.Empty).Trim();

                Write(picked, say,
                      who.Length == 0 ? "Cleared the assignment on {0}." : "Assigned {0} to " + who + ".",
                      (testId, ids) => _session.Clash.Assign(testId, ids, who),
                      row => row.Assigned = who);
            }

            private void CommentPicked(IReadOnlyList<object> picked, Panel say)
            {
                var body = _comment.Text.Trim();

                if (body.Length == 0)
                {
                    say.Children.Clear();
                    say.Children.Add(Ui.Problem("Type the comment in the panel first."));
                    return;
                }

                var author = Environment.UserName ?? "reviewer";

                Write(picked, say, "Commented on {0}.",
                      (testId, ids) => _session.Clash.Comment(testId, ids, body, author),
                      row =>
                      {
                          row.Comments++;
                          row.LastComment = body;
                          row.Trail.Add(new ClashComment(author, DateTime.UtcNow.Ticks, body));
                      });
            }

            private void GroupPicked(IReadOnlyList<object> picked, Panel say)
            {
                var group = _group.Text.Trim();

                Write(picked, say,
                      group.Length == 0 ? "Moved {0} out of their groups." : "Grouped {0} into \"" + group + "\".",
                      (testId, ids) => _session.Clash.Regroup(testId, ids, group),
                      row => row.Group = group);
            }

            /// <summary>
            /// One write over the picked rows, test by test, with the cache kept truthful.
            ///
            /// From the model, the host is asked first and the cached rows change only after it
            /// said yes. From a file, the rows ARE the data, so the edit lands directly and the
            /// save writes it out — that is what a markup is.
            /// </summary>
            private void Write(IReadOnlyList<object> picked, Panel say, string didFormat,
                               Func<string, IReadOnlyList<string>, bool> write, Action<Row> apply)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var done = 0;
                var problems = new List<string>();

                if (OnFile)
                {
                    foreach (var row in rows) apply(row);
                    done = rows.Count;
                }
                else
                {
                    foreach (var byTest in rows.GroupBy(r => r.TestId, StringComparer.OrdinalIgnoreCase))
                    {
                        var ids = byTest.Select(r => r.Info.Id).ToList();

                        if (!write(byTest.Key, ids))
                        {
                            problems.Add(_session.Clash.LastProblem ?? "The host refused.");
                            continue;
                        }

                        foreach (var row in byTest) apply(row);
                        done += ids.Count;
                    }
                }

                if (done == 0)
                {
                    foreach (var problem in problems) say.Children.Add(Ui.Problem(problem));
                    return;
                }

                if (!OnFile)
                    _session.Record(ActivityKind.Regroup,
                                    string.Format(CultureInfo.InvariantCulture, didFormat,
                                                  Ui.Count(done, "result")));

                // Redrawing replaces the table whose bar the message panel lived in, so the
                // outcome goes to the screen's own log, which survives.
                RedrawTests();
                Tell(string.Format(CultureInfo.InvariantCulture, didFormat,
                                   Ui.Count(done, "result"))
                     + (OnFile ? " Save the markup to keep it." : string.Empty), problems);
            }

            private void Spheres(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                // Model results arrive in document units; a file's are already metres.
                var scale = OnFile ? 1.0 : _session.MetresPerUnit;

                var items = rows.Select(r => new ClashItem(r.Info.Key(), r.TestName)
                {
                    X = r.Info.PointX * scale,
                    Y = r.Info.PointY * scale,
                    Z = r.Info.PointZ * scale,
                    Status = Enum.TryParse<FindingStatus>(r.Status, true, out var parsed)
                        ? parsed
                        : FindingStatus.New,
                }).ToList();

                var recipe = new MarkerRecipe
                {
                    RadiusMetres = _markerAcross / 2000.0,

                    // The coordinates above are already metres, so no further conversion.
                    MetresPerUnit = 1.0,
                };

                var mesh = ClashMarkers.Write(items, recipe);

                if (mesh.Refusal != null)
                {
                    say.Children.Add(Ui.Problem(mesh.Refusal));
                    return;
                }

                var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "camelworks-markers");
                var stamp = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
                var obj = System.IO.Path.Combine(folder, "clash-spheres-" + stamp + ".obj");
                var mtl = System.IO.Path.ChangeExtension(obj, ".mtl");

                try
                {
                    System.IO.Directory.CreateDirectory(folder);

                    System.IO.File.WriteAllText(obj, mesh.Text.Replace(
                        "mtllib " + mesh.MaterialFile,
                        "mtllib " + System.IO.Path.GetFileName(mtl)));

                    if (mesh.Materials.Length > 0) System.IO.File.WriteAllText(mtl, mesh.Materials);
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not write the markers: " + Ui.Describe(e)));
                    return;
                }

                try
                {
                    say.Children.Add(_session.Model.Document.TryAppendFile(obj)
                        ? Ui.Done(Ui.Count(rows.Count, "sphere") + " dropped at the clash points, "
                            + "coloured by status. They are ordinary geometry: take them out again "
                            + "from the host's file list, where the file is called "
                            + System.IO.Path.GetFileName(obj) + ".")
                        : (UIElement)Ui.Problem("The host would not append the marker file."));
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not append the markers: " + Ui.Describe(e)));
                }
            }

            /// <summary>The result's worst distance, told in honest units, or nothing.</summary>
            private string Worst(Row row)
            {
                if (!row.Info.Distance.HasValue) return string.Empty;

                var metres = Math.Abs(row.Info.Distance.Value) * (OnFile ? 1.0 : _session.MetresPerUnit);

                return metres >= 1
                    ? metres.ToString("0.00", CultureInfo.InvariantCulture) + " m"
                    : (metres * 1000).ToString("0", CultureInfo.InvariantCulture) + " mm";
            }

            /// <summary>
            /// The host's rendered image of each picked result - scene plus highlight - written
            /// as PNGs beside the project file, where a report or an email can pick them up.
            /// </summary>
            private void Snapshots(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                if (OnFile)
                {
                    say.Children.Add(Ui.Problem("Snapshots are rendered by the clash engine, so "
                        + "they need the model source."));
                    return;
                }

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var project = _session.Store.Path;
                var beside = string.IsNullOrWhiteSpace(project)
                    ? System.IO.Path.GetTempPath()
                    : System.IO.Path.GetDirectoryName(project) ?? System.IO.Path.GetTempPath();
                var folder = System.IO.Path.Combine(beside, "clash-snapshots");

                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not make the folder: " + Ui.Describe(e)));
                    return;
                }

                var written = 0;

                foreach (var row in rows)
                {
                    var bytes = _session.Clash.ResultSnapshot(row.TestId, row.Info.Id, 1280, 720);
                    if (bytes == null) continue;

                    var name = Safe(row.TestName) + "_" + Safe(row.Info.Name ?? row.Info.Id) + ".png";

                    try
                    {
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name), bytes);
                        written++;
                    }
                    catch (Exception e)
                    {
                        say.Children.Add(Ui.Problem(name + ": " + Ui.Describe(e)));
                    }
                }

                say.Children.Add(written == 0
                    ? (UIElement)Ui.Problem(_session.Clash.LastProblem
                        ?? "The host rendered none of them.")
                    : Ui.Done(Ui.Count(written, "snapshot") + " written to " + folder + "."));
            }

            /// <summary>
            /// Find the same conflict reported by more than one test - the thing no native clash
            /// window can do, and the reason a board says 214 where the tests say 18,844.
            /// </summary>
            private void Duplicates()
            {
                var read = Current();

                if (read == null)
                {
                    Said(Ui.Problem("Read the results first."));
                    return;
                }

                var of = new Dictionary<ClashItem, Row>();
                var items = new List<ClashItem>();
                var scale = OnFile ? 1.0 : _session.MetresPerUnit;

                foreach (var row in read.SelectMany(e => e.Rows))
                {
                    if (row.Info.A.IsEmpty || row.Info.B.IsEmpty) continue;

                    var item = new ClashItem(row.Info.Key(), row.TestName)
                    {
                        X = row.Info.PointX * scale,
                        Y = row.Info.PointY * scale,
                        Z = row.Info.PointZ * scale,
                    };

                    of[item] = row;
                    items.Add(item);
                }

                var collapse = DuplicateCollapse.Across(items, _session.Profile.DuplicateBand);

                if (collapse.Count == 0)
                {
                    Said(Ui.Done("No two tests report the same conflict."));
                    return;
                }

                _log.Children.Clear();
                _log.Children.Add(Ui.Done(Ui.Count(collapse.Count, "result") + " are the same "
                    + "conflict found by more than one test - the same pipe through the same "
                    + "wall, counted twice."));

                _log.Children.Add(Ui.Actions(Ui.Quiet(
                    "Mark the duplicates Approved, naming the keeper", () =>
                {
                    var marked = 0;

                    foreach (var pair in collapse.FoldedInto)
                    {
                        if (!of.TryGetValue(pair.Key, out var dupe)) continue;
                        if (!of.TryGetValue(pair.Value, out var keeper)) continue;

                        var note = "Duplicate of " + keeper.TestName + " / "
                                   + (keeper.Info.Name ?? "its result") + ".";

                        if (OnFile)
                        {
                            dupe.Status = "Approved";
                            dupe.Comments++;
                            dupe.LastComment = note;
                            dupe.Trail.Add(new ClashComment(
                                Environment.UserName ?? "reviewer", DateTime.UtcNow.Ticks, note));
                            marked++;
                            continue;
                        }

                        var one = new[] { dupe.Info.Id };

                        if (!_session.Clash.SetStatus(dupe.TestId, one, "Approved")) continue;

                        _session.Clash.Comment(dupe.TestId, one, note,
                                               Environment.UserName ?? "reviewer");

                        dupe.Status = "Approved";
                        dupe.Comments++;
                        dupe.LastComment = note;
                        marked++;
                    }

                    if (!OnFile && marked > 0)
                        _session.Record(ActivityKind.Regroup, "approved "
                            + Ui.Count(marked, "cross-test duplicate") + ", each naming its keeper");

                    RedrawTests();
                    Tell("Marked " + Ui.Count(marked, "duplicate") + " Approved, each with a "
                         + "comment naming the result that stays open."
                         + (OnFile ? " Save the markup to keep it." : string.Empty), null);
                })));
            }

            /// <summary>
            /// Write the current state - picked tests, or everything - as a baseline file, at
            /// full fidelity: statuses, assignees, the whole comment trail, and the groups.
            /// </summary>
            private void SaveBaseline()
            {
                var read = Current();

                if (read == null)
                {
                    Said(Ui.Problem("Read the results first."));
                    return;
                }

                var from = _pickedTests.Count == 0
                    ? read
                    : read.Where(e => _pickedTests.Contains(e.Test.Id)).ToList();

                var items = new List<ClashItem>();
                var scale = OnFile ? 1.0 : _session.MetresPerUnit;

                foreach (var row in from.SelectMany(e => e.Rows))
                {
                    if (row.Info.A.IsEmpty || row.Info.B.IsEmpty) continue;

                    var item = new ClashItem(row.Info.Key(), row.TestName)
                    {
                        X = row.Info.PointX * scale,
                        Y = row.Info.PointY * scale,
                        Z = row.Info.PointZ * scale,
                        Status = Enum.TryParse<FindingStatus>(row.Status, true, out var status)
                            ? status
                            : FindingStatus.New,
                        AssignedTo = row.Assigned.Length == 0 ? null : row.Assigned,
                        CarriedGroup = row.Group.Length == 0 ? null : row.Group,
                        CategoryA = row.Info.CategoryA,
                        CategoryB = row.Info.CategoryB,
                        Distance = row.Info.Distance,
                    };

                    foreach (var comment in row.Trail) item.Comments.Add(comment);

                    items.Add(item);
                }

                if (items.Count == 0)
                {
                    Said(Ui.Problem("Nothing here can be written: no result names elements by key."));
                    return;
                }

                var written = ClashXml.Write(items, null, new ClashXmlOptions
                {
                    SourceFile = _session.Store.Path,
                    WhenTicks = DateTime.UtcNow.Ticks,
                });

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the baseline",
                    Filter = "Clash XML|*.xml",
                    FileName = "clash-baseline.xml",
                    AddExtension = true,
                    DefaultExt = ".xml",
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllText(dialog.FileName, written.Text);
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote a clash baseline", written.Explain());

                Said(Ui.Done("Wrote " + dialog.FileName + " - " + written.Explain()
                    + " Statuses, assignments, comment trails and groups all travel in it. "
                    + "Reviewers open it on this tab and save their markups beside it."));
            }

            /// <summary>The results grid, exactly as filtered and shown, as a spreadsheet.</summary>
            private void Sheet()
            {
                if (_shown.Count == 0)
                {
                    Said(Ui.Problem("Nothing is on the grid to write."));
                    return;
                }

                var block = new TableBlock("Test", "Result", "Group", "Status", "Worst",
                                           "Assigned", "Comments", "Latest comment",
                                           "Item 1", "Item 2");

                foreach (var row in _shown)
                    block.Row(row.TestName, row.Info.Name ?? string.Empty, row.Group, row.Status,
                              Worst(row), row.Assigned,
                              row.Comments.ToString(CultureInfo.InvariantCulture),
                              row.LastComment, row.Info.NameA ?? string.Empty,
                              row.Info.NameB ?? string.Empty);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Write the results to a spreadsheet",
                    Filter = "Excel|*.xlsx",
                    FileName = "clash-results.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName,
                                                 XlsxWriter.Write("Clash results", block));
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the clash results grid",
                                Ui.Count(_shown.Count, "row") + " to " + dialog.FileName);

                Said(Ui.Done("Wrote " + Ui.Count(_shown.Count, "row") + " to "
                             + dialog.FileName + " - the grid exactly as it is filtered."));
            }

            // -------------------------------------------------------------------------------
            // The review round: save a markup, sync markups back
            // -------------------------------------------------------------------------------

            /// <summary>
            /// Write the file source's current state as this reviewer's markup.
            ///
            /// To <c>markups\&lt;reviewer&gt;.xml</c> beside the file it was read from — the same
            /// convention every reviewer follows, so the sync can find everybody's file and nobody
            /// can overwrite anybody else's.
            /// </summary>
            private void SaveMarkup()
            {
                var round = _file;

                if (round == null)
                {
                    Said(Ui.Problem("Read a file first — the markup is its edited state."));
                    return;
                }

                var items = new List<ClashItem>();
                var skipped = 0;

                foreach (var row in round.Entries.SelectMany(e => e.Rows))
                {
                    var item = row.FileItem;

                    if (item == null)
                    {
                        // A result with no CamelWorks key cannot land back on the model, so a
                        // markup of it would be a record of nothing. Counted, not hidden.
                        skipped++;
                        continue;
                    }

                    item.Status = Enum.TryParse<FindingStatus>(row.Status, true, out var status)
                        ? status
                        : FindingStatus.New;
                    item.AssignedTo = row.Assigned.Length == 0 ? null : row.Assigned;
                    item.CarriedGroup = row.Group.Length == 0 ? null : row.Group;

                    item.Comments.Clear();
                    foreach (var comment in row.Trail) item.Comments.Add(comment);

                    items.Add(item);
                }

                if (items.Count == 0)
                {
                    Said(Ui.Problem("Nothing in that file carries a CamelWorks key, so a markup "
                        + "of it could never land back on the model."));
                    return;
                }

                var written = ClashXml.Write(items, null, new ClashXmlOptions
                {
                    SourceFile = round.Path,
                    WhenTicks = DateTime.UtcNow.Ticks,
                });

                var reviewer = Safe(Environment.UserName ?? "reviewer");
                var beside = System.IO.Path.GetDirectoryName(round.Path) ?? string.Empty;
                var folder = System.IO.Path.Combine(beside, "markups");
                var path = System.IO.Path.Combine(folder, reviewer + ".xml");

                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                    System.IO.File.WriteAllText(path, written.Text);
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write the markup: " + Ui.Describe(e)));
                    return;
                }

                var note = "Saved your markup: " + path + " — " + written.Explain()
                           + (skipped > 0
                               ? " " + Ui.Count(skipped, "unkeyed result") + " could not be "
                                 + "included."
                               : string.Empty);

                Said(Ui.Done(note + " Send it back, or leave it there for the sync to find."));
            }

            /// <summary>
            /// Read the reviewers' markups and write their answers into the model.
            ///
            /// Every markup beside the named file is read, oldest save first, so where two
            /// reviewers disagree the later save wins — and every authored comment from every
            /// file survives, because the adapter refuses to write the same author saying the
            /// same thing twice.
            /// </summary>
            private void Sync(Job job)
            {
                var named = _markups.Text.Trim();

                if (named.Length == 0)
                {
                    _sync.Children.Add(Ui.Problem("Point at a markup file, or at the baseline "
                        + "beside the markups folder, first."));
                    return;
                }

                var files = MarkupFiles(named);

                if (files.Count == 0)
                {
                    _sync.Children.Add(Ui.Problem("There is no markup to read there."));
                    return;
                }

                job.Say("Reading the model...");

                // The model's results, keyed the same way the files are: the stable clash key.
                var at = new Dictionary<string, (string TestId, string ResultId, string Status,
                                                 string Assigned, string Group)>(StringComparer.Ordinal);

                var read = 0;

                foreach (var test in _session.Clash.Tests())
                {
                    foreach (var result in _session.Clash.Results(test.Id))
                    {
                        if (!job.Step(++read, "results read")) return;

                        at[result.Key().ToString()] = (test.Id, result.Id,
                                                       result.HostStatus ?? "New",
                                                       result.HostAssignedTo ?? string.Empty,
                                                       result.GroupName ?? string.Empty);
                    }
                }

                var statuses = 0;
                var assigns = 0;
                var comments = 0;
                var groups = 0;
                var missing = 0;
                var problems = new List<string>();

                foreach (var file in files)
                {
                    job.Say("Reading " + System.IO.Path.GetFileName(file) + "...");

                    string text;

                    try
                    {
                        text = System.IO.File.ReadAllText(file);
                    }
                    catch (Exception e)
                    {
                        problems.Add(System.IO.Path.GetFileName(file) + ": " + Ui.Describe(e));
                        continue;
                    }

                    var markup = ClashXml.Read(text);

                    if (!markup.IsUsable)
                    {
                        problems.Add(System.IO.Path.GetFileName(file) + ": " + markup.Explain());
                        continue;
                    }

                    // Gathered per test so each write is one seam call, then applied.
                    var setStatus = new Dictionary<(string Test, string Status), List<string>>();
                    var setAssign = new Dictionary<(string Test, string Who), List<string>>();
                    var setGroup = new Dictionary<(string Test, string Group), List<string>>();
                    var say = new List<(string Test, string Id, ClashComment Comment)>();

                    foreach (var result in markup.Results)
                    {
                        if (result.Item == null) continue;

                        if (!at.TryGetValue(result.Item.Key.ToString(), out var found))
                        {
                            missing++;
                            continue;
                        }

                        var status = result.Status.ToString();

                        if (!string.Equals(status, found.Status, StringComparison.OrdinalIgnoreCase))
                            Bucket(setStatus, (found.TestId, status), found.ResultId);

                        var who = result.AssignedTo ?? string.Empty;

                        if (who.Length > 0
                            && !string.Equals(who, found.Assigned, StringComparison.Ordinal))
                            Bucket(setAssign, (found.TestId, who), found.ResultId);

                        var band = result.Item.CarriedGroup ?? result.Group ?? string.Empty;

                        if (!string.Equals(band, found.Group, StringComparison.Ordinal))
                            Bucket(setGroup, (found.TestId, band), found.ResultId);

                        foreach (var comment in result.Comments)
                            say.Add((found.TestId, found.ResultId, comment));

                        // Later files see the state this one just asked for.
                        at[result.Item.Key.ToString()] =
                            (found.TestId, found.ResultId, status,
                             who.Length > 0 ? who : found.Assigned, band);
                    }

                    foreach (var batch in setStatus)
                    {
                        if (_session.Clash.SetStatus(batch.Key.Test, batch.Value, batch.Key.Status))
                            statuses += batch.Value.Count;
                        else
                            problems.Add(_session.Clash.LastProblem ?? "A status write was refused.");
                    }

                    foreach (var batch in setAssign)
                    {
                        if (_session.Clash.Assign(batch.Key.Test, batch.Value, batch.Key.Who))
                            assigns += batch.Value.Count;
                        else
                            problems.Add(_session.Clash.LastProblem ?? "An assignment was refused.");
                    }

                    foreach (var batch in setGroup)
                    {
                        if (_session.Clash.Regroup(batch.Key.Test, batch.Value, batch.Key.Group))
                            groups += batch.Value.Count;
                        else
                            problems.Add(_session.Clash.LastProblem ?? "A regroup was refused.");
                    }

                    foreach (var (testId, id, comment) in say)
                    {
                        // The adapter skips a comment the result already carries word for word,
                        // so re-running a sync never doubles the conversation.
                        if (_session.Clash.Comment(testId, new[] { id }, comment.Body, comment.Author))
                            comments++;
                    }
                }

                _session.Record(ActivityKind.Regroup, "synced " + Ui.Count(files.Count, "markup")
                    + " into the model", statuses + " statuses, " + comments + " comments, "
                    + assigns + " assignments");

                Reread("Synced " + Ui.Count(files.Count, "markup") + ": "
                    + Ui.Count(statuses, "status change") + ", "
                    + Ui.Count(comments, "comment") + ", "
                    + Ui.Count(assigns, "assignment") + ", "
                    + Ui.Count(groups, "regrouped result") + "."
                    + (missing > 0
                        ? " " + Ui.Count(missing, "result") + " in the markups are not in the "
                          + "model any more — usually resolved and gone from a newer run."
                        : string.Empty), problems);
            }

            /// <summary>
            /// The markup files a name points at: everything in the markups folder beside it,
            /// oldest save first, or just the file itself when there is no such folder.
            /// </summary>
            private static List<string> MarkupFiles(string named)
            {
                var files = new List<string>();

                try
                {
                    var beside = System.IO.Path.GetDirectoryName(named) ?? string.Empty;

                    // Pointing at the baseline finds markups/ beside it; pointing at a file
                    // inside markups/ finds its siblings; pointing anywhere else syncs that file.
                    var folder = string.Equals(System.IO.Path.GetFileName(beside), "markups",
                                               StringComparison.OrdinalIgnoreCase)
                        ? beside
                        : System.IO.Path.Combine(beside, "markups");

                    if (System.IO.Directory.Exists(folder))
                        files.AddRange(System.IO.Directory.GetFiles(folder, "*.xml")
                            .OrderBy(System.IO.File.GetLastWriteTimeUtc));

                    if (files.Count == 0 && System.IO.File.Exists(named)) files.Add(named);
                }
                catch (Exception)
                {
                    // An unreadable path reads as no markup, and the caller says so.
                }

                return files;
            }

            private static void Bucket<TKey>(Dictionary<TKey, List<string>> into, TKey key, string id)
                where TKey : notnull
            {
                if (!into.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    into[key] = list;
                }

                list.Add(id);
            }

            private static string Safe(string name)
            {
                foreach (var wrong in System.IO.Path.GetInvalidFileNameChars())
                    name = name.Replace(wrong, '_');

                return string.IsNullOrWhiteSpace(name) ? "reviewer" : name;
            }

            // -------------------------------------------------------------------------------

            /// <summary>
            /// Re-read from the document and redraw, then say what happened in the screen's own
            /// log — the panel the verb's bar offered is destroyed by the redraw, so a message
            /// written there would never be seen.
            /// </summary>
            private void Reread(string did, List<string>? problems)
            {
                _read = null;

                var entries = new List<TestEntry>();

                foreach (var test in _session.Clash.Tests())
                {
                    var entry = new TestEntry(test);

                    foreach (var result in _session.Clash.Results(test.Id))
                        entry.Rows.Add(new Row(test.Id, test.Name, result));

                    entries.Add(entry);
                }

                _read = entries;
                RedrawTests();

                Tell(did, problems);
            }

            private void Tell(string did, List<string>? problems)
            {
                _log.Children.Clear();
                _log.Children.Add(Ui.Done(did));

                foreach (var problem in problems ?? new List<string>())
                    _log.Children.Add(Ui.Problem(problem));
            }

            private void Said(UIElement note)
            {
                _log.Children.Clear();
                _log.Children.Add(note);
            }

            private List<string> Assignees()
            {
                var names = new List<string>();

                foreach (var party in _session.Profile.Parties)
                    if (!names.Contains(party, StringComparer.OrdinalIgnoreCase)) names.Add(party);

                foreach (var entry in Current() ?? new List<TestEntry>())
                    foreach (var row in entry.Rows)
                        if (row.Assigned.Length > 0
                            && !names.Contains(row.Assigned, StringComparer.OrdinalIgnoreCase))
                            names.Add(row.Assigned);

                return names;
            }

            /// <summary>An editable combo: the suggestions are offered, the text stays free.</summary>
            private static ComboBox Suggesting(IReadOnlyList<string> options)
            {
                var combo = new ComboBox
                {
                    IsEditable = true,
                    Width = 170,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };

                foreach (var option in options) combo.Items.Add(option);

                return combo;
            }
        }
    }
}
