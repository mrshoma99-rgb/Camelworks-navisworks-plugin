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
using CamelWorks.Core.Issues;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Nav;

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

        // The property columns and the grouping chain survive tab switches with the read: they
        // are how somebody set their bench up, and a bench that resets when glanced away from
        // is a bench nobody trusts with a long afternoon.
        private static readonly List<PropColumn> _columns = new List<PropColumn>();
        private static readonly List<string> _chain = new List<string>();

        /// <summary>Where the register of refused edits lives in the project file.</summary>
        private const string HeldKey = "heldEdits";

        /// <summary>
        /// The edits this document would not take, kept so the week's work is not lost.
        ///
        /// Static and beside the read, because it belongs to the document rather than to one visit
        /// to the tab: loaded from the project file on the first visit, written back on every
        /// change so it survives closing Navisworks, and dropped when the document changes so it
        /// can never be applied to a federation it says nothing about.
        /// </summary>
        private static HeldEdits? _heldEdits;

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Manage(Session session) => new Screen(session).Build();

        static ClashManageViews() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Forget everything cached about the document, so the next visit reads afresh.
        ///
        /// Called when the document changes as well as by hand. Everything here describes one
        /// federation - the results, the file being marked up, the bench somebody set up over it,
        /// and the edits held for it - and every one of them would be a lie about the next.
        /// </summary>
        internal static void Forget()
        {
            _read = null;
            _file = null;
            _source = FromModel;
            _heldEdits = null;

            _columns.Clear();
            _chain.Clear();
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// One custom column: an element property, read from one side of each clash or both.
        ///
        /// This is the move the engine's own window never learned: the results table showing the
        /// System Name, the Workset, the fire rating - whatever the elements carry - and the
        /// grouping chain running over exactly those values.
        /// </summary>
        private sealed class PropColumn
        {
            internal PropColumn(string tab, string name, string side, string title)
            {
                Id = Guid.NewGuid().ToString("N");
                Tab = tab;
                Name = name;
                Side = side;
                Title = title;
            }

            internal string Id { get; }
            internal string Tab { get; }
            internal string Name { get; }
            internal string Side { get; }
            internal string Title { get; }
        }

        /// <summary>One leaf of the grouping chain, with the rows that fell into it.</summary>
        private sealed class Bucket
        {
            internal Bucket(string name) => Name = name;

            internal string Name { get; }

            internal List<Row> Rows { get; } = new List<Row>();

            internal int Of(string status) =>
                Rows.Count(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
        }

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

            /// <summary>Custom column values, by column id, once "Read the values" has run.</summary>
            internal Dictionary<string, string> Custom { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);
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

            private readonly StackPanel _groups = Ui.Stack();
            private readonly StackPanel _columnList = Ui.Stack();
            private readonly StackPanel _chainList = Ui.Stack();
            private readonly StackPanel _fill = Ui.Stack();
            private readonly HashSet<string> _pickedGroups = new HashSet<string>(StringComparer.Ordinal);

            private string _newTab = string.Empty;
            private string _newProp = string.Empty;
            private string _newSide = BothSides;
            private string _chainAdd = string.Empty;

            private readonly TextBox _filter;
            private readonly StackPanel _filterScope = Ui.Stack();
            private string _filterIn = AnyColumn;

            // The bench: two panes, each with its own live line and its own row of symbols.
            private readonly TextBlock _testsSay = Ui.Small(string.Empty);
            private readonly TextBlock _resultsSay = Ui.Small(string.Empty);
            private readonly TextBlock _testsPicked = Ui.Small("none picked");
            private readonly TextBlock _resultsPicked = Ui.Small("none picked");
            private readonly WrapPanel _testVerbs = new WrapPanel();
            private readonly WrapPanel _resultVerbs = new WrapPanel();
            private readonly List<System.Windows.Controls.Button> _testButtons =
                new List<System.Windows.Controls.Button>();
            private readonly List<System.Windows.Controls.Button> _resultButtons =
                new List<System.Windows.Controls.Button>();

            private ListView? _testsTable;
            private ListView? _resultsTable;

            private string _searchIn = Both;

            private const string Both = "Both";
            private const string JustTests = "Tests";
            private const string JustResults = "Results";

            private const string AnyColumn = "Any column";

            private const string FirstSide = "First item";
            private const string SecondSide = "Second item";
            private const string BothSides = "Both";

            internal Screen(Session session)
            {
                _session = session;

                _heldEdits ??= HeldEdits.FromJson(
                    session.Store.Section(ProjectStore.ClashSection)[HeldKey]);

                // A seat with no clash engine has exactly one source; the switch would be a lie.
                if (!session.Clash.IsAvailable) _source = FromFile;

                _find = Ui.Text(string.Empty, "test, result, item, assignee or comment", null, 260);
                _filter = Ui.Text(string.Empty, null, 150);
                _filter.TextChanged += (s, e) => RedrawGroups();
                _comment = Ui.Text(string.Empty, null, 170);
                _group = Ui.Text(string.Empty, null, 170);
                _renameTest = Ui.Text(string.Empty, null, 170);
                _renameResults = Ui.Text(string.Empty, null, 170);
                _folder = Ui.Text(string.Empty, null, 170);
                _path = Ui.Text(_file?.Path ?? string.Empty, null, 190);
                _markups = Ui.Text(string.Empty, null, 190);
                _assign = Suggesting(Assignees());
                _find.TextChanged += (s, e) => Redraw();
            }

            private bool OnFile => _source == FromFile;

            internal UIElement Build()
            {
                // One caption for both sources: the results are what gets read either way, and
                // the tool registry promises this exact action.
                var read = Ui.Runner("Read the results", _log, Read);

                if (Current() == null)
                {
                    _headline.Children.Add(Bench.Ready(
                        OnFile
                            ? "Reading opens an exported clash file for markup: every verb here "
                              + "works on the file's own results, and the markup saves beside it "
                              + "for whoever runs the sync."
                            : "Reading pulls every test and every result out of the document, with "
                              + "status, assignee, comments and grouping — tests on the left, "
                              + "their results on the right.",
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

                var left = Bench.Pane("Tests", _testsSay,
                                      Ui.Stack(_headline, _tests),
                                      TestTools());

                var right = Bench.Pane("Results", _resultsSay,
                                       Ui.Stack(_groups, _results),
                                       ResultTools());

                return Bench.Split(Strip(), left, right, Foot(read));
            }

            // -------------------------------------------------------------------------------
            // The strip: what somebody touches every minute
            // -------------------------------------------------------------------------------

            /// <summary>
            /// One search over both tables, what it covers, and the status ticks.
            ///
            /// The old search matched a test's name and nothing else, which is why typing a result
            /// number or an element name found nothing and the box read as broken. This one runs
            /// over both tables and says which it is running over.
            /// </summary>
            private UIElement Strip()
            {
                var scope = Ui.Segmented(new[] { Both, JustTests, JustResults }, _searchIn, pick =>
                {
                    _searchIn = pick;
                    Redraw();
                });

                var ticks = new WrapPanel();

                foreach (var status in Statuses)
                {
                    var here = status;

                    ticks.Children.Add(new Border { Width = 10 });
                    ticks.Children.Add(Ui.Check(here, _show.Contains(here), on =>
                    {
                        if (on) _show.Add(here); else _show.Remove(here);
                        RedrawGroups();
                    }));
                }

                var row = new WrapPanel();

                row.Children.Add(Ui.Icon(Glyphs.Find, "Search both tables", () => _find.Focus()));
                row.Children.Add(_find);
                row.Children.Add(new Border { Width = 8 });
                row.Children.Add(scope);
                row.Children.Add(new Border { Width = 16 });
                row.Children.Add(ticks);

                return row;
            }

            /// <summary>The action row: read, then the windows where everything else lives.</summary>
            private UIElement Foot(UIElement read)
            {
                var row = new WrapPanel();

                row.Children.Add(read);
                row.Children.Add(new Border { Width = 8 });

                row.Children.Add(Ui.Quiet("Save the changes", SaveChanges));
                row.Children.Add(new Border { Width = 6 });

                _syncButton = Ui.Quiet("Sync into the document", Sync);
                row.Children.Add(_syncButton);

                ArmSync();

                row.Children.Add(new Border { Width = 14 });

                void Add(string glyph, string tip, string section)
                {
                    row.Children.Add(Ui.Icon(glyph, tip, () => Configure(section)));
                }

                Add(Glyphs.Read, "Source — the model, or a file to mark up", "source");
                Add(Glyphs.Filter, "Filters", "filters");
                Add(Glyphs.Columns, "Columns and grouping", "columns");
                Add(Glyphs.Person, "What the status, assign and group verbs write", "verbs");
                Add(Glyphs.Sphere, "Markers and the view", "model");
                Add(Glyphs.Round, "The review round", "round");
                Add(Glyphs.More, "Duplicates, spreadsheets and baselines", "beyond");

                // The one that opens all of them, for somebody who does not know which symbol
                // holds the thing they are after.
                row.Children.Add(Ui.Icon(Glyphs.Tune, "All of the above, in one window", Everything));

                row.Children.Add(new Border { Width = 14 });
                row.Children.Add(_log);

                return row;
            }

            /// <summary>The verbs under the tests table.</summary>
            private UIElement TestTools()
            {
                _testVerbs.Children.Clear();

                var bar = _testVerbs;

                bar.Children.Add(_testsPicked);

                if (!OnFile)
                {
                    bar.Children.Add(Verb(Glyphs.Play, "Run", RunTests, _testButtons));
                    bar.Children.Add(Verb(Glyphs.Pencil, "Rename", RenameTests, _testButtons));
                    bar.Children.Add(Verb(Glyphs.Number, "Renumber the results", RenameResults, _testButtons));
                    bar.Children.Add(Verb(Glyphs.MoveIn, "Move to a folder", MoveTests, _testButtons));
                    bar.Children.Add(Verb(Glyphs.Compact, "Compact — drop resolved from storage",
                                          CompactTests, _testButtons));
                    bar.Children.Add(Verb(Glyphs.Bin, "Delete", DeleteTests, _testButtons));
                }

                bar.Children.Add(Verb(Glyphs.Copy, "Copy", CopyTests, _testButtons));

                return bar;
            }

            /// <summary>The verbs under the results table.</summary>
            private UIElement ResultTools()
            {
                _resultVerbs.Children.Clear();

                var bar = _resultVerbs;

                bar.Children.Add(_resultsPicked);

                bar.Children.Add(Verb(Glyphs.Focus, "Focus", Focus, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Eye, "Isolate", Isolate, _resultButtons));
                bar.Children.Add(Ui.Icon(Glyphs.EyeOff, "Show everything again", () =>
                {
                    _session.View.ShowAll();
                    Said(Ui.Done("Everything is visible again."));
                }));

                bar.Children.Add(Verb(Glyphs.Tick, "Set the status", Mark, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Person, "Assign", AssignPicked, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Comment, "Comment", CommentPicked, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Group, "Group", GroupPicked, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Number, "Rename", RenamePickedResults, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Sphere, "Marker sphere", Spheres, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Camera, "Snapshot", Snapshots, _resultButtons));
                bar.Children.Add(Verb(Glyphs.Issue, "Create an issue",
                                      (picked, say) => RaiseIssue(picked, false), _resultButtons));
                bar.Children.Add(Verb(Glyphs.Copy, "Copy", CopyResults, _resultButtons));

                return bar;
            }

            /// <summary>
            /// One verb, as a symbol.
            ///
            /// Held in a list so the whole row can grey itself out the moment nothing is picked:
            /// a verb that reads as available and then says "nothing to work on" is a press
            /// wasted, and there are twelve of them under one table.
            /// </summary>
            private UIElement Verb(string glyph, string tip, Action<IReadOnlyList<object>, Panel> act,
                                   List<System.Windows.Controls.Button> into)
            {
                var button = Ui.Icon(glyph, tip, () => act(Picked(into), _log));

                button.IsEnabled = false;
                into.Add(button);

                return button;
            }

            /// <summary>What the row verbs act on, by which table's row the button belongs to.</summary>
            private IReadOnlyList<object> Picked(List<System.Windows.Controls.Button> which) =>
                ReferenceEquals(which, _testButtons)
                    ? (_testsTable?.PickedMany() ?? Array.Empty<object>())
                    : (_resultsTable?.PickedMany() ?? Array.Empty<object>());

            private void CopyTests(IReadOnlyList<object> picked, Panel say)
            {
                if (_testsTable != null) Ui.Copy(Ui.Lines(_testsTable, picked), say, "test");
            }

            private void CopyResults(IReadOnlyList<object> picked, Panel say)
            {
                if (_resultsTable != null) Ui.Copy(Ui.Lines(_resultsTable, picked), say, "result");
            }

            /// <summary>Grey every verb of a table that has nothing picked.</summary>
            private void Arm()
            {
                var tests = _testsTable?.PickedMany().Count ?? 0;
                var results = _resultsTable?.PickedMany().Count ?? 0;

                foreach (var button in _testButtons) button.IsEnabled = tests > 0;
                foreach (var button in _resultButtons) button.IsEnabled = results > 0;

                _testsPicked.Text = tests == 0 ? "none picked" : tests + " picked";
                _resultsPicked.Text = results == 0 ? "none picked" : results + " picked";
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
                var carried = Harvest(_read);
                var entries = new List<TestEntry>();
                var seen = 0;

                foreach (var test in _session.Clash.Tests())
                {
                    var entry = new TestEntry(test);

                    foreach (var result in _session.Clash.Results(test.Id))
                    {
                        if (!job.Step(++seen, "results read")) return;
                        entry.Rows.Add(Restored(new Row(test.Id, test.Name, result), carried));
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

                byte[] bytes;

                try
                {
                    bytes = System.IO.File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    _log.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                // Either format. A baseline written by this version is BCF; one written before
                // the change is Navisworks clash XML, and last month's review round has to still
                // open.
                var read = ClashFile.Read(bytes);

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

            /// <summary>
            /// Everything that is set up once, in a window of its own.
            ///
            /// This used to be a column down the right of the screen: every field of every verb,
            /// every property column, the whole review round, laid out beside the two tables it
            /// was meant to serve. Nobody could find anything in it and the tables had half the
            /// window. The filters somebody touches every minute are on the strip; the rest is
            /// here, one section at a time, opened from the symbol that names it.
            /// </summary>
            /// <param name="section">Which section to open on.</param>
            /// <summary>
            /// Open one section, in a window of its own.
            ///
            /// One symbol, one section, one window titled with the section's name. It opened the
            /// whole seven-section window on every symbol before, which meant seven buttons that
            /// all did the same visible thing and nothing on screen saying which had been pressed.
            /// </summary>
            /// <param name="section">The section's key, as the foot's symbols name it.</param>
            private void Configure(string section)
            {
                var page = Pages().FirstOrDefault(
                    p => string.Equals(p.Key, section, StringComparison.Ordinal));

                if (page == null) { Everything(); return; }

                Ui.Configure(page.Page.Name, Redraw, page.Page);
            }

            /// <summary>Every section at once, behind the More symbol, for a look around.</summary>
            private void Everything() =>
                Ui.Configure("Clash results", Redraw, Pages().Select(p => p.Page).ToArray());

            /// <summary>The sections, in the order the foot's symbols read.</summary>
            private IReadOnlyList<Section> Pages() => new[]
            {
                new Section("source", new Ui.Page("Source", SourcePage)),
                new Section("filters", new Ui.Page("Filters", FiltersPage)),
                new Section("columns", new Ui.Page("Columns and grouping", ColumnsPage)),
                new Section("verbs", new Ui.Page("Status and assignment", VerbsPage)),
                new Section("model", new Ui.Page("In the model", ModelPage)),
                new Section("round", new Ui.Page("The review round", RoundPage)),
                new Section("beyond", new Ui.Page("Beyond the engine", BeyondPage)),
            };

            /// <summary>A section and the key the foot's symbol names it by.</summary>
            private sealed class Section
            {
                internal Section(string key, Ui.Page page)
                {
                    Key = key;
                    Page = page;
                }

                internal string Key { get; }

                internal Ui.Page Page { get; }
            }

            private UIElement SourcePage() => Ui.Stack(
                _session.Clash.IsAvailable
                    ? Bench.Group("Where the results come from",
                        Ui.Segmented(new[] { FromModel, FromFile }, _source, pick =>
                        {
                            _source = pick;
                            Ui.Told(Shell.AppWindow.Show("clash", "manage"));
                        }),
                        Ui.Small("From the model, every edit writes into the model's own clash "
                                 + "data. From a file, the same verbs mark up an exported "
                                 + "baseline — which is what a seat with no clash engine does."))
                    : Ui.Info("This Navisworks edition has no clash engine, so there is one "
                              + "source: a file. " + (_session.ClashProblem ?? string.Empty)),
                OnFile
                    ? Bench.Group("The file",
                        Ui.PathField("Open", _path, PathKind.Open,
                                     ClashFile.Filter,
                                     "the baseline a Manage seat exported, or a saved markup"),
                        Ui.Actions(Ui.Primary("Save markup", SaveMarkup)),
                        Ui.Small("The markup lands in a \"markups\" folder beside the file, named "
                                 + "after you — it never touches the baseline or anyone else's "
                                 + "markup."))
                    : null);

            private UIElement FiltersPage()
            {
                RedrawFilterScope();

                return Ui.Stack(
                    Bench.Group("Narrow the results",
                        Ui.Check("Only ones with comments", _onlyCommented, on =>
                        {
                            _onlyCommented = on;
                            RedrawGroups();
                        }),
                        Ui.Check("Hide tests with no results", _hideEmpty, on =>
                        {
                            _hideEmpty = on;
                            RedrawTests();
                        }),
                        Ui.Field("Where", _filterScope),
                        Ui.Field("contains", _filter,
                                 "one column, or any of them - the search at the top of the "
                                 + "bench is the quick version of this")),
                    Bench.Group(string.Empty,
                        Ui.Small("The status ticks are on the strip above the tables, not here: "
                                 + "they are the one filter somebody changes every few minutes.")));
            }

            private UIElement ColumnsPage()
            {
                RedrawColumnList();
                RedrawChainList();

                return Ui.Stack(
                    Bench.Group("Property columns",
                        Ui.Field("Tab", Pickers.Tab(_newTab, pick => _newTab = pick)),
                        Ui.Field("Property", Pickers.Property(_newProp, pick => _newProp = pick)),
                        Ui.Field("Read from", Ui.Choice(new[] { BothSides, FirstSide, SecondSide },
                                                        _newSide, pick => _newSide = pick, 150)),
                        Ui.Actions(Ui.Icon(Glyphs.Add, "Add the column", AddColumn)),
                        _columnList,
                        Ui.Runner("Read values", _fill, primary: false, work: FillColumns),
                        _fill,
                        Ui.Small("Each column reads that property off the clashing elements "
                                 + "themselves - the results table carries whatever the model "
                                 + "carries, and the grouping below can chain over it.")),
                    Bench.Group("Group by, in order",
                        _chainList,
                        Ui.Small("Chained: the first entry makes the bands, the next splits each "
                                 + "band, and so on. Pick group rows to work just those results.")));
            }

            private UIElement VerbsPage() => Ui.Stack(
                Bench.Group("What the symbols under the results write",
                    Ui.Field("Set the status to", Ui.Choice(Statuses, _mark, pick =>
                    {
                        _mark = pick;
                        RedrawResults();
                    }, 150)),
                    Ui.Field("Assign to", _assign, "the project's parties, or type a name"),
                    Ui.Field("Comment", _comment),
                    Ui.Field("Group into", _group, "blank moves them out of any group")),
                OnFile ? null : Bench.Group("On the tests",
                    Ui.Field("Rename to", _renameTest, "for one picked test"),
                    Ui.Field("Move into", _folder, "a clash folder; blank is the root"),
                    Ui.Field("Results prefix", _renameResults,
                             "renames a test's results prefix1, prefix2, ...")));

            private UIElement ModelPage() => Bench.Group("Markers and the view",
                Ui.Field("Marker size", Ui.Text(
                    _markerAcross.ToString(CultureInfo.InvariantCulture), text =>
                    {
                        if (double.TryParse(text, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var mm) && mm > 0)
                            _markerAcross = mm;
                    }, 90), "millimetres across"),
                Ui.Small("Spheres are ordinary geometry, coloured by status, appended as their own "
                         + "file so they come out again from the host's file list. \"Show "
                         + "everything again\" is the eye symbol under the results."));

            private UIElement RoundPage() => OnFile
                ? Bench.Group("The review round",
                    Ui.Small("You are on the file leg: mark the results up and press \"Save "
                             + "markup\" on the Source page. A Manage seat reads it back."))
                : Ui.Stack(
                    Bench.Group("Send it out",
                        Ui.Actions(Ui.Quiet("Export...", () =>
                        {
                            ClashExportWindow.Preferred = _pickedTests.ToList();
                            Ui.Told(Shell.AppWindow.Show("issues", "clashxml"));
                        }))),
                    Bench.Group("Bring it back",
                        Ui.PathField("Markups", _markups, PathKind.Open,
                                     ClashFile.Filter,
                                     "a markup file, or the baseline — every markup beside it is read"),
                        Ui.Runner("Sync markups", _sync, primary: false, work: Sync),
                        _sync,
                        Ui.Small("Reviewers on Simulate open the exported file on this tab, mark "
                                 + "it up, and save. This reads their markups back and writes the "
                                 + "statuses, comments and assignments into the model's own clash "
                                 + "data.")));

            private UIElement BeyondPage() => Bench.Group("Beyond the engine",
                Ui.Actions(
                    Ui.Quiet("Find duplicates", Duplicates),
                    Ui.Quiet("Excel...", Sheet),
                    Ui.Quiet("Review...", () => Ui.Told(ReviewWindow.Show()))),
                Ui.Actions(Ui.Quiet("Baseline...", SaveBaseline)),
                Ui.Small("Duplicates are the same conflict reported by two tests - marked "
                         + "Approved with a comment naming the one that stays open. The "
                         + "baseline carries statuses, assignments, comment trails and "
                         + "groups, ready for reviewers."));

            /// <summary>Whoever the assign symbol would write, for the menu to say so.</summary>
            private string Who()
            {
                var name = (_assign.Text ?? string.Empty).Trim();
                return name.Length == 0 ? "nobody" : name;
            }

            /// <summary>Whether a result answers the bench's own search box.</summary>
            private bool Hits(Row row, string find)
            {
                bool Has(string? value) =>
                    value != null && value.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;

                if (Has(row.Info.Name) || Has(row.TestName) || Has(row.Status) || Has(row.Group)
                    || Has(row.Assigned) || Has(row.LastComment)
                    || Has(row.Info.NameA) || Has(row.Info.NameB)
                    || Has(row.Info.CategoryA) || Has(row.Info.CategoryB))
                    return true;

                foreach (var value in row.Custom.Values)
                    if (Has(value)) return true;

                return false;
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
                _testsTable = null;

                // The source can have changed since the foot was built, and with it whether the
                // document will take a sync at all.
                ArmSync();

                var read = Current();
                if (read == null) { Arm(); return; }

                var find = _find.Text.Trim();
                var searching = find.Length > 0 && _searchIn != JustResults;

                var wanted = read
                    .Where(e => !searching
                                || e.Test.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0
                                || e.Rows.Any(r => Hits(r, find)))
                    .Where(e => !_hideEmpty || e.Rows.Count > 0)
                    .ToList();

                _headline.Children.Clear();

                if (!OnFile && !Held.IsEmpty)
                {
                    _headline.Children.Add(Ui.Info(
                        Ui.Count(Held.Count, "result") + " carries an edit this document would "
                        + "not take. Published files hold their clash results read-only, so the "
                        + "decisions are kept here: they show on the board, they survive a "
                        + "re-read, and \"Save the baseline\" writes them out as clash XML to "
                        + "sync into the NWF the results belong to."));

                    _headline.Children.Add(Ui.Actions(
                        Ui.Quiet("What is waiting", ShowHeld),
                        Ui.Quiet("Forget them", ForgetHeld)));
                }

                _testsSay.Text =
                    (OnFile && _file != null
                        ? System.IO.Path.GetFileName(_file.Path) + " — "
                        : string.Empty)
                    + (wanted.Count == read.Count
                        ? Ui.Count(read.Count, "test")
                        : wanted.Count + " of " + Ui.Count(read.Count, "test"))
                    + ", " + Ui.Count(read.Sum(e => e.Rows.Count), "result");

                if (wanted.Count == 0)
                {
                    _tests.Children.Add(Ui.Empty("No test matches.",
                        "Clear the search at the top, or widen what it covers."));
                    Arm();
                    RedrawGroups();
                    return;
                }

                // Five status columns were five numbers to read per row. One band says the same
                // thing at a glance and leaves the width for the name, which is the long part.
                var rows = wanted.Select(e => new TableRow(e,
                    e.Test.Name,
                    e.Test.Folder ?? string.Empty,
                    Skin.Num(e.Rows.Count),
                    Band(e))
                {
                    Tone = e.Of("New") > 0 ? Tone.Warn : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                    ("Test", 200), ("Folder", 80), ("Results", 65), ("New · active · closed", 0));

                _testsTable = table;

                table.SelectionChanged += (s, e) =>
                {
                    _pickedTests.Clear();

                    foreach (var picked in table.PickedMany())
                        if (picked is TestEntry entry) _pickedTests.Add(entry.Test.Id);

                    Arm();
                    RedrawGroups();
                };

                // A file is a record of a run: tests cannot be run, renamed, moved or deleted in
                // one, so those verbs are not on a file's menu rather than shown grey.
                table.Menu(OnFile
                    ? new[] { new Ui.OnMenu("Copy {n}", picked => CopyTests(picked, _log)) }
                    : new[]
                    {
                        new Ui.OnMenu("Run {n}", picked => RunTests(picked, _log)),
                        new Ui.OnMenu("Rename...", picked => RenameTests(picked, _log)),
                        new Ui.OnMenu("Renumber the results of {n}", picked => RenameResults(picked, _log)),
                        new Ui.OnMenu("Move {n} to a folder", picked => MoveTests(picked, _log)),
                        new Ui.OnMenu("Compact {n}", picked => CompactTests(picked, _log)),
                        new Ui.OnMenu("Delete {n}", picked => DeleteTests(picked, _log)),
                        new Ui.OnMenu("Copy {n}", picked => CopyTests(picked, _log)),
                    });

                _tests.Children.Add(table);

                Arm();
                RedrawGroups();
            }

            /// <summary>A test's results as one band: how many are new, working, and closed.</summary>
            private static string Band(TestEntry entry)
            {
                if (entry.Rows.Count == 0) return "—";

                var closed = entry.Of("Approved") + entry.Of("Resolved");
                var active = entry.Of("Active") + entry.Of("Reviewed");

                return Skin.Num(entry.Of("New")) + " · " + Skin.Num(active) + " · " + Skin.Num(closed);
            }

            /// <summary>The rows the filters allow, before any group pick narrows them.</summary>
            private List<Row> Filtered()
            {
                var read = Current();
                if (read == null) return new List<Row>();

                var from = _pickedTests.Count == 0
                    ? read
                    : read.Where(e => _pickedTests.Contains(e.Test.Id)).ToList();

                var rows = from
                    .SelectMany(e => e.Rows)
                    .Where(r => _show.Contains(r.Status))
                    .Where(r => !_onlyCommented || r.Comments > 0);

                // The bench's own search, which covers both tables. The column filter below is
                // the same idea aimed at one column, and lives in the configuration window.
                var find = _find.Text.Trim();

                if (find.Length > 0 && _searchIn != JustTests)
                    rows = rows.Where(r => Hits(r, find));

                var wanted = _filter.Text.Trim();

                if (wanted.Length > 0)
                    rows = rows.Where(r => Matches(r, wanted));

                return rows.ToList();
            }

            /// <summary>Whether a row's chosen column - or any of them - contains the text.</summary>
            private bool Matches(Row row, string wanted)
            {
                bool Has(string? value) =>
                    value != null && value.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;

                if (_filterIn != AnyColumn)
                {
                    foreach (var column in _columns)
                        if (string.Equals(_filterIn, column.Title, StringComparison.Ordinal))
                            return Has(row.Custom.TryGetValue(column.Id, out var v) ? v : null);

                    return _filterIn switch
                    {
                        "Result" => Has(row.Info.Name),
                        "Test" => Has(row.TestName),
                        "Group" => Has(row.Group),
                        "Status" => Has(row.Status),
                        "Assigned" => Has(row.Assigned),
                        "Comment" => Has(row.LastComment),
                        "Item 1" => Has(row.Info.NameA),
                        "Item 2" => Has(row.Info.NameB),
                        _ => false,
                    };
                }

                if (Has(row.Info.Name) || Has(row.TestName) || Has(row.Group) || Has(row.Status)
                    || Has(row.Assigned) || Has(row.LastComment)
                    || Has(row.Info.NameA) || Has(row.Info.NameB)
                    || Has(row.Info.CategoryA) || Has(row.Info.CategoryB))
                    return true;

                foreach (var value in row.Custom.Values)
                    if (Has(value)) return true;

                return false;
            }

            /// <summary>The filter's column picker, rebuilt when the custom columns change.</summary>
            private void RedrawFilterScope()
            {
                _filterScope.Children.Clear();

                var options = new List<string>
                {
                    AnyColumn, "Result", "Test", "Group", "Status", "Assigned", "Comment",
                    "Item 1", "Item 2",
                };

                foreach (var column in _columns) options.Add(column.Title);

                if (!options.Contains(_filterIn)) _filterIn = AnyColumn;

                _filterScope.Children.Add(Ui.Choice(options, _filterIn, pick =>
                {
                    _filterIn = pick;
                    if (_filter.Text.Trim().Length > 0) RedrawGroups();
                }, 150));
            }

            /// <summary>
            /// The grouping chain's summary: one row per leaf band, with its counts, its own
            /// bulk verbs, and picking that narrows the results grid to the picked bands. The
            /// same master-detail the tests table already is, one level further in.
            /// </summary>
            private void RedrawGroups()
            {
                _groups.Children.Clear();
                _pickedGroups.Clear();

                if (_chain.Count == 0)
                {
                    RedrawResults();
                    return;
                }

                var rows = Filtered();

                var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);

                foreach (var row in rows)
                {
                    var name = ChainKey(row);

                    if (!buckets.TryGetValue(name, out var bucket))
                    {
                        bucket = new Bucket(name);
                        buckets[name] = bucket;
                    }

                    bucket.Rows.Add(row);
                }

                var ordered = buckets.Values
                    .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _groups.Children.Add(Ui.Eyebrow("Grouped by "
                    + string.Join(" · ", _chain.Select(ChainTitle))));

                var table = Ui.Table(ordered.Select(b => new TableRow(b,
                    b.Name,
                    Skin.Num(b.Rows.Count),
                    Skin.Num(b.Of("New")),
                    Skin.Num(b.Of("Active")),
                    Skin.Num(b.Of("Reviewed")),
                    Skin.Num(b.Of("Approved")),
                    Skin.Num(b.Of("Resolved")))
                {
                    Tone = b.Of("New") > 0 ? Tone.Warn : Tone.Plain,
                }).ToList(),
                    ("Group", 300), ("Results", 70), ("New", 55), ("Active", 55),
                    ("Reviewed", 70), ("Approved", 70), ("Resolved", 0));

                table.SelectionChanged += (s, e) =>
                {
                    _pickedGroups.Clear();

                    foreach (var picked in table.PickedMany())
                        if (picked is Bucket bucket) _pickedGroups.Add(bucket.Name);

                    RedrawResults();
                };

                _groups.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Mark {n}", (picked, say) =>
                        Mark(Flatten(picked), say)),
                    new Ui.OnPicked("Assign {n}", (picked, say) =>
                        AssignPicked(Flatten(picked), say)),
                    new Ui.OnPicked("Comment {n}", (picked, say) =>
                        CommentPicked(Flatten(picked), say)),
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "group"))));

                RedrawResults();
            }

            /// <summary>The picked buckets' rows, as the row verbs expect them.</summary>
            private static IReadOnlyList<object> Flatten(IReadOnlyList<object> picked) =>
                picked.OfType<Bucket>().SelectMany(b => b.Rows).Cast<object>().ToList();

            private void RedrawResults()
            {
                _results.Children.Clear();
                _resultsTable = null;

                if (Current() == null) { Arm(); return; }

                var rows = Filtered();

                if (_chain.Count > 0 && _pickedGroups.Count > 0)
                    rows = rows.Where(r => _pickedGroups.Contains(ChainKey(r))).ToList();

                _resultsSay.Text =
                    (_chain.Count > 0 && _pickedGroups.Count > 0
                        ? "of " + Ui.Count(_pickedGroups.Count, "picked group")
                        : _pickedTests.Count == 0
                            ? "every test"
                            : "of " + Ui.Count(_pickedTests.Count, "picked test"))
                    + " — " + Ui.Count(rows.Count, "shown");

                if (rows.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing matches.",
                        "Every result of "
                        + (_pickedTests.Count == 0 ? "every test" : "the picked tests")
                        + " is filtered out. Untick a status on the strip above, clear the search, "
                        + "or pick other tests."));
                    Arm();
                    return;
                }

                // The window stays honest about what a table can hold: the verbs below act on
                // what is picked in what is SHOWN, and a cap this size scrolls rather than hangs.
                const int most = 2500;
                var shown = rows.Count > most ? rows.Take(most).ToList() : rows;

                _shown = shown;

                // The columns are built as lists because the custom ones come and go: the
                // property columns land between Worst and Assigned, where the eye is already
                // reading facts about the elements.
                var headers = new List<(string, double)>
                {
                    ("Result", 110), ("Test", 140), ("Group", 80), ("Status", 70), ("Worst", 65),
                };

                foreach (var column in _columns) headers.Add((column.Title, 95));

                headers.Add(("Assigned", 85));
                headers.Add(("Comment", 130));
                headers.Add(("Item 1", 110));
                headers.Add(("Item 2", 0));

                TableRow Line(Row r)
                {
                    var cells = new List<string?>
                    {
                        r.Info.Name ?? string.Empty, r.TestName, r.Group, r.Status, Worst(r),
                    };

                    foreach (var column in _columns)
                        cells.Add(r.Custom.TryGetValue(column.Id, out var value)
                            ? value
                            : string.Empty);

                    cells.Add(r.Assigned);
                    cells.Add(r.Comments == 0 ? string.Empty : r.LastComment);
                    cells.Add(r.Info.NameA ?? string.Empty);
                    cells.Add(r.Info.NameB ?? string.Empty);

                    return new TableRow(r, cells.ToArray())
                    {
                        Tone = string.Equals(r.Status, "New", StringComparison.OrdinalIgnoreCase) ? Tone.Bad
                             : string.Equals(r.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(r.Status, "Approved", StringComparison.OrdinalIgnoreCase) ? Tone.Good
                             : Tone.Plain,
                    };
                }

                var table = Ui.Table(shown.Select(Line).ToList(), headers.ToArray())
                    .Menu(new Ui.OnMenu("Focus {n}", picked => Focus(picked, _log)),
                          new Ui.OnMenu("Isolate {n}", picked => Isolate(picked, _log)),
                          new Ui.OnMenu("Mark {n} " + _mark, picked => Mark(picked, _log)),
                          new Ui.OnMenu("Assign {n} to " + Who(), picked => AssignPicked(picked, _log)),
                          new Ui.OnMenu("Comment on {n}", picked => CommentPicked(picked, _log)),
                          new Ui.OnMenu("Group {n}", picked => GroupPicked(picked, _log)),
                          new Ui.OnMenu("Create an issue from {n}", picked => RaiseIssue(picked, false)),
                          new Ui.OnMenu("An issue for each of {n}", picked => RaiseIssue(picked, true)),
                          new Ui.OnMenu("Marker sphere on {n}", picked => Spheres(picked, _log)),
                          new Ui.OnMenu("Copy {n}", picked => CopyResults(picked, _log)));

                _resultsTable = table;

                table.SelectionChanged += (s, e) => Arm();

                _results.Children.Add(table);

                Arm();

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
                say.Children.Add(Ui.Danger("Yes, delete " + Ui.Count(tests.Count, "test"), () =>
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

            /// <summary>
            /// Drop the picked tests' Resolved results from storage - the host's own compact,
            /// and the only per-result delete any clash tool honestly has: the engine owns its
            /// results, and the next run recreates whatever still clashes.
            /// </summary>
            private void CompactTests(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                if (tests.Count == 0) return;

                if (!_session.Clash.CompactTests(tests.Select(t => t.Test.Id).ToList()))
                {
                    say.Children.Add(Ui.Problem(_session.Clash.LastProblem ?? "The host refused."));
                    return;
                }

                _session.Record(ActivityKind.Regroup,
                                "compacted " + Ui.Count(tests.Count, "clash test")
                                + ", dropping their Resolved results from storage");
                Reread("Compacted " + Ui.Count(tests.Count, "test") + " - the Resolved results "
                       + "are out of storage. The next run recreates whatever still clashes.", null);
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

                // One result from the model gets the host's own composed view of it - the frame
                // the engine saved when the test ran, which reads like the native clash window.
                if (!OnFile && rows.Count == 1
                    && _session.Clash.ApplyResultView(rows[0].TestId, rows[0].Info.Id))
                {
                    _session.Model.Select(keys);

                    say.Children.Add(Ui.Done("Showing the engine's own view of \""
                        + (rows[0].Info.Name ?? "the result") + "\"."));
                    return;
                }

                // The camera goes to the point the results already carry, before anything is
                // resolved. A clash result knows where it happened; framing the elements means
                // finding them first, and the coordinator is looking at a 3D view either way.
                var point = Middle(rows);

                if (point != null)
                {
                    _session.View.LookAt(point.Value.X, point.Value.Y, point.Value.Z, 4.0);
                }

                // Then the selection, which is what makes the view worth arriving at - and is now
                // the only part that costs anything.
                _session.Model.Select(keys);

                if (point == null) _session.View.ZoomTo(keys, 1.0);

                say.Children.Add(Ui.Done("Selected and framed "
                    + Ui.Count(keys.Count, "element") + "."));
            }

            /// <summary>
            /// The middle of what was picked, in model units, or null when no row carries a point.
            ///
            /// Every clash result records where the two elements met, so a camera move needs no
            /// elements at all. Averaging is right for a handful of picked rows in one area, which
            /// is what a coordinator picks; a selection spread across the site gets a point in the
            /// middle of nowhere, and the selection they can still see in the tree is the answer
            /// to that.
            /// </summary>
            private static (double X, double Y, double Z)? Middle(IReadOnlyList<Row> rows)
            {
                double x = 0, y = 0, z = 0;
                var counted = 0;

                foreach (var row in rows)
                {
                    var info = row.Info;

                    // A result with no geometry behind it records the origin, and averaging that
                    // in drags the camera towards a corner of the site.
                    if (info.PointX == 0 && info.PointY == 0 && info.PointZ == 0) continue;

                    x += info.PointX;
                    y += info.PointY;
                    z += info.PointZ;
                    counted++;
                }

                return counted == 0 ? null : ((double, double, double)?)(x / counted, y / counted, z / counted);
            }

            /// <summary>
            /// Make issues out of picked results.
            ///
            /// The move the native window has no answer for: eleven results are one conversation
            /// with one person, and what gets sent, chased and closed is that conversation - not
            /// eleven rows of geometry. One issue over all of them by default, one each when they
            /// really are separate problems.
            /// </summary>
            /// <param name="picked">The picked results.</param>
            /// <param name="each">One issue per result, rather than one over all of them.</param>
            private void RaiseIssue(IReadOnlyList<object> picked, bool each)
            {
                var rows = Rows(picked);
                if (rows.Count == 0) return;

                // A file's results are already metres; the model's are in document units.
                var scale = OnFile ? 1.0 : _session.MetresPerUnit;

                Issue? Make(string id, IReadOnlyList<Row> from)
                {
                    if (from.Count == 0) return null;

                    var first = from[0];

                    var tests = from.Select(r => r.TestName).Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                    var title = from.Count == 1
                        ? (first.Info.Name ?? "Clash") + " — " + first.TestName
                        : Ui.Count(from.Count, "clash") + " in "
                          + (tests.Count == 1 ? tests[0] : Ui.Count(tests.Count, "test"));

                    var issue = new Issue(id, title, IssueOrigin.Clash)
                    {
                        Type = "Clash",
                        Source = tests.Count == 1 ? tests[0] : string.Join(", ", tests.Take(4)),
                        Description = string.Join(Environment.NewLine, from.Take(20).Select(r =>
                            (r.Info.Name ?? "result") + ": " + (r.Info.NameA ?? "?") + " vs "
                            + (r.Info.NameB ?? "?"))),
                        AssignedTo = first.Assigned.Length == 0 ? null : first.Assigned,
                        HasPoint = true,
                        X = from.Average(r => r.Info.PointX) * scale,
                        Y = from.Average(r => r.Info.PointY) * scale,
                        Z = from.Average(r => r.Info.PointZ) * scale,
                    };

                    foreach (var row in from)
                    {
                        issue.Clashes.Add(row.Info.Key());

                        if (!row.Info.A.IsEmpty && !issue.Elements.Contains(row.Info.A))
                            issue.Elements.Add(row.Info.A);

                        if (!row.Info.B.IsEmpty && !issue.Elements.Contains(row.Info.B))
                            issue.Elements.Add(row.Info.B);
                    }

                    return issue;
                }

                var at = 0;

                var (made, problem) = IssueViews.Raise(_session,
                    id => each ? Make(id, new[] { rows[at++] }) : Make(id, rows),
                    each ? rows.Count : 1);

                if (problem != null)
                {
                    Said(Ui.Problem(problem));
                    return;
                }

                Said(Ui.Info(Ui.Count(made, "issue") + " raised from "
                    + Ui.Count(rows.Count, "result") + ".",
                    "Open the register", () => IssueViews.Open(null)));
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
                      row => row.Status = _mark,
                      edit => edit.Status = _mark);

            private void AssignPicked(IReadOnlyList<object> picked, Panel say)
            {
                var who = (_assign.Text ?? string.Empty).Trim();

                Write(picked, say,
                      who.Length == 0 ? "Cleared the assignment on {0}." : "Assigned {0} to " + who + ".",
                      (testId, ids) => _session.Clash.Assign(testId, ids, who),
                      row => row.Assigned = who,
                      edit => edit.Assigned = who);
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

                var when = DateTime.UtcNow.Ticks;

                Write(picked, say, "Commented on {0}.",
                      (testId, ids) => _session.Clash.Comment(testId, ids, body, author),
                      row =>
                      {
                          row.Comments++;
                          row.LastComment = body;
                          row.Trail.Add(new ClashComment(author, when, body));
                      },
                      edit => edit.Comments.Add(new ClashComment(author, when, body)));
            }

            /// <summary>Rename just the picked results, prefix1, prefix2, ... in host order.</summary>
            private void RenamePickedResults(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                if (OnFile)
                {
                    say.Children.Add(Ui.Problem("A markup regenerates its result names when it "
                        + "saves, so renaming here would not survive the file. Rename in the "
                        + "model, where the name is the result's own."));
                    return;
                }

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var prefix = _renameResults.Text.Trim();

                if (prefix.Length == 0)
                {
                    say.Children.Add(Ui.Problem("Type the prefix in \"Results prefix\" first."));
                    return;
                }

                var done = 0;
                var problems = new List<string>();

                foreach (var byTest in rows.GroupBy(r => r.TestId, StringComparer.OrdinalIgnoreCase))
                {
                    var ids = byTest.Select(r => r.Info.Id).ToList();

                    if (!_session.Clash.RenamePicked(byTest.Key, ids, prefix))
                    {
                        problems.Add(_session.Clash.LastProblem ?? "The host refused.");
                        continue;
                    }

                    done += ids.Count;
                }

                if (done == 0)
                {
                    foreach (var problem in problems) say.Children.Add(Ui.Problem(problem));
                    return;
                }

                _session.Record(ActivityKind.Regroup, "renamed " + Ui.Count(done, "picked result")
                    + " to \"" + prefix + "...\"");
                Reread("Renamed " + Ui.Count(done, "result") + " to \"" + prefix + "...\".",
                       problems);
            }

            private void GroupPicked(IReadOnlyList<object> picked, Panel say)
            {
                var group = _group.Text.Trim();

                Write(picked, say,
                      group.Length == 0 ? "Moved {0} out of their groups." : "Grouped {0} into \"" + group + "\".",
                      (testId, ids) => _session.Clash.Regroup(testId, ids, group),
                      row => row.Group = group,
                      edit => edit.Group = group);
            }

            /// <summary>
            /// One write over the picked rows, test by test, with the cache kept truthful.
            ///
            /// From the model, the host is asked first and the cached rows change only after it
            /// said yes. From a file, the rows ARE the data, so the edit lands directly and the
            /// save writes it out — that is what a markup is.
            /// </summary>
            private void Write(IReadOnlyList<object> picked, Panel say, string didFormat,
                               Func<string, IReadOnlyList<string>, bool> write, Action<Row> apply,
                               Action<HeldEdit>? hold = null)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var done = 0;
                var held = 0;
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
                            if (hold == null)
                            {
                                problems.Add(_session.Clash.LastProblem ?? "The host refused.");
                                continue;
                            }

                            // The document would not take it - a published NWD holds its results
                            // read-only - so the edit is kept rather than lost. It shows on the
                            // board, it survives a re-read and a re-open, and it travels out in
                            // the baseline that syncs into the NWF the results belong to.
                            foreach (var row in byTest)
                            {
                                hold(Held.For(row.TestId, row.Info.Id));
                                apply(row);
                                held++;
                            }

                            continue;
                        }

                        // Written for real: whatever was being kept for these results is now in
                        // the document, and keeping it any longer would write it twice.
                        foreach (var row in byTest)
                        {
                            Held.Release(row.TestId, row.Info.Id);
                            apply(row);
                        }

                        done += ids.Count;
                    }
                }

                if (done == 0 && held == 0)
                {
                    foreach (var problem in problems) say.Children.Add(Ui.Problem(problem));
                    return;
                }

                if (!OnFile) { KeepHeld(); ArmSync(); }

                var what = string.Format(CultureInfo.InvariantCulture, didFormat,
                                         Ui.Count(done + held, "result"));

                if (!OnFile) _session.Record(ActivityKind.Regroup, what);

                // Redrawing replaces the table whose bar the message panel lived in, so the
                // outcome goes to the screen's own log, which survives.
                RedrawTests();
                Tell(what + Trailer(held), problems);
            }

            /// <summary>What to say after a write that the document would not take all of.</summary>
            /// <param name="held">How many results were kept here instead.</param>
            private string Trailer(int held)
            {
                if (OnFile) return " Save the markup to keep it.";

                if (held == 0) return string.Empty;

                return " " + Ui.Count(held, "result") + " could not be written into this "
                       + "document, so " + (held == 1 ? "it is" : "they are") + " kept here "
                       + "instead - " + Ui.Count(Held.Count, "result") + " waiting in all. "
                       + "Write the baseline to carry them into the NWF.";
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

            // -------------------------------------------------------------------------------
            // Property columns and the grouping chain
            // -------------------------------------------------------------------------------

            private void AddColumn()
            {
                var name = _newProp.Trim();

                if (name.Length == 0)
                {
                    Said(Ui.Problem("Pick or type the property first."));
                    return;
                }

                // The title has to tell two columns apart: the bare name until it collides,
                // the tab beside it after, the side beside that when both sides are read apart.
                var title = name;

                if (_columns.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    && _newTab.Trim().Length > 0)
                    title = _newTab.Trim() + " / " + name;

                if (_newSide == FirstSide) title += " (1)";
                else if (_newSide == SecondSide) title += " (2)";

                _columns.Add(new PropColumn(_newTab.Trim(), name, _newSide, title));

                RedrawColumnList();
                RedrawChainList();
                RedrawFilterScope();
                RedrawGroups();

                Said(Ui.Done("Added \"" + title + "\". Press \"Read values\" to fill it."));
            }

            private void RedrawColumnList()
            {
                _columnList.Children.Clear();

                foreach (var column in _columns)
                {
                    var here = column;

                    _columnList.Children.Add(Ui.Across(
                        Ui.Line(here.Title, 0.85),
                        Ui.Icon(Glyphs.Close, "Take the column out", () =>
                        {
                            _columns.Remove(here);
                            _chain.Remove("col:" + here.Id);
                            RedrawColumnList();
                            RedrawChainList();
                            RedrawFilterScope();
                            RedrawGroups();
                        })));
                }
            }

            /// <summary>Everything the chain can group by: the fixed facts, then the columns.</summary>
            private List<(string Key, string Title)> ChainOptions()
            {
                var options = new List<(string, string)>
                {
                    ("test", "Test"),
                    ("status", "Status"),
                    ("assigned", "Assigned to"),
                    ("hostgroup", "Host group"),
                    ("item1", "Item 1"),
                    ("item2", "Item 2"),
                    ("cat1", "Category (1)"),
                    ("cat2", "Category (2)"),
                    ("worst", "Worst band"),
                    ("commented", "Commented or not"),
                };

                foreach (var column in _columns) options.Add(("col:" + column.Id, column.Title));

                return options;
            }

            private void RedrawChainList()
            {
                _chainList.Children.Clear();

                for (var i = 0; i < _chain.Count; i++)
                {
                    var at = i;
                    var key = _chain[at];

                    _chainList.Children.Add(Ui.Across(
                        Ui.Line((at + 1).ToString(CultureInfo.InvariantCulture) + ". "
                                + ChainTitle(key), 0.85),
                        Ui.Icon(Glyphs.Up, "Group by this sooner", () =>
                        {
                            if (at == 0) return;
                            _chain.RemoveAt(at);
                            _chain.Insert(at - 1, key);
                            RedrawChainList();
                            RedrawGroups();
                        }),
                        Ui.Icon(Glyphs.Down, "Group by this later", () =>
                        {
                            if (at >= _chain.Count - 1) return;
                            _chain.RemoveAt(at);
                            _chain.Insert(at + 1, key);
                            RedrawChainList();
                            RedrawGroups();
                        }),
                        Ui.Icon(Glyphs.Close, "Take it out of the chain", () =>
                        {
                            _chain.RemoveAt(at);
                            RedrawChainList();
                            RedrawGroups();
                        })));
                }

                var options = ChainOptions()
                    .Where(o => !_chain.Contains(o.Key))
                    .ToList();

                if (options.Count == 0) return;

                var titles = options.Select(o => o.Title).ToList();

                if (!titles.Contains(_chainAdd)) _chainAdd = titles[0];

                _chainList.Children.Add(Ui.Across(
                    Ui.Choice(titles, _chainAdd, pick => _chainAdd = pick, 160),
                    Ui.Quiet("Add", () =>
                    {
                        var chosen = options.FirstOrDefault(
                            o => string.Equals(o.Title, _chainAdd, StringComparison.Ordinal));

                        if (chosen.Key == null) return;

                        _chain.Add(chosen.Key);
                        RedrawChainList();
                        RedrawGroups();
                    })));
            }

            private string ChainTitle(string key)
            {
                foreach (var option in ChainOptions())
                    if (string.Equals(option.Key, key, StringComparison.Ordinal)) return option.Title;

                return "(a removed column)";
            }

            /// <summary>One row's value for one link of the chain, with absence said honestly.</summary>
            private string GroupValue(Row row, string key)
            {
                switch (key)
                {
                    case "test": return row.TestName;
                    case "status": return row.Status;
                    case "assigned": return row.Assigned.Length == 0 ? "(unassigned)" : row.Assigned;
                    case "hostgroup": return row.Group.Length == 0 ? "(no group)" : row.Group;
                    case "item1": return Blankless(row.Info.NameA, "(unnamed)");
                    case "item2": return Blankless(row.Info.NameB, "(unnamed)");
                    case "cat1": return Blankless(row.Info.CategoryA, "(no category)");
                    case "cat2": return Blankless(row.Info.CategoryB, "(no category)");
                    case "worst": return WorstBand(row);
                    case "commented": return row.Comments > 0 ? "Commented" : "No comments";
                }

                if (key.StartsWith("col:", StringComparison.Ordinal))
                {
                    if (!row.Custom.TryGetValue(key.Substring(4), out var value))
                        return "(values not read)";

                    return value.Length == 0 ? "(none)" : value;
                }

                return "(unknown)";
            }

            private static string Blankless(string? value, string absent) =>
                string.IsNullOrWhiteSpace(value) ? absent : value!;

            /// <summary>
            /// The worst distance as a band a person argues in - grouping by the raw millimetre
            /// would put every clash in a band of one.
            /// </summary>
            private string WorstBand(Row row)
            {
                if (!row.Info.Distance.HasValue) return "(no distance)";

                var mm = Math.Abs(row.Info.Distance.Value)
                         * (OnFile ? 1.0 : _session.MetresPerUnit) * 1000;

                return mm < 10 ? "under 10 mm"
                     : mm < 25 ? "10 - 25 mm"
                     : mm < 50 ? "25 - 50 mm"
                     : mm < 100 ? "50 - 100 mm"
                     : "over 100 mm";
            }

            private string ChainKey(Row row) =>
                string.Join(" · ", _chain.Select(key => GroupValue(row, key)));

            /// <summary>
            /// Read every custom column's value off the clashing elements themselves.
            ///
            /// The one deliberately user-started read that touches the element index: the first
            /// resolve builds it for the whole federation, which is exactly the cost the board
            /// build was cured of paying by accident. Asked for by a press, it is worth it - and
            /// it is paid once, not once per glance.
            /// </summary>
            private void FillColumns(Job job)
            {
                if (_columns.Count == 0)
                {
                    _fill.Children.Add(Ui.Problem("Add a column first."));
                    return;
                }

                var read = Current();

                if (read == null)
                {
                    _fill.Children.Add(Ui.Problem("Read the results first."));
                    return;
                }

                job.Say("Finding the elements (the first look indexes the federation)...");

                var rows = read.SelectMany(e => e.Rows).ToList();
                var filled = 0;
                var blank = 0;
                var done = 0;

                foreach (var row in rows)
                {
                    if (!job.Step(++done, "results read")) return;

                    var a = row.Info.A.IsEmpty ? null : _session.Model.Resolve(row.Info.A);
                    var b = row.Info.B.IsEmpty ? null : _session.Model.Resolve(row.Info.B);

                    foreach (var column in _columns)
                    {
                        var tab = column.Tab.Length == 0 ? null : column.Tab;

                        var fromA = column.Side == SecondSide
                            ? null
                            : Value(a, tab, column.Name);

                        var fromB = column.Side == FirstSide
                            ? null
                            : Value(b, tab, column.Name);

                        var value = column.Side == BothSides
                            ? (fromA != null && fromB != null && fromA != fromB
                                ? fromA + " | " + fromB
                                : fromA ?? fromB)
                            : column.Side == FirstSide ? fromA : fromB;

                        row.Custom[column.Id] = value ?? string.Empty;

                        if (value == null) blank++; else filled++;
                    }
                }

                RedrawGroups();

                _fill.Children.Add(Ui.Done(Ui.Count(filled, "value") + " read into "
                    + Ui.Count(_columns.Count, "column") + "."
                    + (blank > 0
                        ? " " + Ui.Count(blank, "cell") + " came back empty - elements that do "
                          + "not carry the property, or results a file could not identify here."
                        : string.Empty)));
            }

            private static string? Value(IModelItem? item, string? tab, string name)
            {
                if (item == null) return null;

                try
                {
                    // Named tab first; without one, the tabs a value usually lives on.
                    if (tab != null) return item.Property(tab, name);

                    return item.Property("Element", name)
                           ?? item.Property("Item", name)
                           ?? item.Property(NavWriteTransaction.TabName, name);
                }
                catch (Exception)
                {
                    return null;
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
                    "Approve duplicates", () =>
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

                // The GlobalIds are what let the receiving tool select the two clashing
                // elements. Read before writing, under the wait cursor: on a first export of a
                // large board this is the slow part, and it is not a press that looks slow.
                var named = 0;

                if (!OnFile) Ui.Busy(() => named = ClashIdentity.Stamp(_session, items, null));

                var written = ClashBcf.Write(items, null, new ClashBcfOptions
                {
                    SourceFile = _session.Store.Path,
                    ProjectName = _session.Profile.ProjectName,
                    Author = Environment.UserName ?? "CamelWorks",
                    MetresPerUnit = OnFile ? 1.0 : _session.MetresPerUnit,
                    WhenTicks = DateTime.UtcNow.Ticks,
                });

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the baseline",
                    Filter = ClashFile.Filter,
                    FileName = "clash-baseline" + ClashFile.Extension,
                    AddExtension = true,
                    DefaultExt = ClashFile.Extension,
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName, written.Bytes);
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

                if (!OnFile)
                {
                    var about = ClashIdentity.Explain(items.Count, named);

                    if (about != null) Said(named == 0 ? Ui.Note(about) : Ui.Line(about, 0.8));
                }
            }

            /// <summary>The results grid, exactly as filtered and shown, as a spreadsheet.</summary>
            private void Sheet()
            {
                if (_shown.Count == 0)
                {
                    Said(Ui.Problem("Nothing is on the grid to write."));
                    return;
                }

                var headers = new List<string>
                {
                    "Test", "Result", "Group", "Status", "Worst",
                };

                foreach (var column in _columns) headers.Add(column.Title);
                if (_chain.Count > 0) headers.Add("Grouped as");

                headers.AddRange(new[]
                {
                    "Assigned", "Comments", "Latest comment", "Item 1", "Item 2",
                });

                var block = new TableBlock(headers.ToArray());

                foreach (var row in _shown)
                {
                    var cells = new List<string>
                    {
                        row.TestName, row.Info.Name ?? string.Empty, row.Group, row.Status,
                        Worst(row),
                    };

                    foreach (var column in _columns)
                        cells.Add(row.Custom.TryGetValue(column.Id, out var value)
                            ? value
                            : string.Empty);

                    if (_chain.Count > 0) cells.Add(ChainKey(row));

                    cells.Add(row.Assigned);
                    cells.Add(row.Comments.ToString(CultureInfo.InvariantCulture));
                    cells.Add(row.LastComment);
                    cells.Add(row.Info.NameA ?? string.Empty);
                    cells.Add(row.Info.NameB ?? string.Empty);

                    block.Row(cells.ToArray());
                }

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

                var written = ClashBcf.Write(items, null, new ClashBcfOptions
                {
                    SourceFile = round.Path,
                    ProjectName = _session.Profile.ProjectName,
                    Author = Environment.UserName ?? "reviewer",
                    WhenTicks = DateTime.UtcNow.Ticks,
                });

                var reviewer = Safe(Environment.UserName ?? "reviewer");
                var beside = System.IO.Path.GetDirectoryName(round.Path) ?? string.Empty;
                var folder = System.IO.Path.Combine(beside, "markups");
                var path = System.IO.Path.Combine(folder, reviewer + ClashFile.Extension);

                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                    System.IO.File.WriteAllBytes(path, written.Bytes);
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

                    byte[] bytes;

                    try
                    {
                        bytes = System.IO.File.ReadAllBytes(file);
                    }
                    catch (Exception e)
                    {
                        problems.Add(System.IO.Path.GetFileName(file) + ": " + Ui.Describe(e));
                        continue;
                    }

                    var markup = ClashFile.Read(bytes);

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
                        files.AddRange(System.IO.Directory.GetFiles(folder)
                            .Where(f => Markup(f))
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
                var carried = Harvest(_read);

                _read = null;

                var entries = new List<TestEntry>();

                foreach (var test in _session.Clash.Tests())
                {
                    var entry = new TestEntry(test);

                    foreach (var result in _session.Clash.Results(test.Id))
                        entry.Rows.Add(Restored(new Row(test.Id, test.Name, result), carried));

                    entries.Add(entry);
                }

                _read = entries;
                RedrawTests();

                Tell(did, problems);
            }

            /// <summary>
            /// The custom column values a read is about to replace, keyed by result — so a run,
            /// a rename or a sync does not cost the property read somebody already paid for.
            /// </summary>
            private static Dictionary<string, Dictionary<string, string>> Harvest(List<TestEntry>? read)
            {
                var kept = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                if (read == null) return kept;

                foreach (var row in read.SelectMany(e => e.Rows))
                    if (row.Custom.Count > 0)
                        kept[row.TestId + "\u001f" + row.Info.Id] = row.Custom;

                return kept;
            }

            /// <summary>
            /// A freshly read row, with what this bench knows and the document does not put back.
            ///
            /// Two things travel across a re-read: the custom column values, which cost a property
            /// read each and would otherwise be re-fetched for no reason, and the held edits — the
            /// decisions a read-only document refused. Without the second the board would forget
            /// the afternoon's work the moment somebody pressed Read again.
            /// </summary>
            private Row Restored(Row row,
                                 Dictionary<string, Dictionary<string, string>> carried)
            {
                if (carried.TryGetValue(row.TestId + "\u001f" + row.Info.Id, out var values))
                    foreach (var pair in values) row.Custom[pair.Key] = pair.Value;

                Apply(Held.Find(row.TestId, row.Info.Id), row);

                return row;
            }

            /// <summary>The register of refused edits. Never null once a screen exists.</summary>
            private static HeldEdits Held => _heldEdits ??= new HeldEdits();

            /// <summary>Lay a held edit over a row, so the board reads the way the coordinator left it.</summary>
            private static void Apply(HeldEdit? edit, Row row)
            {
                if (edit == null) return;

                if (edit.Status != null) row.Status = edit.Status;
                if (edit.Assigned != null) row.Assigned = edit.Assigned;
                if (edit.Group != null) row.Group = edit.Group;

                foreach (var comment in edit.Comments)
                {
                    row.Trail.Add(comment);
                    row.Comments++;
                    row.LastComment = comment.Body;
                }
            }

            /// <summary>
            /// Offer the held edits to the document again, and keep only what it still refuses.
            ///
            /// The case this is for: the coordinator marked up the NWD on site, then opened the
            /// NWF the results actually live in. One press moves the week's decisions across.
            /// </summary>
            private void PushHeld()
            {
                if (Held.IsEmpty) return;

                var written = 0;
                var refused = 0;
                var problems = new List<string>();

                foreach (var byTest in Held.All.ToList()
                                           .GroupBy(e => e.TestId, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var edit in byTest)
                    {
                        var ids = new[] { edit.ResultId };
                        var all = true;

                        if (edit.Status != null)
                            all &= Kept(_session.Clash.SetStatus(byTest.Key, ids, edit.Status), problems);

                        if (edit.Assigned != null)
                            all &= Kept(_session.Clash.Assign(byTest.Key, ids, edit.Assigned), problems);

                        if (edit.Group != null)
                            all &= Kept(_session.Clash.Regroup(byTest.Key, ids, edit.Group), problems);

                        foreach (var comment in edit.Comments)
                            all &= Kept(_session.Clash.Comment(byTest.Key, ids, comment.Body,
                                                               comment.Author), problems);

                        // Released only when every part landed: a half-written edit that stopped
                        // being held would lose the half the document refused.
                        if (all)
                        {
                            Held.Release(edit.TestId, edit.ResultId);
                            written++;
                        }
                        else
                        {
                            refused++;
                        }
                    }
                }

                KeepHeld();
                ArmSync();
                RedrawTests();

                Tell(written == 0
                        ? "This document still will not take them. They are still kept here."
                        : Ui.Count(written, "held edit") + " written into the document"
                          + (refused > 0
                              ? "; " + Ui.Count(refused, "other") + " still refused and still kept."
                              : ". Nothing is waiting now."),
                     problems.Distinct().Take(3).ToList());
            }

            /// <summary>One host answer, with its complaint collected the first time it is heard.</summary>
            private bool Kept(bool ok, List<string> problems)
            {
                if (!ok) problems.Add(_session.Clash.LastProblem ?? "The host refused.");

                return ok;
            }

            /// <summary>The Sync button, kept so its state can be refreshed as the source changes.</summary>
            private System.Windows.Controls.Button? _syncButton;

            /// <summary>
            /// Why this document cannot take the changes, or null when it can.
            ///
            /// Four reasons, and each one is a different thing to do about it, so each says so
            /// rather than all four sharing a grey button and a shrug.
            /// </summary>
            private string? WhySync()
            {
                if (OnFile)
                    return "A file is open for markup, not the model. Save the markup and run the "
                           + "sync from the seat that has the model open.";

                if (!_session.Clash.IsAvailable)
                    return "This Navisworks edition has no clash engine, so there is nothing to "
                           + "write into. " + (_session.ClashProblem ?? string.Empty).Trim();

                if (_session.IsPublished)
                    return "This is a published NWD: its clash results are read-only and a "
                           + "published file cannot be saved back into. Open the NWF the models "
                           + "were federated in and this button writes the same changes into it.";

                if (!_session.Clash.CanEdit)
                    return "This document's clash results will not take an edit. Open the NWF the "
                           + "models were federated in.";

                return null;
            }

            /// <summary>Show the Sync button as available or not, and say which in a word.</summary>
            private void ArmSync()
            {
                if (_syncButton == null) return;

                var why = WhySync();

                _syncButton.IsEnabled = why == null;

                _syncButton.ToolTip = why
                    ?? "Writes every held decision into the document's own clash results, where "
                       + "the native clash window and every export can see them.";

                // Kept visible rather than hidden when it cannot run: a button that disappears
                // teaches nobody that the sync exists, and it is the thing somebody came for.
                _syncButton.Content = Held.IsEmpty
                    ? "Sync into the document"
                    : "Sync " + Ui.Count(Held.Count, "change") + " into the document";
            }

            /// <summary>
            /// Save the changes where this source keeps them.
            ///
            /// One button, two files, because it is one act: a file being marked up saves a
            /// markup, and the model saves a baseline. Both carry statuses, assignments, comment
            /// trails and groups, and both are what the other seat reads.
            /// </summary>
            private void SaveChanges()
            {
                if (OnFile) SaveMarkup();
                else SaveBaseline();
            }

            /// <summary>Write the held decisions into the document, when it will have them.</summary>
            private void Sync()
            {
                var why = WhySync();

                if (why != null)
                {
                    Tell("Nothing was written.", new List<string> { why });
                    return;
                }

                if (Held.IsEmpty)
                {
                    Tell("Nothing is waiting to be written - every change on this board is "
                         + "already in the document.", null);
                    return;
                }

                PushHeld();
            }

            /// <summary>List what is waiting, so somebody can see it before deciding anything.</summary>
            private void ShowHeld()
            {
                _log.Children.Clear();

                if (Held.IsEmpty)
                {
                    _log.Children.Add(Ui.Line("Nothing is waiting.", 0.75));
                    return;
                }

                var read = Current();

                string Name(HeldEdit edit) =>
                    read?.FirstOrDefault(e => string.Equals(e.Test.Id, edit.TestId,
                                                            StringComparison.OrdinalIgnoreCase))
                        ?.Test.Name ?? edit.TestId;

                _log.Children.Add(Ui.Eyebrow("Waiting to be written"));

                var table = Ui.Table(
                    Held.All.OrderBy(e => Name(e), StringComparer.OrdinalIgnoreCase)
                            .ThenBy(e => e.ResultId, StringComparer.OrdinalIgnoreCase)
                            .Select(e => new TableRow(e, Name(e), e.ResultId, e.Describe())),
                    ("Test", 200), ("Result", 140), ("What is held", 0));

                _log.Children.Add(table);

                _log.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "held edit")),
                    new Ui.OnPicked("Forget {n}", (picked, say) =>
                    {
                        foreach (var edit in picked.OfType<HeldEdit>())
                            Held.Release(edit.TestId, edit.ResultId);

                        KeepHeld();
                        say.Children.Add(Ui.Done("Forgotten. Press \"Read the results\" to see "
                            + "the board the way the document holds it."));
                    })));
            }

            /// <summary>Throw the held edits away, on purpose.</summary>
            private void ForgetHeld()
            {
                // Two presses, because there is no undo: the first says what would be lost, the
                // second does it. A confirmation dialog would be the same question in a window
                // somebody has to close.
                if (!_forgetArmed)
                {
                    _forgetArmed = true;

                    Tell("Press \"Forget them\" again to throw away "
                         + Ui.Count(Held.Count, "held edit")
                         + ". They have not been written into any document, so once forgotten "
                         + "they are gone - unless a baseline was saved while they were held.",
                         null);

                    return;
                }

                _forgetArmed = false;

                Held.Clear();
                KeepHeld();
                ArmSync();
                RedrawTests();
                Tell("Forgot the held edits. Press \"Read the results\" to see the board the "
                     + "way the document holds it.", null);
            }

            /// <summary>Whether "Forget them" has been pressed once already.</summary>
            private bool _forgetArmed;

            /// <summary>
            /// Write the register into the project file.
            ///
            /// Failing to save is an ordinary outcome — a document nobody has saved has no project
            /// file — and never a reason to refuse the edit: it still applies on the board and
            /// still travels out in the baseline.
            /// </summary>
            private void KeepHeld()
            {
                try
                {
                    _session.Store.Section(ProjectStore.ClashSection).Set(HeldKey, Held.ToJson());
                    _session.Store.Save();
                }
                catch (Exception)
                {
                    // Nowhere to write. The edits are still on the board and still exportable.
                }
            }

            /// <summary>
            /// Whether a file in the markups folder is one a reviewer saved.
            ///
            /// By extension here rather than by content, because this is a listing of a folder
            /// rather than a read of one file: opening every file in it to find out costs a read
            /// per file, and a reviewer's markup is one of two things by name.
            /// </summary>
            private static bool Markup(string path)
            {
                var extension = System.IO.Path.GetExtension(path);

                return extension.Equals(ClashFile.Extension, StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".bcf", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
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
