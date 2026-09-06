using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The one door a clash file goes out of and comes back through.
    ///
    /// A coordination round has two halves and they are weeks apart. On Monday the coordinator
    /// sends each trade the clashes that are theirs: not the board, a pack per recipient, narrowed
    /// to what that trade is being asked about. On Thursday the packs come back, each holding the
    /// same clashes with a handful answered, and the answers have to land on the board without
    /// anybody retyping them.
    ///
    /// Both halves are here because they are one round. They were three tabs - one that wrote a
    /// file, one that read a file, one that merged several - so setting up an issue and taking one
    /// back were in different places, and neither said what the other had done.
    ///
    /// BCF, and only BCF. It is what Solibri, BIMcollab, Revizto, BIMTrack and the issue trackers
    /// projects are actually run on, so a pack written here is one the architect can open.
    /// </summary>
    public static class ClashExchangeViews
    {
        /// <summary>Where the profiles live in the project file.</summary>
        public const string Key = "exportProfiles";

        /// <summary>What a profile file is called on disk.</summary>
        public const string Extension = ".cwexport";

        /// <summary>
        /// Test ids the clash manager asked to send, applied to the next desk once.
        ///
        /// The hand-off from the Manage tab: "export these three tests" should arrive with those
        /// three ticked, not with a memory test about which three they were.
        /// </summary>
        internal static IReadOnlyList<string>? Preferred { get; set; }

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Surface(Session session) =>
            Drawn(session.Store, session.Profile.ProjectName, findings: session.Findings);

        /// <summary>
        /// The same surface from the project file alone, so it can be drawn without a document.
        ///
        /// A Navisworks document cannot be made outside Navisworks, so a surface that took one
        /// could only ever be looked at on a licensed seat and described in words. Everything this
        /// screen needs is the project file and the board the clash run left behind.
        /// </summary>
        /// <param name="store">The project file, which holds the profiles and the storeys.</param>
        /// <param name="projectName">The project's name, for the packs it writes.</param>
        /// <param name="board">The board to send, or null to take the last clash run's.</param>
        /// <param name="importing">True to open on the import half rather than the export half.</param>
        /// <param name="findings">
        /// Where an applied answer is written to last. Null draws the screen without one, which is
        /// what the harness does; applying then reaches the live board and says so.
        /// </param>
        public static UIElement Drawn(ProjectStore store, string? projectName = null,
                                      IReadOnlyList<ClashGroup>? board = null,
                                      bool importing = false, FindingStore? findings = null) =>
            new Desk(store, projectName ?? "Untitled project", board, importing, findings).View;

        /// <summary>The two halves of a round.</summary>
        private const string SendOut = "Export";

        /// <summary>The two halves of a round.</summary>
        private const string TakeBack = "Import";

        private sealed class Desk
        {
            private readonly ProjectStore _store;
            private readonly string _projectName;

            private readonly List<ExportProfile> _profiles = new List<ExportProfile>();
            private ExportProfile _profile = new ExportProfile();

            private readonly ContentControl _half = new ContentControl();

            // -------------------------------------------------------------------------------
            // The send-out half
            // -------------------------------------------------------------------------------

            private readonly StackPanel _tests = Ui.Stack();
            private readonly StackPanel _testsSaid = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _resultsSaid = Ui.Stack();
            private readonly StackPanel _sending = Ui.Stack();

            /// <summary>Clashes left out by hand, on top of what the filter already removed.</summary>
            private readonly HashSet<Core.Identity.ClashKey> _dropped =
                new HashSet<Core.Identity.ClashKey>();
            private readonly TextBox _find = Ui.Text(string.Empty, "a test or a folder", null, 200);

            private readonly TextBox _named = Ui.Text("Everything", null, 180);

            private string _search = string.Empty;
            private System.Windows.Threading.DispatcherTimer? _typing;

            // -------------------------------------------------------------------------------
            // The take-back half
            // -------------------------------------------------------------------------------

            /// <summary>The packs chosen, by path, in the order they were chosen.</summary>
            private readonly List<string> _returns = new List<string>();

            private readonly StackPanel _files = Ui.Stack();
            private readonly StackPanel _packsSaid = Ui.Stack();
            private readonly StackPanel _back = Ui.Stack();
            private readonly StackPanel _rows = Ui.Stack();
            private readonly StackPanel _rowsSaid = Ui.Stack();
            private readonly StackPanel _applied = Ui.Stack();

            /// <summary>Which returned rows the table lists.</summary>
            private string _showing = Everything;

            private ExchangeReading? _reading;

            /// <summary>The primary of the import half, kept so reading a pack can turn it on.</summary>
            private FrameworkElement? _apply;

            private UIElement? _out;
            private UIElement? _in;

            private readonly IReadOnlyList<ClashGroup>? _board;

            private readonly FindingStore? _findings;

            internal Desk(ProjectStore store, string projectName,
                          IReadOnlyList<ClashGroup>? board = null, bool importing = false,
                          FindingStore? findings = null)
            {
                _store = store;
                _projectName = projectName;
                _board = board;
                _importing = importing;
                _findings = findings;

                Load();

                View = Build();
            }

            /// <summary>Which half is on show when the desk opens.</summary>
            private readonly bool _importing;

            internal UIElement View { get; }

            // -------------------------------------------------------------------------------
            // The board
            // -------------------------------------------------------------------------------

            private IReadOnlyList<ClashGroup> Board() =>
                _board ?? CoordinateViews.LastBoard?.Groups ?? Array.Empty<ClashGroup>();

            /// <summary>
            /// Every clash on the board, flattened once.
            ///
            /// Drawing the export half asks for this a dozen times over — the facets, the values
            /// behind each filter chip, the counts, the selection — and each ask walked every
            /// group of an eighteen-thousand-result board and built a new list.
            /// </summary>
            private IReadOnlyList<ClashItem> Results()
            {
                var board = Board();

                if (_flattened != null && ReferenceEquals(_resultsFrom, board)) return _flattened;

                _flattened = board.SelectMany(one => one.Items).Where(one => one != null).ToList();
                _resultsFrom = board;

                return _flattened;
            }

            private IReadOnlyList<ClashItem>? _flattened;
            private IReadOnlyList<ClashGroup>? _resultsFrom;

            /// <summary>
            /// What has already gone out, read once and kept for the life of the desk.
            /// </summary>
            private ClashIssueLog Issued() => _issued ??= ClashIssueLog.FromJson(
                _store.Section(ProjectStore.ClashSection)[ClashIssueLog.Key]);

            private ClashIssueLog? _issued;

            /// <summary>The project's storeys, so a filter can name one.</summary>
            private LevelPlan Storeys() => LevelPlan.FromJson(
                _store.Section(ProjectStore.ProfileSection)[LevelPlan.Key]);

            // -------------------------------------------------------------------------------
            // Layout
            // -------------------------------------------------------------------------------

            private UIElement Build()
            {
                _out = SendingOut();
                _in = TakingBack();

                var first = _importing ? _in : _out;

                if (first != null) _half.Content = Ui.Loose(first);

                return _half;
            }

            /// <summary>Put the chosen half on show, detaching the other first.</summary>
            /// <param name="half">Which half was pressed.</param>
            private void Half(string half)
            {
                // Detached before the other goes in, or the half being left is still claimed by
                // this holder when it is handed back to it.
                var wanted = half == TakeBack ? _in : _out;

                _half.Content = null;

                if (wanted != null) _half.Content = Ui.Loose(wanted);
            }

            private UIElement Switch(string here) =>
                Ui.Segmented(new[] { SendOut, TakeBack }, here, Half);

            // -------------------------------------------------------------------------------
            // -------------------------------------------------------------------------------
            // Exporting
            // -------------------------------------------------------------------------------

            /// <summary>
            /// A strip, two tables and one action row - the anatomy the rest of the product uses
            /// for a bench worked in rather than a tool configured once.
            ///
            /// Left is the tests, right is their results, because that is how a clash is reached:
            /// it belongs to a test, and the two are filtered by different things. A test by its
            /// name, its folder and how much it holds; a result by its status, who it is assigned
            /// to and the storey it stands on. Both are tables, so both sort by heading, pick by
            /// click and shift-click, and act on the count.
            ///
            /// The strip holds what is changed while working - the profile, the search, the split.
            /// Everything set up once is behind the two symbols on the action row.
            /// </summary>
            private UIElement SendingOut()
            {
                var strip = Ui.Across(
                    Ui.Field("Half", Switch(SendOut)),
                    Ui.Field("Export profile", Saved()),
                    Ui.Field("Find a test", _find),
                    Ui.Field("One file per", Splitting()),
                    Ui.Field("Order inside each file", Sorting()));

                var foot = new WrapPanel();

                // Labelled. Two symbols beside a primary say nothing about what they open, and
                // the funnel is the same picture the results filter uses a few inches away.
                var scope = Ui.Quiet("Export scope\u2026", () => Configure("in"));
                var saved = Ui.Quiet("Save as a profile\u2026", () => Configure("profile"));

                scope.ToolTip = "Which statuses go out, and whether folders travel with them.";
                saved.ToolTip = "Keep these settings under a name, so the next round is one press.";
                scope.VerticalAlignment = VerticalAlignment.Center;
                saved.VerticalAlignment = VerticalAlignment.Center;
                saved.Margin = new Thickness(6, 0, 0, 0);

                foot.Children.Add(scope);
                foot.Children.Add(saved);
                foot.Children.Add(new Border { Width = 12 });

                foot.Children.Add(Ui.Commit("Preview the export", "Write", "pack",
                                            Rehearse, Publish));

                // Live, but not per keystroke. Every letter rebuilds the tests table, which on a
                // job of any size is a tenth of a second between one letter and the next.
                if (_typing == null)
                {
                    _typing = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(250),
                    };

                    _typing.Tick += Settled;
                    _find.TextChanged += (s, e) => { _typing.Stop(); _typing.Start(); };
                }

                DrawTests();
                DrawResults();

                // A pane's label and its "say" share one clipped row, so the say is one short
                // line and everything else - the filter bar, the picking bar - is in the body
                // with the table it acts on.
                return Bench.Split(strip,
                                   Bench.Pane("Tests", _testsSaid, _tests, null, scrolls: false),
                                   Bench.Pane("Results", _resultsSaid, _results, null,
                                              scrolls: false),
                                   Ui.Stack(foot, _sending),
                                   leftWidth: 460);
            }

            /// <summary>The saved profiles, as a chooser for the strip.</summary>
            private UIElement Saved()
            {
                var names = _profiles.Select(one => one.Name).ToList();

                if (names.Count == 0) names.Add(_profile.Name);

                return Ui.Choice(names, _profile.Name, Pick, 180);
            }

            /// <summary>Redraw the tests table once the typing in the find box has stopped.</summary>
            /// <param name="sender">The timer.</param>
            /// <param name="e">Unused.</param>
            private void Settled(object? sender, EventArgs e)
            {
                _typing?.Stop();

                var said = _find.Text.Trim();

                if (string.Equals(said, _search, StringComparison.Ordinal)) return;

                _search = said;
                DrawTests();
            }

            /// <summary>Draw both tables and the lines that count them.</summary>
            private void Redraw()
            {
                DrawTests();
                DrawResults();
            }

            // -------------------------------------------------------------------------------
            // The tests
            // -------------------------------------------------------------------------------

            /// <summary>Every test the board holds, in its folder, with its result count.</summary>
            private IReadOnlyList<BoardTest> Tests()
            {
                var byName = new Dictionary<string, BoardTest>(StringComparer.OrdinalIgnoreCase);
                var order = new List<BoardTest>();

                foreach (var item in Results())
                {
                    if (!byName.TryGetValue(item.TestName, out var test))
                    {
                        test = new BoardTest(item.TestName, item.TestFolder);
                        byName[item.TestName] = test;
                        order.Add(test);
                    }

                    test.Count++;
                }

                return order
                    .OrderBy(one => one.Folder ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(one => one.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            /// <summary>Whether this test is included. A profile that knows nothing sends everything.</summary>
            /// <param name="name">The test.</param>
            private bool Sending(string name)
            {
                var found = _profile.Tests.FirstOrDefault(
                    one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

                return found?.Chosen ?? true;
            }

            /// <summary>Include or exclude a test, adding it to the profile if it had never seen it.</summary>
            /// <param name="name">The test.</param>
            /// <param name="on">Whether it goes.</param>
            private void Send(string name, bool on)
            {
                var found = _profile.Tests.FirstOrDefault(
                    one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

                if (found == null)
                {
                    // The profile has to hold every test before one can be left out, or excluding
                    // one would read as "send only that one" - an empty list means everything.
                    foreach (var test in Tests())
                        if (!_profile.Tests.Any(held => string.Equals(held.Name, test.Name,
                                                                      StringComparison.OrdinalIgnoreCase)))
                            _profile.Add(test.Name);

                    found = _profile.Tests.FirstOrDefault(
                        one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));
                }

                if (found != null) found.Chosen = on;
            }

            private void DrawTests()
            {
                _tests.Children.Clear();
                _testsSaid.Children.Clear();

                var all = Tests();

                if (all.Count == 0)
                {
                    _tests.Children.Add(Ui.Empty("No tests on the board.",
                                                 "Run them on the Manage tab first."));

                    return;
                }

                var showing = _search.Length == 0
                    ? all
                    : all.Where(one => Holds(one.Name) || Holds(one.Folder)).ToList();

                var rows = showing.Select(one => new TableRow(one,
                    string.Empty,
                    one.Name,
                    one.Folder ?? string.Empty,
                    one.Count.ToString("N0", CultureInfo.InvariantCulture),
                    ExportProfile.When(_profile.Tests.FirstOrDefault(
                        held => string.Equals(held.Name, one.Name,
                                              StringComparison.OrdinalIgnoreCase))))
                {
                    Tone = Tone.Plain,
                }.Ticking(Sending(one.Name), on => Included(new object[] { one }, null, on, rebuild: false)))
                    .ToList();

                var table = Ui.Table(rows,
                    ("In", 34), ("Test", 0), ("Folder", 100), ("Results", 62),
                    ("Last sent", 76)).Multi().Ticking(0);

                Ui.SetFills(_tests, true);

                _tests.Children.Add(Ui.Loose(table));

                _tests.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Include these {n}", (picked, said) =>
                        Included(picked, said, on: true), primary: true),
                    new Ui.OnPicked("Exclude these {n}", (picked, said) =>
                        Included(picked, said, on: false))));

                DrawTestsSaid();
            }

            /// <summary>
            /// How much of the board is going, without rebuilding the table under it.
            /// </summary>
            private void DrawTestsSaid()
            {
                _testsSaid.Children.Clear();

                var all = Tests();
                var chosen = all.Count(one => Sending(one.Name));

                _testsSaid.Children.Add(Ui.Small(
                    chosen + " of " + all.Count + " included · "
                    + Ui.Count(all.Where(one => Sending(one.Name)).Sum(one => one.Count),
                               "result")));
            }

            /// <summary>Include or exclude the tests picked in the table.</summary>
            /// <param name="picked">The rows picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            /// <param name="on">Whether they go.</param>
            /// <summary>Put tests in the export, or take them out.</summary>
            /// <param name="picked">The tests.</param>
            /// <param name="said">Where the picking bar says what it did, or null for a tick box
            /// on a single row, which says it by moving.</param>
            /// <param name="on">True to include them.</param>
            /// <param name="rebuild">
            /// False for a tick box, which is inside the table this would tear down and rebuild:
            /// the box is mid-click, and rebuilding from inside its own binding update throws
            /// away the selection and the scroll position and re-flattens the whole board. The
            /// box has already moved, so only what depends on it needs redrawing.
            /// </param>
            private void Included(IReadOnlyList<object> picked, Panel? said, bool on,
                                  bool rebuild = true)
            {
                var tests = picked.OfType<BoardTest>().ToList();

                if (tests.Count == 0)
                {
                    said?.Children.Add(Ui.Note("Select at least one test."));
                    return;
                }

                foreach (var test in tests) Send(test.Name, on);

                said?.Children.Add(Ui.Note(
                    Ui.Count(tests.Count, "test") + (on ? " included." : " excluded.")));

                if (rebuild) Redraw();
                else { DrawTestsSaid(); DrawResults(); }
            }

            // -------------------------------------------------------------------------------
            // The results
            // -------------------------------------------------------------------------------

            /// <summary>What the tests and the filter between them let through.</summary>
            private IReadOnlyList<ClashItem> Scope()
            {
                var kept = _profile.Choose(Results(), Board(), Storeys(), Issued()).Items;

                return _dropped.Count == 0
                    ? kept
                    : kept.Where(one => !_dropped.Contains(one.Key)).ToList();
            }

            private void DrawResults()
            {
                _results.Children.Clear();
                _resultsSaid.Children.Clear();

                _results.Children.Add(Filters());

                var scope = Scope();

                if (scope.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing in scope.",
                        Tests().Any(one => Sending(one.Name))
                            ? "No result of the included tests meets the filter."
                            : "No test is included."));

                    return;
                }

                var rows = scope.Take(2000).Select(one => new TableRow(one,
                    one.Name ?? one.Key.ToString(),
                    one.TestName,
                    one.Status.ToString(),
                    one.AssignedTo ?? string.Empty,
                    one.Level ?? string.Empty)).ToList();

                // Every column fixed. With the first one flexible it took the whole leftover
                // width and pushed the last column past the right edge of the pane.
                var table = Ui.Table(rows,
                    ("Clash", 150), ("Test", 130), ("Status", 80),
                    ("Assigned to", 100), ("Level", 56)).Multi();

                Ui.SetFills(_results, true);

                _results.Children.Add(Ui.Loose(table));

                _results.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Leave out these {n}", (picked, said) =>
                    {
                        var held = picked.OfType<ClashItem>().ToList();

                        if (held.Count == 0)
                        {
                            said.Children.Add(Ui.Note("Select at least one clash."));
                            return;
                        }

                        foreach (var one in held) _dropped.Add(one.Key);

                        said.Children.Add(Ui.Note(
                            Ui.Count(held.Count, "clash") + " left out of the packs."));

                        Redraw();
                    })));

                var rounds = Rounds(scope);

                _resultsSaid.Children.Add(Ui.Small(
                    scope.Count.ToString("N0", CultureInfo.InvariantCulture) + " of "
                    + Ui.Count(Results().Count, "result") + " · "
                    + Ui.Count(rounds.Count, "pack")));

                // What the pack carries beyond the list. The writer puts a camera and a snapshot
                // on every topic and never said so, which is why the export read as a file of
                // titles a reviewer would have to find in the model themselves.
                _resultsSaid.Children.Add(Ui.Small(
                    "Each clash goes out as a topic with a viewpoint: a camera on the clash "
                    + "point, both elements highlighted, and a snapshot."));

                if (_dropped.Count > 0)
                    _results.Children.Add(Ui.Across(
                        Ui.Small(Ui.Count(_dropped.Count, "clash") + " left out by hand."),
                        Ui.Link("Put them back",
                                () => Ui.Safely(() => { _dropped.Clear(); Rebuild(); }))));
            }

            /// <summary>
            /// The filter bar: one chip per parameter, saying where it stands.
            ///
            /// The common four are always drawn - status, who it is assigned to, the storey and
            /// the level - so the bar answers "what am I sending them" before anybody adds
            /// anything. Every other parameter the board carries is one press further on.
            /// </summary>
            private UIElement Filters()
            {
                var bar = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };

                var says = Ui.Small("Only results where");

                says.VerticalAlignment = VerticalAlignment.Center;
                says.Margin = new Thickness(0, 2, 8, 2);

                bar.Children.Add(says);

                var facets = new List<ClashFacet>();

                foreach (var key in new[] { "status", "assignee", "storey", "level" })
                {
                    var found = ClashFacets.Of(key, Results(), Storeys(), Issued());

                    if (found != null) facets.Add(found);
                }

                // Anything already filtered on, so a clause added from the menu gets a chip of its
                // own rather than disappearing.
                foreach (var clause in _profile.Pick.Clauses)
                {
                    if (facets.Any(one => string.Equals(one.Key, clause.Facet,
                                                        StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var found = ClashFacets.Of(clause.Facet, Results(), Storeys(), Issued());

                    if (found != null) facets.Add(found);
                }

                foreach (var facet in facets) bar.Children.Add(Chip(facet));

                var add = Ui.Link("and one more…", () => { });

                add.Click += (s, e) => Ui.Safely(() => Parts.PopMenu(add, Facets()));
                add.Margin = new Thickness(4, 2, 0, 2);
                add.VerticalAlignment = VerticalAlignment.Center;

                bar.Children.Add(add);

                // What the filters are keeping out. A bar of chips says what is let through and
                // never how much of the board that leaves behind, which is the number a
                // coordinator is actually deciding on.
                var held = Results().Count;
                var kept = _profile.Pick.Take(Results(), Storeys(), Issued()).Count;

                if (kept < held)
                {
                    var left = Ui.Small(Ui.Count(held - kept, "result") + " left out");

                    left.VerticalAlignment = VerticalAlignment.Center;
                    left.Margin = new Thickness(10, 2, 0, 2);

                    bar.Children.Add(left);
                }

                return bar;
            }

            /// <summary>One parameter, as a chip that says which of its values are let through.</summary>
            /// <param name="facet">The parameter.</param>
            private UIElement Chip(ClashFacet facet)
            {
                var here = facet;

                var clause = _profile.Pick.Clauses.FirstOrDefault(
                    one => string.Equals(one.Facet, here.Key, StringComparison.OrdinalIgnoreCase));

                var button = Ui.Quiet(
                    here.Title + ": " + (clause == null ? "any" : Values(clause)),
                    () => Ui.Safely(() => Choose(here)));

                button.Margin = new Thickness(0, 2, 6, 2);
                button.VerticalAlignment = VerticalAlignment.Center;
                button.ToolTip = clause == null
                    ? "Every value goes. Press to choose which."
                    : clause.Describe(here) + ". Press to change it.";

                // A set filter is tinted, so the bar reads at a glance as which parameters are
                // narrowing the export and which are letting everything through.
                if (clause == null)
                {
                    button.Margin = new Thickness(0, 2, 6, 2);

                    return button;
                }

                button.Tint(System.Windows.Controls.Control.ForegroundProperty, "Accent");
                button.Margin = new Thickness(0, 2, 0, 2);

                // A filter in force is a chip you can take off. Every one of them was a link that
                // reopened the value list, so clearing one meant opening it and unticking whatever
                // was in there.
                var off = Ui.Icon(Glyphs.Close, "Stop filtering on " + here.Title, () => Ui.Safely(() =>
                {
                    _profile.Pick.Clauses.Remove(clause);
                    Rebuild();
                }));

                off.VerticalAlignment = VerticalAlignment.Center;
                off.Margin = new Thickness(0, 2, 6, 2);

                var chip = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                chip.Children.Add(button);
                chip.Children.Add(off);

                return chip;
            }

            /// <summary>The values a clause names, short enough for a chip.</summary>
            /// <param name="clause">The clause.</param>
            private static string Values(ClashClause clause)
            {
                if (clause.Values.Count == 0) return "a range";

                var said = string.Join(", ", clause.Values.Take(2));

                return clause.Values.Count > 2
                    ? said + " +" + (clause.Values.Count - 2).ToString(CultureInfo.InvariantCulture)
                    : said;
            }

            private bool Holds(string? said) =>
                said != null && said.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

            /// <summary>One test on the board: what it is called, where it sits, how much it holds.</summary>
            private sealed class BoardTest
            {
                internal BoardTest(string name, string? folder)
                {
                    Name = name;
                    Folder = folder;
                }

                /// <summary>What it is called.</summary>
                internal string Name { get; }

                /// <summary>The folder it sits in, or null.</summary>
                internal string? Folder { get; }

                /// <summary>How many results it holds.</summary>
                internal int Count { get; set; }

                /// <inheritdoc />
                public override string ToString() => Name;
            }

            /// <summary>Every parameter a result filter can be about, as a menu.</summary>
            private IReadOnlyList<Parts.Entry> Facets()
            {
                var items = new List<Parts.Entry> { Parts.Entry.Heading("Filter by") };

                foreach (var facet in ClashFacets.Every(Results(), Storeys(), Issued()))
                {
                    var here = facet;

                    items.Add(Parts.Entry.Act(here.Title, null, () => Choose(here)));
                }

                return items;
            }

            /// <summary>Ask which values of one parameter go in, from the ones the board holds.</summary>
            /// <param name="facet">The parameter chosen.</param>
            private void Choose(ClashFacet facet)
            {
                var held = ClashFacets.Values(facet, Results());

                if (held.Count == 0)
                {
                    Ui.Told("No clash on the board carries a value for "
                            + facet.Title.ToLowerInvariant() + ".");

                    return;
                }

                var standing = _profile.Pick.Clauses.FirstOrDefault(
                    one => string.Equals(one.Facet, facet.Key, StringComparison.OrdinalIgnoreCase));

                var box = Ui.Stack();

                var taken = new HashSet<string>(standing?.Values ?? Array.Empty<string>(),
                                                StringComparer.OrdinalIgnoreCase);

                foreach (var value in held.Take(60))
                {
                    var here = value;

                    box.Children.Add(Ui.Check(here, taken.Contains(here), on =>
                    {
                        if (on) taken.Add(here);
                        else taken.Remove(here);
                    }));
                }

                var page = new Ui.Page(facet.Title,
                    () => Bench.Group(facet.Title + " values to include", box));

                Ui.Configure(facet.Title, () =>
                {
                    // One clause per parameter. A second clause on the same one would have to hold
                    // as well as the first, so choosing New and then Active would send nothing.
                    if (standing != null) _profile.Pick.Clauses.Remove(standing);

                    if (taken.Count > 0)
                    {
                        var clause = _profile.Pick.Add(facet.Key);

                        foreach (var one in taken) clause.Values.Add(one);
                    }

                    Rebuild();
                }, page);
            }

            /// <summary>
            /// Which parameter the run is split by, so one press writes a pack each.
            ///
            /// A chooser rather than a link, and named for what it does to the output: "one file
            /// per assigned to" writes the mechanical contractor's clashes into one pack and the
            /// structural engineer's into another. "Nothing" writes them all into one.
            /// </summary>
            private UIElement Splitting()
            {
                var offered = new List<string> { Whole };

                offered.AddRange(ClashFacets.Every(Results(), Storeys(), Issued()).Select(one => one.Title));

                var here = _profile.SplitBy == null ? Whole : Titled(_profile.SplitBy);

                if (!offered.Contains(here)) offered.Add(here);

                var box = Ui.Choice(offered, here, said =>
                {
                    _profile.SplitBy = said == Whole ? null : Keyed(said);
                    Rebuild();
                }, 170);

                box.ToolTip = "Writes one pack per value, each holding only that party's clashes. "
                              + "Nothing writes a single pack.";

                return box;
            }

            /// <summary>Which parameter the results are written in the order of.</summary>
            private UIElement Sorting()
            {
                var offered = new List<string> { AsFound };

                offered.AddRange(ClashFacets.Every(Results(), Storeys(), Issued()).Select(one => one.Title));

                var here = _profile.Pick.Sort == null ? AsFound : Titled(_profile.Pick.Sort);

                if (!offered.Contains(here)) offered.Add(here);

                var box = Ui.Choice(offered, here, said =>
                {
                    _profile.Pick.Sort = said == AsFound ? null : Keyed(said);
                    Rebuild();
                }, 150);

                box.ToolTip = "The order the clashes are written in, which is not the order the "
                              + "table happens to be sorted in.";

                var way = Ui.Icon(_profile.Pick.Descending ? Glyphs.Down : Glyphs.Up,
                                  _profile.Pick.Descending ? "Largest first" : "Smallest first",
                                  () => Ui.Safely(() =>
                                  {
                                      _profile.Pick.Descending = !_profile.Pick.Descending;
                                      Rebuild();
                                  }));

                way.IsEnabled = _profile.Pick.Sort != null;

                return Ui.Across(box, way);
            }

            /// <summary>What the split chooser calls writing everything into one pack.</summary>
            private const string Whole = "Nothing - one file";

            /// <summary>What the order chooser calls leaving the results as they came.</summary>
            private const string AsFound = "As they came";

            /// <summary>The saved key behind a parameter's on-screen title.</summary>
            /// <param name="title">What it is called on screen.</param>
            private string? Keyed(string? title) =>
                ClashFacets.Every(Results(), Storeys(), Issued())
                    .FirstOrDefault(one => string.Equals(one.Title, title,
                                                         StringComparison.OrdinalIgnoreCase))?.Key;

            /// <summary>Draw the whole half again, for the controls captioned by their own value.</summary>
            private void Rebuild()
            {
                _out = SendingOut();

                if (ReferenceEquals(_half.Content, _in)) return;

                _half.Content = null;

                if (_out != null) _half.Content = Ui.Loose(_out);
            }

            private string Titled(string? key) =>
                ClashFacets.Of(key, Results(), Storeys(), Issued())?.Title ?? key ?? "nothing";

            // -------------------------------------------------------------------------------
            // Writing the packs
            // -------------------------------------------------------------------------------

            /// <summary>The results that go out: the chosen tests', through the result filter.</summary>
            private IReadOnlyList<ClashItem> Going() => Scope();

            /// <summary>Work out the packs without writing any of them.</summary>
            /// <param name="job">The run, for its progress line.</param>
            /// <param name="into">Where the detail is drawn.</param>
            private int Rehearse(Job job, Panel into)
            {
                var going = Going();

                if (going.Count == 0)
                {
                    into.Children.Add(Ui.Problem("No clash meets the filter."));
                    return 0;
                }

                var rounds = Rounds(going);

                into.Children.Add(Ui.Line(
                    Ui.Count(rounds.Count, "pack") + " covering "
                    + Ui.Count(going.Count, "clash") + ".", 1, true));

                var rows = rounds.Select(one => new TableRow(one,
                    one.Recipient,
                    one.FileStem() + ClashFile.Extension,
                    one.Selection.Items.Count.ToString(CultureInfo.InvariantCulture))).ToList();

                into.Children.Add(Ui.Table(rows,
                    ("Recipient", 0), ("File", 240), ("Clashes", 100)));

                _ = job;

                return rounds.Count;
            }

            /// <summary>The packs this profile would write, from the rows going out.</summary>
            /// <param name="going">The rows going out.</param>
            private IReadOnlyList<ExportRound> Rounds(IReadOnlyList<ClashItem> going) =>
                _profile.Rounds(going, Board(), Storeys(), Issued());

            /// <summary>Write the packs.</summary>
            /// <param name="job">The run.</param>
            private void Publish(Job job)
            {
                var going = Going();
                var rounds = Rounds(going);

                if (rounds.Count == 0) return;

                var suggested = rounds.Count == 1
                    ? rounds[0].FileStem()
                    : (_projectName);

                var path = Ui.AskToSave(
                    rounds.Count == 1
                        ? "Write the pack"
                        : "Write the first pack; the rest are written beside it",
                    ClashFile.Filter, suggested + ClashFile.Extension, ClashFile.Extension);

                if (path == null) return;

                var folder = Path.GetDirectoryName(path) ?? ".";
                var when = DateTime.UtcNow.Ticks;
                var wrote = 0;

                _sending.Children.Clear();

                foreach (var round in rounds)
                {
                    job.Say("Writing " + round.Recipient + "...");

                    var file = rounds.Count == 1
                        ? path
                        : Path.Combine(folder, round.FileStem() + ClashFile.Extension);

                    var written = ClashBcf.Write(round.Selection.Items, round.Selection.Groups,
                                                 _profile.BcfOptions(_projectName, when));

                    try
                    {
                        File.WriteAllBytes(file, written.Bytes);
                    }
                    catch (Exception e)
                    {
                        _sending.Children.Add(Ui.Problem(
                            "Could not write " + Path.GetFileName(file) + ": " + Ui.Describe(e)));

                        continue;
                    }

                    wrote++;

                    // Which clashes went out, to whom, and where each stood when it went. The
                    // per-test stamp below answers "have I sent this test" and nothing else; a
                    // weekly issue is a delta on the results, and without this a pack either
                    // repeated four hundred clashes the trade already had or left out the ones
                    // that moved.
                    Issued().Stamp(round.Selection.Items, round.Recipient, when);

                    _sending.Children.Add(Ui.Note(
                        Path.GetFileName(file) + " - " + round.Selection.Explain()));
                }

                // Which tests went out, and when, so next Friday's list says which have never
                // gone at all.
                _profile.Stamp(going.Select(one => one.TestName).Distinct(StringComparer.OrdinalIgnoreCase),
                               when);

                _store.Section(ProjectStore.ClashSection).Set(ClashIssueLog.Key, Issued().ToJson());

                Keep(quiet: true);

                _sending.Children.Add(Ui.Line(
                    Ui.Count(wrote, "pack") + " written to " + folder + ".", 1, true));
            }

            // -------------------------------------------------------------------------------
            // Importing
            // -------------------------------------------------------------------------------

            /// <summary>
            /// The same anatomy as the export half, because it is the same shape of job.
            ///
            /// Left is where the answers came from - one pack per party - and right is what they
            /// say about the board. A pack can be taken out and the rows redraw without it, which
            /// is the question a coordinator has when one trade's file turns out to be last week's.
            ///
            /// Nothing is applied by reading. The right-hand table is what would happen; the
            /// action row is what does it.
            /// </summary>
            private UIElement TakingBack()
            {
                var reading = Ui.Runner("Read the packs", _back, Receive, primary: false);

                reading.VerticalAlignment = VerticalAlignment.Bottom;

                var strip = Ui.Across(
                    Ui.Field("Half", Switch(TakeBack)),
                    Ui.Field("Returned packs", Ui.Quiet("Choose files…", Chosen)),
                    Ui.Field("Show", Showing()),
                    reading);

                var foot = new WrapPanel();

                // Off until there is something to apply. A primary that is always live on a
                // screen whose left half is empty is a press that does nothing and says nothing.
                _apply = Ui.Runner("Apply to the board", _applied, Apply);
                _apply.IsEnabled = false;
                _apply.ToolTip = "Read the returned packs first.";

                foot.Children.Add(Ui.Loose(_apply));

                DrawPacks();
                DrawReading();

                return Bench.Split(strip,
                                   Bench.Pane("Packs", _packsSaid, _files, null, scrolls: false),
                                   Bench.Pane("Returned clashes", _rowsSaid, _rows, null,
                                              scrolls: false),
                                   Ui.Stack(foot, _back, _applied),
                                   leftWidth: 400);
            }

            /// <summary>Which returned rows are listed: all of them, or only what needs a person.</summary>
            private UIElement Showing()
            {
                var box = Ui.Choice(new[] { Everything, Changing, Conflicts }, _showing, said =>
                {
                    _showing = said;
                    DrawReading();
                }, 150);

                box.ToolTip = "A pack of four hundred clashes usually holds a dozen answers. "
                              + "This is how to see only those.";

                return box;
            }

            /// <summary>Every returned row.</summary>
            private const string Everything = "Everything returned";

            /// <summary>Only the rows that would move the board.</summary>
            private const string Changing = "Only what changes";

            /// <summary>Only the rows two parties answered differently.</summary>
            private const string Conflicts = "Only the conflicts";

            /// <summary>Take several packs at once, which is how they arrive.</summary>
            private void Chosen()
            {
                var picked = Ui.AskToOpenMany("Returned packs", ClashFile.Filter);

                if (picked.Count == 0) return;

                foreach (var file in picked)
                    if (!_returns.Contains(file, StringComparer.OrdinalIgnoreCase))
                        _returns.Add(file);

                // The reading is thrown away, because it was about the packs that were chosen
                // before. Leaving it would show rows from a pack that is no longer in the list.
                _reading = null;

                DrawPacks();
                DrawReading();
            }

            /// <summary>
            /// Who a pack is from: what somebody typed, or the file's own name.
            /// </summary>
            /// <param name="file">The pack's path.</param>
            private string Party(string file) =>
                _parties.TryGetValue(file, out var said) && said.Trim().Length > 0
                    ? said.Trim()
                    : Path.GetFileNameWithoutExtension(file);

            /// <summary>Parties named by hand, by the pack's path.</summary>
            private readonly Dictionary<string, string> _parties =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>The packs chosen, with what each one turned out to hold.</summary>
            private void DrawPacks()
            {
                _files.Children.Clear();
                _packsSaid.Children.Clear();

                if (_returns.Count == 0)
                {
                    _files.Children.Add(Ui.Empty("No packs chosen.",
                        "Choose the .bcfzip files the parties returned. The party is taken from "
                        + "the file name, so Mechanical.bcfzip is read as Mechanical's response."));

                    return;
                }

                var rows = _returns.Select(one =>
                {
                    var read = _reading?.Packs.FirstOrDefault(
                        pack => string.Equals(pack.FileName, Path.GetFileName(one),
                                              StringComparison.OrdinalIgnoreCase));

                    var answered = _reading?.Rows.Count(
                        row => row.Answers.Any(say => string.Equals(
                            say.Party, Party(one),
                            StringComparison.OrdinalIgnoreCase))) ?? 0;

                    return new TableRow(one,
                        Party(one),
                        Path.GetFileName(one),
                        read == null ? string.Empty
                            : read.Results.ToString("N0", CultureInfo.InvariantCulture),
                        _reading == null ? "not read yet"
                            : answered.ToString("N0", CultureInfo.InvariantCulture))
                    {
                        Tone = read?.Read?.Problem != null ? Tone.Bad : Tone.Plain,
                    };
                }).ToList();

                var table = Ui.Table(rows,
                    ("Party", 130), ("File", 0), ("In the pack", 84), ("On this board", 92))
                    .Multi();

                Ui.SetFills(_files, true);

                _files.Children.Add(Ui.Loose(table));

                _files.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Say who these {n} are from", (picked, said) =>
                    {
                        var held = picked.OfType<string>().ToList();

                        if (held.Count == 0)
                        {
                            said.Children.Add(Ui.Note("Select at least one pack."));
                            return;
                        }

                        Parts.Ask(_files, "Who is this from", Party(held[0]), "Use this name",
                                  named => Ui.Safely(() =>
                        {
                            foreach (var one in held) _parties[one] = named;

                            // The reading carries the old names on every answer, so it is read
                            // again rather than relabelled in place.
                            _reading = null;

                            DrawPacks();
                            DrawReading();
                        }));
                    }, primary: true),
                    new Ui.OnPicked("Take out these {n}", (picked, said) =>
                    {
                        var held = picked.OfType<string>().ToList();

                        if (held.Count == 0)
                        {
                            said.Children.Add(Ui.Note("Select at least one pack."));
                            return;
                        }

                        foreach (var one in held) _returns.Remove(one);

                        said.Children.Add(Ui.Note(Ui.Count(held.Count, "pack") + " taken out."));

                        _reading = null;

                        DrawPacks();
                        DrawReading();
                    })));

                _packsSaid.Children.Add(Ui.Small(
                    _reading == null
                        ? Ui.Count(_returns.Count, "pack") + " chosen"
                        : Ui.Count(_returns.Count, "pack") + " · "
                          + Ui.Count(_reading.Rows.Count, "clash") + " returned"));
            }

            /// <summary>Read every pack chosen, and say what would happen.</summary>
            /// <param name="job">The run.</param>
            private void Receive(Job job)
            {
                var packs = new List<ExchangePack>();

                foreach (var file in _returns)
                {
                    if (!File.Exists(file))
                    {
                        _back.Children.Add(Ui.Problem("There is no file at " + file + "."));
                        return;
                    }

                    job.Say("Reading " + Path.GetFileName(file) + "...");

                    byte[] bytes;

                    try
                    {
                        bytes = File.ReadAllBytes(file);
                    }
                    catch (Exception e)
                    {
                        _back.Children.Add(Ui.Problem(
                            "Could not read " + Path.GetFileName(file) + ": " + Ui.Describe(e)));

                        return;
                    }

                    // The party is the file's own name unless somebody has said otherwise. Asking
                    // for "Mechanical" beside a file called Mechanical.bcfzip is asking them to do
                    // the tool's job; what actually arrives is "RE_ L3 clashes - responses FINAL
                    // v2 (3).bcfzip", and one from the sprinkler subcontractor called
                    // Mechanical.bcfzip.
                    packs.Add(new ExchangePack(Party(file),
                                               ClashFile.Read(bytes), Path.GetFileName(file)));
                }

                if (packs.Count == 0)
                {
                    _back.Children.Add(Ui.Empty("No packs chosen.",
                        "Choose the files the parties returned."));

                    return;
                }

                _reading = ClashExchange.Read(packs, Results());

                _back.Children.Add(Ui.Line(_reading.Explain(), 1, true));

                DrawPacks();
                DrawReading();
            }

            /// <summary>The rows that came back, and what each would do.</summary>
            private void DrawReading()
            {
                _rows.Children.Clear();
                _rowsSaid.Children.Clear();

                if (_apply != null)
                {
                    var ready = _reading != null && _reading.Rows.Count > 0;

                    _apply.IsEnabled = ready;
                    _apply.ToolTip = ready
                        ? null
                        : _reading == null
                            ? "Read the returned packs first."
                            : "These packs answer no clash on this board.";
                }

                if (_reading == null)
                {
                    _rows.Children.Add(Ui.Empty("Nothing read yet.",
                        "Choose the returned packs on the left. Where two parties answered one "
                        + "clash differently the row is marked as a conflict, and applied only "
                        + "once a side is chosen."));

                    return;
                }

                var showing = _reading.Rows
                    .Where(one => _showing == Conflicts ? one.IsDisputed
                                : _showing == Changing ? one.Changes || one.IsDisputed
                                : true)
                    .ToList();

                if (showing.Count == 0)
                {
                    _rows.Children.Add(Ui.Empty(
                        _showing == Everything
                            ? "These packs answer no clash on this board."
                            : "Nothing here to decide.",
                        _showing == Everything
                            ? "The parties may have worked against an earlier model issue."
                            : "Every returned answer either agrees with the board or has been "
                              + "decided."));

                    return;
                }

                var rows = showing.Take(2000).Select(one => new TableRow(one,
                    one.Item.Name ?? one.Item.Key.ToString(),
                    one.Item.TestName,
                    one.Current.ToString(),
                    one.IsDisputed && one.Chosen == null ? "Conflict" : one.Would.ToString(),
                    string.Join(", ", one.Answers.Select(say => say.Party)),
                    one.Take ? "Yes" : "No")
                {
                    Tone = one.IsDisputed && one.Chosen == null ? Tone.Warn
                         : one.Changes ? Tone.Good : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                    ("Clash", 140), ("Test", 130), ("On the board", 96),
                    ("Returned", 90), ("Parties", 130), ("Apply", 60)).Multi();

                Ui.SetFills(_rows, true);

                _rows.Children.Add(Ui.Loose(table));

                var settling = Ui.Link("Resolve the conflict…", () => { });

                settling.Click += (s, e) => Ui.Safely(() =>
                {
                    var picked = table.PickedMany().OfType<ExchangeRow>()
                                      .Where(one => one.IsDisputed).ToList();

                    if (picked.Count == 0)
                    {
                        Ui.Told("Select a row marked as a conflict first.");
                        return;
                    }

                    Parts.PopMenu(settling, Sides(picked));
                });

                _rows.Children.Add(Ui.Picking(table, settling,
                    new Ui.OnPicked("Accept these {n}", (picked, said) =>
                        Decide(picked, said, row =>
                        {
                            if (row.Settled != null) row.Take = true;
                        }), primary: true),
                    new Ui.OnPicked("Reject these {n}", (picked, said) =>
                        Decide(picked, said, row => row.Take = false))));

                if (_reading.Strangers.Count > 0)
                    _rows.Children.Add(Ui.Note(
                        Ui.Count(_reading.Strangers.Count, "result")
                        + " reference elements this federation does not hold, which usually "
                        + "means a party worked against an earlier model issue."));

                DrawStatusMapping();

                _rowsSaid.Children.Add(Ui.Small(
                    showing.Count.ToString("N0", CultureInfo.InvariantCulture) + " shown · "
                    + Ui.Count(_reading.Changes, "change") + " to apply"
                    + (_reading.Undecided > 0
                        ? " · " + _reading.Undecided.ToString(CultureInfo.InvariantCulture)
                          + " unresolved"
                        : string.Empty)));
            }

            /// <summary>
            /// Say what the returned status words become, before anything is applied.
            ///
            /// BCF does not fix its status list, so a trade returning forty topics marked
            /// "Rejected" is ordinary. The mapping used to be applied silently, which left no way
            /// to see that forty answers had become Active.
            /// </summary>
            private void DrawStatusMapping()
            {
                var words = _reading?.StatusWords;

                if (words == null || words.Count == 0) return;

                _rows.Children.Add(Ui.Small("Returned statuses become "
                    + string.Join(", ", words.Select(Said)) + "."));

                var guessed = _reading!.Guessed;

                if (guessed.Count == 0) return;

                _rows.Children.Add(Ui.Note(
                    "This project's vocabulary does not hold "
                    + string.Join(", ", guessed.Select(one => "\"" + one.Word + "\"")) + ". "
                    + (guessed.Count == 1 ? "It is read" : "They are read")
                    + " as shown above; change the answer on the row to override it."));
            }

            /// <summary>One status word, as the mapping line reads it.</summary>
            /// <param name="word">The word and where it lands.</param>
            private static string Said(ClashStatusWord word) =>
                (word.Word.Length == 0 ? "(no status)" : word.Word)
                + " \u00d7" + word.Count.ToString(CultureInfo.InvariantCulture)
                + " \u2192 " + word.Lands;

            /// <summary>Do something to the picked rows, then draw them again.</summary>
            /// <param name="picked">The rows picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            /// <param name="what">What to do to each.</param>
            private void Decide(IReadOnlyList<object> picked, Panel said, Action<ExchangeRow> what)
            {
                var rows = picked.OfType<ExchangeRow>().ToList();

                if (rows.Count == 0)
                {
                    said.Children.Add(Ui.Note("Select at least one row."));
                    return;
                }

                foreach (var row in rows) what(row);

                said.Children.Add(Ui.Note(Ui.Count(rows.Count, "row") + " updated."));

                DrawReading();
            }

            /// <summary>Whose answer to take, for the disputed rows picked.</summary>
            /// <param name="picked">The disputed rows.</param>
            private IReadOnlyList<Parts.Entry> Sides(IReadOnlyList<ExchangeRow> picked)
            {
                var parties = picked.SelectMany(one => one.Answers.Select(say => say.Party))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(one => one, StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading("Accept the answer from"),
                };

                foreach (var party in parties)
                {
                    var here = party;

                    items.Add(Parts.Entry.Act(here, null, () =>
                    {
                        foreach (var row in picked)
                        {
                            if (row.Answers.Any(say => string.Equals(say.Party, here,
                                                                     StringComparison.OrdinalIgnoreCase)))
                            {
                                row.Chosen = here;
                                row.Take = true;
                            }
                        }

                        DrawReading();
                    }));
                }

                items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Saying("Leave unresolved", null,
                    "No party's answer is applied", () =>
                    {
                        foreach (var row in picked) { row.Chosen = null; row.Take = false; }

                        DrawReading();
                    }));

                return items;
            }

            /// <summary>Write the taken rows onto the board.</summary>
            /// <param name="job">The run.</param>
            private void Apply(Job job)
            {
                if (_reading == null) return;

                job.Say("Applying...");

                var applied = ClashExchange.Apply(_reading, _findings?.Findings);

                // The register is written here rather than left to the Manage tab: the Manage tab
                // works on findings and never sees these items, so a status, an assignee or a
                // comment left on the board alone was gone when the tab was left.
                var kept = _findings == null || _findings.Save();

                _applied.Children.Add(Ui.Line(applied.Explain(), 1, true));

                if (applied.Moved == 0 && applied.Assigned == 0 && applied.Comments == 0) return;

                if (!kept)
                    _applied.Children.Add(Ui.Problem(
                        "This is on the board but not written down: " + _findings!.LastSaveProblem));
                else if (_findings == null)
                    _applied.Children.Add(Ui.Note(
                        "This screen was opened without a project file, so the answers are on the "
                        + "board for this session only."));
                else
                    _applied.Children.Add(Ui.Note(
                        "Written to the project database. It reaches Navisworks when the open "
                        + "model is an .nwf and the edition is Manage."));

                DrawReading();
            }

            // -------------------------------------------------------------------------------
            // Profiles
            // -------------------------------------------------------------------------------

            /// <summary>One section of the setup, in a window of its own.</summary>
            /// <param name="section">Which section the symbol names.</param>
            private void Configure(string section)
            {
                var page = section == "profile"
                    ? new Ui.Page("Export profile", () => Bench.Group("Export profile", Profiles()))
                    : new Ui.Page("Export scope", () => Bench.Group("Export scope", Statuses()));

                Ui.Configure(page.Name, Rebuild, page);
            }

            /// <summary>The two questions asked often enough to sit in the setup window.</summary>
            private UIElement Statuses()
            {
                var box = Ui.Stack();

                box.Children.Add(Ui.Eyebrow("Statuses"));

                foreach (FindingStatus status in Enum.GetValues(typeof(FindingStatus)))
                {
                    var here = status;

                    box.Children.Add(Ui.Check(here.ToString(), _profile.Statuses.Contains(here),
                        on =>
                        {
                            if (on) _profile.Statuses.Add(here);
                            else _profile.Statuses.Remove(here);
                        }));
                }

                box.Children.Add(Ui.Small("With none ticked, every status is exported."));

                box.Children.Add(Ui.Check("Keep the folder structure", _profile.KeepGroups,
                                          on => _profile.KeepGroups = on));

                return box;
            }

            private UIElement Profiles()
            {
                var names = _profiles.Select(one => one.Name).ToList();

                if (names.Count == 0) names.Add(_profile.Name);

                _named.Text = _profile.Name;

                return Ui.Stack(
                    Ui.Field("Saved", Ui.Choice(names, _profile.Name, Pick, 220)),
                    Ui.Field("Called", _named),
                    Ui.Across(
                        Ui.Quiet("Keep", () => Keep(quiet: false)),
                        Ui.Danger("Forget", Forget),
                        Ui.Quiet("Save…", ToFile),
                        Ui.Quiet("Load…", FromFile)),
                    Ui.Small("A profile holds the tests, statuses, filter, order and split, so "
                             + "a recurring issue is configured once."));
            }

            /// <summary>Switch to a saved profile.</summary>
            /// <param name="name">Which one.</param>
            private void Pick(string name)
            {
                var found = _profiles.FirstOrDefault(
                    one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

                if (found == null) return;

                _profile = found;
                Rebuild();
            }

            /// <summary>Keep this profile in the project file.</summary>
            /// <param name="quiet">True to say nothing, for the save that follows a write.</param>
            private void Keep(bool quiet)
            {
                var name = _named.Text.Trim();

                if (name.Length > 0) _profile.Name = name;

                if (!_profiles.Contains(_profile))
                {
                    _profiles.RemoveAll(one => string.Equals(one.Name, _profile.Name,
                                                             StringComparison.OrdinalIgnoreCase));

                    _profiles.Add(_profile);
                }

                Save();

                if (!quiet) Ui.Told("Saved “" + _profile.Name + "”.");
            }

            private void Forget()
            {
                _profiles.Remove(_profile);
                _profile = _profiles.FirstOrDefault() ?? new ExportProfile();

                Save();
                Rebuild();
            }

            private void ToFile()
            {
                var path = Ui.AskToSave("Save the profile", "Export profile|*" + Extension,
                                        _profile.Name + Extension, Extension);

                if (path == null) return;

                Ui.Safely(() => File.WriteAllText(path, _profile.ToJson().ToJson(indented: true)));
            }

            private void FromFile()
            {
                var path = Ui.AskToOpen("Load a profile", "Export profile|*" + Extension);

                if (path == null) return;

                Ui.Safely(() =>
                {
                    _profile = ExportProfile.FromJson(JsonReader.Parse(File.ReadAllText(path)));

                    _profiles.Add(_profile);

                    Save();
                    Rebuild();
                });
            }

            /// <summary>Read the profiles out of the project file.</summary>
            private void Load()
            {
                var held = _store.Section(ProjectStore.ProfileSection)[Key];

                foreach (var one in held.Items) _profiles.Add(ExportProfile.FromJson(one));

                _profile = _profiles.FirstOrDefault() ?? new ExportProfile();

                // Whatever the Manage tab asked to send, ticked on arrival rather than left as a
                // memory test about which three tests they were.
                if (Preferred != null && Preferred.Count > 0)
                {
                    var wanted = new HashSet<string>(Preferred, StringComparer.OrdinalIgnoreCase);

                    foreach (var test in _profile.Tests)
                        test.Chosen = wanted.Contains(test.Name);

                    Preferred = null;
                }
            }

            /// <summary>Write the profiles into the project file.</summary>
            private void Save()
            {
                _store.Section(ProjectStore.ProfileSection)
                        .Set(Key, JsonValue.Array(_profiles.Select(one => one.ToJson())));

                _store.Save();
            }
        }
    }
}
