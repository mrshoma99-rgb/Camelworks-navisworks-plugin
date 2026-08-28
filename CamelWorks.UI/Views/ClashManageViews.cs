using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The results, the way the engine holds them — and the pen to work them.
    ///
    /// The Board derives issues; this tab manages results. They are different jobs. A coordinator
    /// in a Friday meeting is asked "what is the status of clash 214 in test A2B4", and the answer
    /// has to be read, changed, commented and assigned <i>in the model's own clash data</i>, so
    /// that the native clash window, the exports and every other tool see the same thing. Nothing
    /// here is a CamelWorks-only overlay: every edit writes through to the document.
    ///
    /// The anatomy is the one every clash manager converges on, because it is right: tests on top
    /// with their live status counts, the picked tests' results under them, and verbs on the
    /// picked rows. Camera, selection, isolation and markers hang off the same rows, so "show me"
    /// is one press from "mark it reviewed".
    /// </summary>
    public static class ClashManageViews
    {
        private static readonly string[] Statuses = { "New", "Active", "Reviewed", "Approved", "Resolved" };

        // The read survives tab switches and window closes, like the board does: re-reading
        // eighteen thousand results because somebody glanced at the Rules tab would make the tab
        // switch cost more than the work.
        private static List<TestEntry>? _read;

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Manage(Session session) => new Screen(session).Build();

        /// <summary>Forget the cached read, so the next visit reads afresh.</summary>
        internal static void Forget() => _read = null;

        // -----------------------------------------------------------------------------------

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
            }

            internal string TestId { get; }
            internal string TestName { get; }
            internal ClashResultInfo Info { get; }
            internal string Status { get; set; }
            internal string Assigned { get; set; }
            internal string Group { get; set; }
            internal int Comments { get; set; }
            internal string LastComment { get; set; }
        }

        // -----------------------------------------------------------------------------------

        private sealed class Screen
        {
            private readonly Session _session;

            private readonly StackPanel _tests = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _headline = Ui.Stack();

            private readonly TextBox _find;
            private readonly TextBox _comment;
            private readonly TextBox _group;
            private readonly TextBox _renameTest;
            private readonly TextBox _renameResults;
            private readonly TextBox _folder;
            private readonly ComboBox _assign;

            private readonly HashSet<string> _show =
                new HashSet<string>(Statuses, StringComparer.OrdinalIgnoreCase);

            private string _mark = "Reviewed";
            private bool _hideEmpty;
            private bool _onlyCommented;
            private double _markerAcross = 300;

            private ListView? _testsTable;
            private readonly HashSet<string> _pickedTests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal Screen(Session session)
            {
                _session = session;
                _find = Ui.Text(string.Empty, null, 150);
                _comment = Ui.Text(string.Empty, null, 170);
                _group = Ui.Text(string.Empty, null, 170);
                _renameTest = Ui.Text(string.Empty, null, 170);
                _renameResults = Ui.Text(string.Empty, null, 170);
                _folder = Ui.Text(string.Empty, null, 170);
                _assign = Suggesting(Assignees());
                _find.TextChanged += (s, e) => RedrawTests();
            }

            internal UIElement Build()
            {
                if (!_session.Clash.IsAvailable)
                {
                    return Bench.Surface(null, null, Ui.Stack(
                        Ui.Empty("This Navisworks edition has no clash engine to manage.",
                            (_session.ClashProblem ?? "The clash module is not present.") + " The "
                            + "Board can still open the last recorded run for triage, assignment "
                            + "and issuing.",
                            "Open the Board", () => Ui.Told(Shell.AppWindow.Show("clash", "triage")))));
                }

                var read = Ui.Runner("Read the results", _log, Read);

                // The log sits above the tables: a verb's outcome must land where the eye already
                // is, not below two thousand rows of what it just acted on.
                var stage = Ui.Stack(
                    _headline,
                    _log,
                    _tests,
                    Ui.Rule(),
                    _results);

                if (_read == null)
                {
                    _headline.Children.Add(Bench.Ready(
                        "Reading pulls every test and every result out of the document, with "
                        + "status, assignee, comments and grouping — then the whole board of "
                        + "verbs works on whatever you pick.",
                        "Every edit writes back into the model's own clash data, so the native "
                        + "clash window and every export see the same thing."));
                }
                else
                {
                    Redraw();
                }

                return Bench.Surface(Setup(), read, stage);
            }

            // -------------------------------------------------------------------------------
            // Reading
            // -------------------------------------------------------------------------------

            private void Read(Job job)
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

            // -------------------------------------------------------------------------------
            // The options panel
            // -------------------------------------------------------------------------------

            private UIElement Setup()
            {
                var showing = Statuses.Select(status => (UIElement)Ui.Check(status,
                    _show.Contains(status), on =>
                    {
                        if (on) _show.Add(status); else _show.Remove(status);
                        RedrawResults();
                    })).ToList();

                var mark = Ui.Choice(Statuses, _mark, pick => _mark = pick, 150);

                return Ui.Stack(
                    Bench.Group("Show results that are",
                        Ui.Stack(showing.ToArray()),
                        Ui.Check("Only ones with comments", _onlyCommented, on =>
                        {
                            _onlyCommented = on;
                            RedrawResults();
                        })),
                    Bench.Group("Tests",
                        Ui.Field("Find", _find, "matches the test's name"),
                        Ui.Check("Hide tests with no results", _hideEmpty, on =>
                        {
                            _hideEmpty = on;
                            RedrawTests();
                        }),
                        Ui.Field("Rename to", _renameTest, "for one picked test"),
                        Ui.Field("Move into", _folder, "a clash folder; blank is the root"),
                        Ui.Field("Results prefix", _renameResults,
                                 "renames a test's results prefix1, prefix2, ...")),
                    Bench.Group("On the picked results",
                        Ui.Field("Mark as", mark),
                        Ui.Field("Assign to", _assign, "the project's parties, or type a name"),
                        Ui.Field("Comment", _comment),
                        Ui.Field("Group into", _group, "blank moves them out of any group")),
                    Bench.Group("In the model",
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
                        }))),
                    Bench.Group("Out of here",
                        Ui.Actions(Ui.Quiet("Send the picked tests out as a file...", () =>
                        {
                            ClashExportWindow.Preferred = _pickedTests.ToList();
                            Ui.Told(Shell.AppWindow.Show("issues", "clashxml"));
                        })),
                        Ui.Small("The file leg — XML out to whoever asked, and their answers "
                                 + "back in — lives on Issue Link, one press away.")));
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

                var read = _read;
                if (read == null) return;

                var find = _find.Text.Trim();

                var wanted = read
                    .Where(e => find.Length == 0
                                || e.Test.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(e => !_hideEmpty || e.Rows.Count > 0)
                    .ToList();

                _headline.Children.Clear();
                _headline.Children.Add(Bench.Verdict(
                    Ui.Count(read.Count, "test") + ", "
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

                _testsTable = table;

                table.SelectionChanged += (s, e) =>
                {
                    _pickedTests.Clear();

                    foreach (var picked in table.PickedMany())
                        if (picked is TestEntry entry) _pickedTests.Add(entry.Test.Id);

                    RedrawResults();
                };

                _tests.Children.Add(Ui.Eyebrow("The tests"));

                _tests.Children.Add(Ui.Picking(table,
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

                var read = _read;
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

                var table = Ui.Table(shown.Select(r => new TableRow(r,
                    r.Info.Name ?? string.Empty,
                    r.TestName,
                    r.Group,
                    r.Status,
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
                    ("Assigned", 90), ("Comment", 150), ("Item 1", 130), ("Item 2", 0));

                _results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Focus {n}", Focus, primary: true),
                    new Ui.OnPicked("Isolate {n}", Isolate),
                    new Ui.OnPicked("Mark {n}", Mark),
                    new Ui.OnPicked("Assign {n}", AssignPicked),
                    new Ui.OnPicked("Comment {n}", CommentPicked),
                    new Ui.OnPicked("Group {n}", GroupPicked),
                    new Ui.OnPicked("Sphere {n}", Spheres),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "result"))));

                if (rows.Count > most)
                    _results.Children.Add(Ui.Note(Ui.Count(rows.Count, "result") + " match, and the "
                        + "first " + most.ToString("N0", CultureInfo.InvariantCulture) + " are on "
                        + "screen. Pick a test above, or tighten the status filter, to see the rest."));
            }

            // -------------------------------------------------------------------------------
            // Test verbs
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

            private void Focus(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = rows.SelectMany(r => new[] { r.Info.A, r.Info.B }).Distinct().ToList();

                _session.Model.Select(keys);
                _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done("Selected and framed "
                    + Ui.Count(keys.Count, "element") + "."));
            }

            private void Isolate(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = rows.SelectMany(r => new[] { r.Info.A, r.Info.B }).Distinct().ToList();

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
            /// The mutation is applied to the cached rows only after the host said yes for their
            /// test, so a refusal on one test never shows as done — and nothing needs a full
            /// re-read for an edit the screen just watched happen.
            /// </summary>
            private void Write(IReadOnlyList<object> picked, Panel say, string didFormat,
                               Func<string, IReadOnlyList<string>, bool> write, Action<Row> apply)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var done = 0;
                var problems = new List<string>();

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

                if (done == 0)
                {
                    foreach (var problem in problems) say.Children.Add(Ui.Problem(problem));
                    return;
                }

                _session.Record(ActivityKind.Regroup,
                                string.Format(CultureInfo.InvariantCulture, didFormat,
                                              Ui.Count(done, "result")));

                // Redrawing replaces the table whose bar the message panel lived in, so the
                // outcome goes to the screen's own log, which survives.
                RedrawTests();
                Tell(string.Format(CultureInfo.InvariantCulture, didFormat,
                                   Ui.Count(done, "result")), problems);
            }

            private void Spheres(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var scale = _session.MetresPerUnit;

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

                foreach (var entry in _read ?? new List<TestEntry>())
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
