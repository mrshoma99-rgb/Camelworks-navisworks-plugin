using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Findings;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The Exchange tab: BCF packs out, returned BCF packs back in.
    ///
    /// Two modes, one round. Export writes a pack per recipient, narrowed to the clashes that
    /// recipient is being asked about. Import reads the returned packs, says what each answer
    /// would do to the board, marks the clashes two parties answered differently, and applies
    /// what has been accepted.
    ///
    /// One tab rather than three, because the return leg has to know what went out: the packs
    /// are matched back to the board by the identity the export wrote.
    ///
    /// BCF only. It is the format Solibri, BIMcollab, Revizto, BIMTrack and the common issue
    /// trackers read, so a pack written here opens in the tool the recipient already runs.
    /// </summary>
    public static class ClashExchangeViews
    {
        /// <summary>Where the profiles live in the project file.</summary>
        public const string Key = "exportProfiles";

        /// <summary>What a profile file is called on disk.</summary>
        public const string Extension = ".cwexport";

        /// <summary>
        /// What the Manage tab asked to send: which check, and which of its runs.
        ///
        /// A pair rather than a list of run ids, because the runs mean nothing without the check
        /// that owns them: two checks can name a run the same thing, and a request that carried
        /// only the ids would tick whichever check's runs the tab happened to be showing.
        /// </summary>
        internal sealed class ExchangeRequest
        {
            /// <summary>Ask for these runs of this check.</summary>
            /// <param name="checkKey">The check, as <see cref="FindingSource"/> spells it.</param>
            /// <param name="runIds">The runs to tick, by the check's own id for each.</param>
            internal ExchangeRequest(string checkKey, IReadOnlyList<string> runIds)
            {
                CheckKey = string.IsNullOrWhiteSpace(checkKey) ? FindingSource.Clash : checkKey;
                RunIds = runIds ?? Array.Empty<string>();
            }

            /// <summary>Which check's results are being sent.</summary>
            internal string CheckKey { get; }

            /// <summary>Which of its runs, by id.</summary>
            internal IReadOnlyList<string> RunIds { get; }
        }

        /// <summary>
        /// What the clash manager asked to send, applied to the next desk once.
        ///
        /// The hand-off from the Manage tab: "export these three tests" should arrive with those
        /// three ticked, not with a memory test about which three they were.
        /// </summary>
        internal static ExchangeRequest? Preferred { get; set; }

        /// <summary>
        /// Which check this tab exchanges. The Manage tab owns the choice.
        ///
        /// Not a second picker. Two pickers that can disagree is a coordinator exporting one
        /// check's findings while reading another check's board, so this tab says which check is
        /// selected and links back to Manage to change it.
        /// </summary>
        internal static string CheckKey { get; set; } = FindingSource.Clash;

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Surface(Session session) =>
            Drawn(session.Store, session.Profile.ProjectName, findings: session.Findings,
                  session: session);

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
        /// <param name="importing">True to open on the import mode rather than the export mode.</param>
        /// <param name="findings">
        /// Where an applied answer is written to last. Null draws the screen without one, which is
        /// what the harness does; applying then reaches the live board and says so.
        /// </param>
        /// <param name="session">
        /// The open document, for the parts of a pack only a document can supply: the IFC ids a
        /// receiving tool selects by, where each element sits, and the rendered pictures. Null
        /// writes the pack without them and the tab says what that costs.
        /// </param>
        /// <param name="returned">
        /// Returned packs to open with, by path, read before the surface is built. For the design
        /// harness, which cannot press a file dialog: without it the import mode can only ever be
        /// drawn with nothing chosen.
        /// </param>
        public static UIElement Drawn(ProjectStore store, string? projectName = null,
                                      IReadOnlyList<ClashGroup>? board = null,
                                      bool importing = false, FindingStore? findings = null,
                                      Session? session = null,
                                      IReadOnlyList<string>? returned = null) =>
            new Desk(store, projectName ?? "Untitled project", board, importing, findings,
                     session, returned).View;

        /// <summary>The two modes of the tab: writing packs out.</summary>
        private const string SendOut = "Export";

        /// <summary>The two modes of the tab: reading returned packs back.</summary>
        private const string TakeBack = "Import";

        private sealed class Desk
        {
            private readonly ProjectStore _store;
            private readonly string _projectName;

            private readonly List<ExportProfile> _profiles = new List<ExportProfile>();
            private ExportProfile _profile = new ExportProfile();

            private readonly ContentControl _page = new ContentControl();

            // -------------------------------------------------------------------------------
            // The export mode
            // -------------------------------------------------------------------------------

            /// <summary>
            /// The tests table and its verbs.
            ///
            /// A dock panel rather than a stack: the pane gives its body a fixed height, and a
            /// stack hands each child the height it asks for, so a table longer than the pane was
            /// clipped at the bottom edge with no scrollbar. Docked, the verbs take their line and
            /// the table gets what is left, which is a height its own scroller can work in.
            /// </summary>
            private readonly DockPanel _tests = new DockPanel { LastChildFill = true };
            private readonly StackPanel _testsSaid = Ui.Stack();
            /// <summary>The results table, its filter bar and its verbs. Docked, as above.</summary>
            private readonly DockPanel _results = new DockPanel { LastChildFill = true };
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
            // The import mode
            // -------------------------------------------------------------------------------

            /// <summary>The packs chosen, by path, in the order they were chosen.</summary>
            private readonly List<string> _returns = new List<string>();

            /// <summary>The packs table and its verbs. Docked, as the export tables are.</summary>
            private readonly DockPanel _files = new DockPanel { LastChildFill = true };
            private readonly StackPanel _packsSaid = Ui.Stack();
            private readonly StackPanel _back = Ui.Stack();
            /// <summary>The returned rows, their verbs and what the answers become. Docked.</summary>
            private readonly DockPanel _rows = new DockPanel { LastChildFill = true };
            private readonly StackPanel _rowsSaid = Ui.Stack();
            private readonly StackPanel _applied = Ui.Stack();

            /// <summary>Which returned rows the table lists.</summary>
            private string _showing = Everything;

            private ExchangeReading? _reading;

            /// <summary>The primary of the import mode, kept so reading a pack can turn it on.</summary>
            private System.Windows.Controls.Button? _apply;

            /// <summary>Where the row filter is drawn, once there are rows for it to narrow.</summary>
            private readonly ContentControl _show = new ContentControl();

            /// <summary>Whether the import mode is drawn as two panes rather than as one statement.</summary>
            private bool _paned;

            private UIElement? _out;
            private UIElement? _in;

            private readonly IReadOnlyList<ClashGroup>? _board;

            private readonly FindingStore? _findings;

            /// <summary>The open document, or null when the desk was drawn from the project file.</summary>
            private readonly Session? _session;

            /// <summary>Whether each topic carries a rendered picture. Needs a document.</summary>
            private bool _pictures;

            internal Desk(ProjectStore store, string projectName,
                          IReadOnlyList<ClashGroup>? board = null, bool importing = false,
                          FindingStore? findings = null, Session? session = null,
                          IReadOnlyList<string>? returned = null)
            {
                _store = store;
                _projectName = projectName;
                _board = board;
                _importing = importing;
                _findings = findings;
                _session = session;

                Load();

                if (returned != null) _returns.AddRange(returned);

                // Read before the surface is built: the import mode has one shape with packs and
                // another without, and it is built from whichever is true at that moment.
                if (_returns.Count > 0)
                {
                    try
                    {
                        Receive(null);
                    }
                    catch (Exception e)
                    {
                        _back.Children.Add(Ui.Problem(
                            "These packs could not be read: " + Ui.Describe(e)));
                    }
                }

                View = Build();
            }

            /// <summary>Which mode is on show when the desk opens.</summary>
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
            /// Drawing the export mode asks for this a dozen times over — the facets, the values
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

                if (first != null) _page.Content = Ui.Loose(first);

                return _page;
            }

            /// <summary>Put the chosen mode on show, detaching the other first.</summary>
            /// <param name="mode">Which mode was pressed.</param>
            private void Mode(string mode)
            {
                // Detached before the other goes in, or the mode being left is still claimed by
                // this holder when it is handed back to it.
                var wanted = mode == TakeBack ? _in : _out;

                _page.Content = null;

                if (wanted != null) _page.Content = Ui.Loose(wanted);
            }

            /// <summary>
            /// The Export/Import switch, on its own line above the controls of the mode showing.
            ///
            /// The two are separate modes with separate controls, so the switch stands alone
            /// rather than sitting in the strip as one field among the settings.
            /// </summary>
            /// <param name="here">The mode on show.</param>
            /// <param name="aside">What belongs on the right of the same line, or null.</param>
            private UIElement Modes(string here, UIElement? aside)
            {
                var row = new DockPanel
                {
                    LastChildFill = false,
                    Margin = new Thickness(0, 0, 0, 10),
                };

                var tabs = Ui.Segmented(new[] { SendOut, TakeBack }, here, Mode);

                DockPanel.SetDock(tabs, Dock.Left);
                row.Children.Add(tabs);

                if (aside != null)
                {
                    DockPanel.SetDock(aside, Dock.Right);
                    row.Children.Add(aside);
                }

                return row;
            }

            /// <summary>
            /// Hold a pane's one card at the top of it.
            ///
            /// A pane body is a <see cref="DockPanel"/> whose last child fills, so an empty state
            /// dropped into an otherwise empty pane would be stretched down the whole of it.
            /// </summary>
            /// <param name="card">The card.</param>
            private static UIElement Topped(UIElement card)
            {
                if (card is FrameworkElement element)
                    element.VerticalAlignment = VerticalAlignment.Top;

                return card;
            }

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
            /// The strip holds two groups: what goes out, and how the packs are written.
            /// Everything set up once is behind the two buttons on the action row.
            /// </summary>
            private UIElement SendingOut()
            {
                // A wrap panel of the group's own, not Ui.Across: that sets a six-pixel margin on
                // everything it is given, which is the gutter between two fields and too little
                // between two groups.
                var groups = new WrapPanel();

                groups.Children.Add(Bench.Group("What goes out", across: true,
                                                Ui.Field("Kind", Kind()),
                                                Ui.Field("Find a test", _find)));

                groups.Children.Add(Bench.Group("How the packs are written", across: true,
                                                Ui.Field("Packs", Splitting()),
                                                Ui.Field("Order in each pack", Sorting())));

                var strip = Ui.Stack(Modes(SendOut, Setup()), groups);

                // Labelled. Two symbols beside a primary say nothing about what they open, and
                // the funnel is the same picture the results filter uses a few inches away.
                var scope = Ui.Quiet("Export scope…", () => Configure("in"));
                var saved = Ui.Quiet("Save as a profile…", () => Configure("profile"));

                scope.ToolTip = "Which statuses go out, what each topic carries, and whether "
                              + "folders travel with them.";
                saved.ToolTip = "Keep these settings under a name, so the next round is one press.";
                saved.Margin = new Thickness(6, 0, 0, 0);

                var setup = new StackPanel { Orientation = Orientation.Horizontal };

                setup.Children.Add(scope);
                setup.Children.Add(saved);

                // The write is the primary and it sits bottom right. The preview is the same run
                // without the files, so it is the secondary beside it rather than the only action
                // on the screen.
                var write = Ui.Runner("Write the packs", _sending, Publish);
                var preview = Ui.Runner("Preview the export", _sending,
                                        job => Rehearse(job, _sending), primary: false);

                // Flat against the footer's own padding: Ui.Foot replaces the runner's own top
                // gap, which otherwise set the two buttons eight pixels apart vertically.
                var foot = Ui.Foot(write, preview, setup);

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

                // The pane bodies are fields this screen redraws in place, and a pane puts the
                // body it is given inside a border of its own. Loosed on the way in, or the
                // second build hands one element to a second parent.
                return Bench.Split(strip,
                                   Bench.Pane("Tests", Ui.Loose(_testsSaid),
                                              Ui.Loose(_tests), null, scrolls: false),
                                   Bench.Pane("Results", Ui.Loose(_resultsSaid),
                                              Ui.Loose(_results), null, scrolls: false),
                                   Ui.Stack(foot, _sending),
                                   leftWidth: 460);
            }

            /// <summary>
            /// Which kind of test is being exchanged, and where it is chosen.
            ///
            /// Read-only here on purpose: see <see cref="CheckKey"/>. Drawn as the value it is
            /// rather than as a button, with the way to change it beside it.
            /// </summary>
            private UIElement Kind()
            {
                // Wrapped, not a horizontal stack. The kind and the way to change it are wider
                // than the column this field sits in, and a stack that cannot wrap ran the link
                // into the box beside it with no gap between them.
                var row = new WrapPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    MaxWidth = 170,
                };

                var named = new TextBlock
                {
                    Text = Named(CheckKey),
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body");

                var link = Ui.Link("Change on Manage",
                                   () => Ui.Told(Shell.AppWindow.Show("clash", "manage")));

                link.ToolTip = "The Manage tab holds the selected kind. This tab exchanges "
                             + "whatever it has selected, so the two cannot disagree.";
                link.VerticalAlignment = VerticalAlignment.Center;
                link.Margin = new Thickness(0, 2, 0, 0);

                // The symbol first, then the word. The same mark this kind carries in the Create
                // list and the check picker, so the three screens name the kind the same way.
                row.Children.Add(Ui.Glyph(Symbolled(CheckKey), 15, "Muted",
                                          new Thickness(0, 0, 7, 0)));
                row.Children.Add(named);
                row.Children.Add(link);

                return row;
            }

            /// <summary>
            /// The saved setup, beside the mode switch.
            ///
            /// A profile restores the whole export - tests, filter, order and split - so it sits
            /// with the page rather than in either group of settings it overwrites.
            /// </summary>
            private UIElement Setup()
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                row.Children.Add(new TextBlock
                {
                    Text = "Saved setup",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                }.Wearing("Lbl"));

                row.Children.Add(Saved());

                return row;
            }

            /// <summary>
            /// What a kind of test is called on screen.
            ///
            /// The tab holds a check key and not the check itself — the check over a document is
            /// built by the Manage tab and this desk draws without one — so the key is worded here
            /// rather than shown raw. "FallHazard" is a constant in a persisted format and not a
            /// name anybody wrote.
            /// </summary>
            /// <param name="checkKey">The check's key, as <see cref="FindingSource"/> spells it.</param>
            private static string Named(string checkKey)
            {
                switch (checkKey)
                {
                    case FindingSource.Clash: return "Clash";
                    case FindingSource.Headroom: return "Clearance";
                    case FindingSource.SetDrift: return "Set drift";
                    case FindingSource.FallHazard: return "Fall hazard";
                    case FindingSource.Ids: return "IDS";
                    default: return string.IsNullOrWhiteSpace(checkKey) ? "Clash" : checkKey;
                }
            }

            /// <summary>
            /// The symbol for a kind of test, from its check key.
            ///
            /// Resolved from the key for the same reason <see cref="Named"/> is: this desk holds a
            /// check key and not the check itself, so it cannot ask <see cref="ICheck.Symbol"/>.
            /// A key with no symbol named here draws the fallback, as an unknown key already
            /// draws its own name.
            /// </summary>
            /// <param name="checkKey">The check's key, as <see cref="FindingSource"/> spells it.</param>
            private static string Symbolled(string checkKey)
            {
                switch (checkKey)
                {
                    case FindingSource.Headroom: return Ui.CheckSymbol(CheckSymbols.Clearance);
                    case FindingSource.FallHazard: return Ui.CheckSymbol(CheckSymbols.Opening);
                    case FindingSource.SetDrift:
                    case FindingSource.Ids: return Ui.CheckSymbol(CheckSymbols.Check);
                    default: return Ui.CheckSymbol(CheckSymbols.Clash);
                }
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
                    _tests.Children.Add(Topped(Ui.Empty("No tests on the board.",
                                                        "Run them on the Manage tab first.")));

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

                // Captions read as instructions rather than as statements: with nothing picked,
                // "Include these 0" reads as a count of something.
                var verbs = Ui.Picking(table,
                    new Ui.OnPicked("Include {n} tests", (picked, said) =>
                        Included(picked, said, on: true), primary: true),
                    new Ui.OnPicked("Exclude {n} tests", (picked, said) =>
                        Included(picked, said, on: false)));

                Bench.Listed(_tests, table, verbs);

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

                // What is going, once. How many rows the table holds and how many are picked is
                // the verb bar's line, a few pixels below this one.
                //
                // The search is counted too, or the line says six tests over a table showing two.
                var found = _search.Length == 0
                    ? all.Count
                    : all.Count(one => Holds(one.Name) || Holds(one.Folder));

                _testsSaid.Children.Add(Ui.Small(
                    chosen + " of " + Ui.Count(all.Count, "test") + " included"
                    + (_search.Length == 0
                        ? string.Empty
                        : " · " + found.ToString(CultureInfo.InvariantCulture)
                          + " match the search")));
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

                var filters = Filters();

                // Docked top here, and again by Bench.Listed once there is a table: the empty
                // states below add themselves to this same panel, so the filter bar has to be on
                // it before them and cannot wait for the listing.
                DockPanel.SetDock(filters, Dock.Top);
                _results.Children.Add(filters);

                var scope = Scope();

                if (scope.Count == 0)
                {
                    _results.Children.Add(Topped(Ui.Empty("Nothing in scope.",
                        Tests().Any(one => Sending(one.Name))
                            ? "No result of the included tests meets the filter."
                            : "No test is included.")));

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

                var verbs = Ui.Picking(table,
                    new Ui.OnPicked("Leave out {n} clashes", (picked, said) =>
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
                    }));

                var dropped = _dropped.Count == 0 ? null : Ui.Across(
                    Ui.Small(Ui.Count(_dropped.Count, "clash") + " left out by hand."),
                    Ui.Link("Put them back",
                            () => Ui.Safely(() => { _dropped.Clear(); Rebuild(); })));

                Bench.Listed(_results, table, verbs, dropped, filters);

                var rounds = Rounds(scope);

                _resultsSaid.Children.Add(Ui.Small(
                    Ui.Count(scope.Count, "result") + " in " + Ui.Count(rounds.Count, "pack")));
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

                // The rest of the parameters, counted and named. "and one more" said neither how
                // many there were nor what they were about.
                var rest = ClashFacets.Every(Results(), Storeys(), Issued())
                    .Where(one => !facets.Any(shown => string.Equals(shown.Key, one.Key,
                                                                     StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (rest.Count > 0)
                {
                    var add = Ui.Link("Add a filter (" + rest.Count.ToString(CultureInfo.InvariantCulture)
                                      + " more)", () => { });

                    add.Click += (s, e) => Ui.Safely(() => Parts.PopMenu(add, Facets()));
                    add.Margin = new Thickness(4, 2, 0, 2);
                    add.VerticalAlignment = VerticalAlignment.Center;
                    add.ToolTip = "Filter on: " + string.Join(", ", rest.Take(12).Select(one => one.Title))
                                + (rest.Count > 12 ? ", and more" : string.Empty) + ".";

                    bar.Children.Add(add);
                }

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
            /// How the results going out are divided into packs.
            ///
            /// Named for the file it produces: "One pack per assigned to" writes the mechanical
            /// contractor's clashes into one pack and the structural engineer's into another.
            /// </summary>
            private UIElement Splitting()
            {
                var offered = new List<string> { Whole };

                offered.AddRange(ClashFacets.Every(Results(), Storeys(), Issued())
                    .Select(one => PerPack + Lowered(one.Title)));

                var here = _profile.SplitBy == null
                    ? Whole
                    : PerPack + Lowered(Titled(_profile.SplitBy));

                if (!offered.Contains(here)) offered.Add(here);

                var box = Ui.Choice(offered, here, said =>
                {
                    _profile.SplitBy = said == Whole ? null : Keyed(Stripped(said, PerPack));
                    Rebuild();
                }, 210);

                box.ToolTip = "One pack per value of the parameter chosen, each holding only that "
                              + "party's clashes.";

                return box;
            }

            /// <summary>Which parameter the clashes inside each pack are ordered by.</summary>
            private UIElement Sorting()
            {
                var offered = new List<string> { AsFound };

                offered.AddRange(ClashFacets.Every(Results(), Storeys(), Issued())
                    .Select(one => By + Lowered(one.Title)));

                var here = _profile.Pick.Sort == null ? AsFound : By + Lowered(Titled(_profile.Pick.Sort));

                if (!offered.Contains(here)) offered.Add(here);

                var box = Ui.Choice(offered, here, said =>
                {
                    _profile.Pick.Sort = said == AsFound ? null : Keyed(Stripped(said, By));
                    Rebuild();
                }, 190);

                box.ToolTip = "The order the clashes are written in, which is not the order the "
                              + "table happens to be sorted in.";

                // Only where there is an order to reverse. Left in place it was a symbol that was
                // permanently unavailable on the setting the screen opens on.
                if (_profile.Pick.Sort == null) return box;

                var way = Ui.Icon(_profile.Pick.Descending ? Glyphs.Down : Glyphs.Up,
                                  _profile.Pick.Descending ? "Descending" : "Ascending",
                                  () => Ui.Safely(() =>
                                  {
                                      _profile.Pick.Descending = !_profile.Pick.Descending;
                                      Rebuild();
                                  }));

                return Ui.Across(box, way);
            }

            /// <summary>What the split chooser calls writing everything into one pack.</summary>
            private const string Whole = "One pack for everything";

            /// <summary>What the split chooser puts before a parameter's name.</summary>
            private const string PerPack = "One pack per ";

            /// <summary>What the order chooser calls the order the results came off the board in.</summary>
            private const string AsFound = "As on the board";

            /// <summary>What the order chooser puts before a parameter's name.</summary>
            private const string By = "By ";

            /// <summary>A parameter's title, as it reads inside a phrase.</summary>
            /// <param name="title">The title.</param>
            private static string Lowered(string title) =>
                string.IsNullOrEmpty(title)
                    ? title
                    : char.ToLowerInvariant(title[0]) + title.Substring(1);

            /// <summary>The parameter's title out of a chooser's phrase.</summary>
            /// <param name="said">What the chooser holds.</param>
            /// <param name="prefix">The phrase's leading words.</param>
            private static string Stripped(string said, string prefix) =>
                said.StartsWith(prefix, StringComparison.Ordinal)
                    ? said.Substring(prefix.Length)
                    : said;

            /// <summary>The saved key behind a parameter's on-screen title.</summary>
            /// <param name="title">What it is called on screen.</param>
            private string? Keyed(string? title) =>
                ClashFacets.Every(Results(), Storeys(), Issued())
                    .FirstOrDefault(one => string.Equals(one.Title, title,
                                                         StringComparison.OrdinalIgnoreCase))?.Key;

            /// <summary>Draw the export mode again, for the controls captioned by their own value.</summary>
            private void Rebuild()
            {
                _out = SendingOut();

                if (ReferenceEquals(_page.Content, _in)) return;

                _page.Content = null;

                if (_out != null) _page.Content = Ui.Loose(_out);
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
            private void Rehearse(Job job, Panel into)
            {
                var going = Going();

                if (going.Count == 0)
                {
                    into.Children.Add(Ui.Problem("No clash meets the filter."));
                    return;
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

                // Which split was used, when it is not the one that was asked for. A profile that
                // splits by discipline over a check with no trade sides would otherwise produce
                // one unexplained pack.
                var fell = rounds.FirstOrDefault(one => one.Asked != null);

                if (fell != null)
                    into.Children.Add(Ui.Note(
                        "Nothing here carries " + fell.Asked + ", which is what this profile "
                        + "splits on, so "
                        + (fell.SplitBy == ExportProfile.OnePack
                            ? "everything went in one pack: nothing here is assigned either."
                            : "the split fell back to " + fell.SplitBy + ".")));

                into.Children.Add(Ui.Small(Framing(going.Count)));

                _ = job;
            }

            /// <summary>
            /// What each topic will carry, and what that will cost.
            ///
            /// Said before the press rather than discovered as a tab that appears to hang, and
            /// said from what this desk actually has. Without an open document a pack carries its
            /// camera and its facts and nothing else: no IFC ids to select by, no route to each
            /// element, no pictures.
            /// </summary>
            /// <param name="results">How many results are going out.</param>
            private string Framing(int results)
            {
                if (_session == null)
                    return "No model is open, so these topics go out with a camera and the facts "
                         + "the register holds: no IFC ids for the receiving tool to select by, "
                         + "no route to the elements for a reviewing seat, and no pictures.";

                if (!_pictures)
                    return "Each topic carries its elements' IFC ids and a camera on the finding. "
                         + "Pictures are off.";

                if (CheckKey == FindingSource.Clash)
                    return "Pictures are framed by the clash engine where the result still has "
                         + "the host ids it was read under, the way the native clash window "
                         + "frames it, and by a camera otherwise. Each topic carries its "
                         + "elements' IFC ids.";

                // A camera move and a capture each, which measures at about a fifth of a second
                // on a federation of any size. Rounded up to the minute, because a number to the
                // second reads as a promise.
                var minutes = Math.Max(1, (int)Math.Ceiling(results * 0.2 / 60.0));

                return "This check has no result geometry of its own, so each picture is framed "
                     + "by backing a camera off the finding's elements. That is slower and frames "
                     + "worse than a clash: allow about "
                     + Ui.Count(minutes, "minute") + " for the pictures.";
            }

            /// <summary>
            /// Which host test and result a finding was read from, when it carries both.
            ///
            /// The clash engine can draw a result the way the native clash window does, and it
            /// needs the host's two ids to do it. A finding read from a file or produced by any
            /// other check carries neither, and the camera route draws that one.
            /// </summary>
            /// <param name="finding">The finding going out.</param>
            private static (string Test, string Result)? Drawn(Finding finding) =>
                finding.Facts.TryGetValue(ClashFindings.HostTest, out var test)
                && !string.IsNullOrEmpty(test)
                && finding.Facts.TryGetValue(ClashFindings.HostId, out var result)
                && !string.IsNullOrEmpty(result)
                    ? (test!, result!)
                    : ((string Test, string Result)?)null;

            /// <summary>
            /// The findings behind the rows going out, for a read that is made once for the run.
            /// </summary>
            /// <param name="going">The rows going out.</param>
            private IReadOnlyList<Finding> Findings(IReadOnlyList<ClashItem> going)
            {
                var register = _findings?.Findings;
                var found = new List<Finding>();

                if (register == null) return found;

                foreach (var item in going)
                {
                    var finding = item?.FindingId == null ? null : register.Find(item.FindingId);

                    if (finding != null) found.Add(finding);
                }

                return found;
            }

            /// <summary>The packs this profile would write, from the rows going out.</summary>
            /// <param name="going">The rows going out.</param>
            private IReadOnlyList<ExportRound> Rounds(IReadOnlyList<ClashItem> going) =>
                _profile.Rounds(going, Board(), Storeys(), Issued());

            /// <summary>
            /// One round's findings, and the pack's own folders and place words.
            ///
            /// The board is a projection of the register, so what goes in the file is the finding
            /// behind each row rather than the row: a pack written from the projection carries no
            /// facts, no locations and no identity the return leg can match on.
            ///
            /// What the board derives per document rather than the producer writing it — the level
            /// a height falls on, the grid it is nearest, the zone it is in, and the property
            /// columns read off the clashing elements — travels as facts of this pack. Handed to
            /// the writer rather than written onto the finding: a finding belongs to the register,
            /// and an export that edited it changed rows for findings it did not send. The pack's
            /// folder goes the same way, because a board folder is made by the grouping rules and
            /// is not the group the host filed the result under.
            /// </summary>
            /// <param name="round">The round.</param>
            /// <param name="how">The writer's settings, which this fills the folder and fact maps into.</param>
            private IReadOnlyList<Finding> Pack(ExportRound round, FindingBcfOptions how)
            {
                var register = _findings?.Findings;
                var folders = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var group in round.Selection.Groups)
                    foreach (var member in group.Items)
                        if (member?.FindingId != null) folders[member.FindingId] = group.Name;

                var pack = new List<Finding>();
                var said = new Dictionary<string, IReadOnlyDictionary<string, string?>>(
                    StringComparer.Ordinal);

                foreach (var item in round.Selection.Items)
                {
                    var finding = item?.FindingId == null || register == null
                        ? null
                        : register.Find(item.FindingId);

                    if (finding == null) continue;

                    said[finding.Id] = Derived(item!);
                    pack.Add(finding);
                }

                how.GroupOf = finding =>
                    folders.TryGetValue(finding.Id, out var named) ? named : null;

                how.FactsOf = finding =>
                    said.TryGetValue(finding.Id, out var facts) ? facts : null;

                return pack;
            }

            /// <summary>
            /// What the board worked out about a result and the register does not hold.
            ///
            /// The three place words travel as labels by the declared fact-to-label mapping; the
            /// property columns travel in the sidecar, which is where a board reading the pack
            /// looks for them. An empty value removes the finding's own fact for this pack, so a
            /// board that derived no level does not send last week's.
            /// </summary>
            /// <param name="item">The row going out.</param>
            private static IReadOnlyDictionary<string, string?> Derived(ClashItem item)
            {
                var facts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Level"] = item.Level ?? string.Empty,
                    ["Grid"] = item.Grid ?? string.Empty,
                    ["Zone"] = item.Zone ?? string.Empty,
                };

                foreach (var pair in item.Properties)
                {
                    // The board draws these two off the producer's own facts. Sending them again
                    // under the board's column names would put the same value in the topic's
                    // description twice.
                    if (pair.Key == ClashItem.HostStatusProperty
                        || pair.Key == ClashItem.HostAssigneeProperty)
                        continue;

                    if (!facts.ContainsKey(pair.Key)) facts[pair.Key] = pair.Value;
                }

                return facts;
            }

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

                // The GlobalIds a receiving tool selects by and the route to each element, read
                // once for everything going out rather than once per pack: the first read after a
                // model change pays for the federation index, and a split by assignee writes
                // thirty packs off the same elements.
                var ids = new ClashIdentity.Ids(null, Findings(going), null);

                if (_session != null)
                {
                    job.Say("Reading IFC ids for the export...");
                    ids = new ClashIdentity.Ids(_session, Findings(going), job);
                }

                foreach (var round in rounds)
                {
                    job.Say("Writing " + round.Recipient + "...");

                    var file = rounds.Count == 1
                        ? path
                        : Path.Combine(folder, round.FileStem() + ClashFile.Extension);

                    var how = _profile.BcfOptions(_projectName, when);

                    how.IfcGuidOf = ids.Of;
                    how.LocatorOf = ids.At;
                    how.Author = Environment.UserName ?? "CamelWorks";

                    // The check as well as the session, so a kind that draws its own picture is
                    // asked first: a fall hazard's picture is the floor plan the run rasterised,
                    // and a photograph of the model at that point is not the same thing.
                    if (_pictures && _session != null)
                        how.Snapshot = ClashIdentity.Pictures(_session, 1280, 720, Drawn, job,
                                                              _session.Checks.Find(CheckKey));

                    var pack = Pack(round, how);

                    if (pack.Count == 0)
                    {
                        _sending.Children.Add(Ui.Problem(
                            round.Recipient + "'s pack holds no result the register knows, so "
                            + "nothing was written for them."));

                        continue;
                    }

                    var written = FindingBcf.Write(pack, null, how);

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
                    // per-test stamp below answers "have I sent this test" and nothing else.
                    //
                    // A round's name is only a party when the profile splits by one. Unsplit, it
                    // is the profile's own name, and stamping that put "Weekly MEP" under the
                    // facet titled "Last sent to".
                    var to = string.IsNullOrWhiteSpace(_profile.SplitBy) ? null : round.Recipient;

                    Issued().Stamp(round.Selection.Items, to, when);

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
            /// The same anatomy as the export mode once there are packs to work on.
            ///
            /// Left is where the answers came from - one pack per party - and right is what they
            /// say about the board. A pack can be taken out and the rows redraw without it, which
            /// is the question a coordinator has when one trade's file turns out to be last week's.
            ///
            /// With nothing chosen there is nothing for two panes to hold, so the mode opens as
            /// one statement and the action that ends it.
            ///
            /// Nothing is applied by reading. The right-hand table is what would happen; the
            /// footer action is what does it.
            /// </summary>
            private UIElement TakingBack()
            {
                // The primary of the mode, bottom right and always drawn as a button. Off until
                // there is something to apply, and drawn at the secondary weight while it is off:
                // the primary style paints no plate and no border when it is disabled.
                var applying = Ui.RunParts("Apply to the board", _applied, Apply);

                _apply = applying.Buttons.Children.OfType<System.Windows.Controls.Button>()
                                 .FirstOrDefault();

                if (_returns.Count == 0)
                {
                    _paned = false;

                    DrawReading();

                    var alone = Ui.Stack(
                        Modes(TakeBack, null),
                        Ui.Empty("No packs chosen.",
                                 "Choose the .bcfzip files the parties returned. Each pack is "
                                 + "read as it is chosen.",
                                 "Choose returned packs…",
                                 () => Ui.Safely(() => { if (Choosing()) Receive(null); })),
                        _back);

                    return Bench.Surface(null, Ui.Stack(Ui.Foot(Ui.Loose(applying.Buttons)),
                                                        applying.Progress, _applied), alone);
                }

                _paned = true;

                // One press: the packs are read as they are chosen. Choosing and then reading
                // were two buttons whose order had to be learned.
                var choose = Ui.Runner("Choose more packs…", _back,
                                       job => { if (Choosing()) Receive(job); }, primary: false);

                choose.Margin = new Thickness(0);

                var strip = Ui.Stack(
                    Modes(TakeBack, null),
                    Ui.Across(choose, _show));

                // One footer anatomy: the primary hard right, the reason it cannot be pressed to
                // its left. The reason is read off the button's tooltip, which DrawReading sets.
                var foot = Ui.Foot(Ui.Loose(applying.Buttons));

                DrawPacks();
                DrawReading();

                return Bench.Split(strip,
                                   Bench.Pane("Packs", Ui.Loose(_packsSaid),
                                              Ui.Loose(_files), null, scrolls: false),
                                   Bench.Pane("Returned clashes", Ui.Loose(_rowsSaid),
                                              Ui.Loose(_rows), null, scrolls: false),
                                   Ui.Stack(foot, applying.Progress, _back, _applied),
                                   leftWidth: 520);
            }

            /// <summary>Draw the import mode again, for the states it has different shapes in.</summary>
            private void Remade()
            {
                _in = TakingBack();

                if (ReferenceEquals(_page.Content, _out)) return;

                _page.Content = null;

                if (_in != null) _page.Content = Ui.Loose(_in);
            }

            /// <summary>
            /// Which returned rows are listed: all of them, or only what needs a person.
            ///
            /// Drawn only once there are rows to narrow. A filter over an empty list is a control
            /// that cannot do anything.
            /// </summary>
            private void DrawShowing()
            {
                _show.Content = null;

                if (_reading == null || _reading.Rows.Count == 0) return;

                var box = Ui.Choice(new[] { Everything, Changing, Conflicts }, _showing, said =>
                {
                    _showing = said;
                    DrawReading();
                }, 170);

                box.ToolTip = "A pack of four hundred clashes usually holds a dozen answers. "
                              + "This is how to see only those.";

                _show.Content = Ui.Field("Show", box);
            }

            /// <summary>Every returned row.</summary>
            private const string Everything = "Everything returned";

            /// <summary>Only the rows that would move the board.</summary>
            private const string Changing = "Only what changes";

            /// <summary>Only the rows two parties answered differently.</summary>
            private const string Conflicts = "Only the conflicts";

            /// <summary>Take several packs at once, which is how they arrive.</summary>
            /// <returns>True when the list of packs changed and the packs need reading.</returns>
            private bool Choosing()
            {
                var picked = Ui.AskToOpenMany("Returned packs", ClashFile.Filter);

                if (picked.Count == 0) return false;

                foreach (var file in picked)
                    if (!_returns.Contains(file, StringComparer.OrdinalIgnoreCase))
                        _returns.Add(file);

                // The reading is thrown away, because it was about the packs that were chosen
                // before. Leaving it would show rows from a pack that is no longer in the list.
                // What was decided on those rows is kept for the next read.
                Keeping();

                _reading = null;

                // The mode has two shapes - nothing chosen, and packs to work on - so the first
                // choice rebuilds it rather than filling panes that are not on screen.
                if (!_paned) Remade();

                return true;
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

                // Nothing chosen is drawn as one statement by the mode itself, not as an empty
                // pane in a two-pane layout.
                if (_returns.Count == 0) return;

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

                // The file name takes what is left and the three others ask for what their own
                // values need. The counts used to ask for the width of their headings, which on a
                // pane this wide left the file name — the one column somebody reads to tell two
                // packs apart — showing three characters and an ellipsis.
                var table = Ui.Table(rows,
                    ("Party", 110), ("File", 0), ("In pack", 62), ("On board", 70))
                    .Multi();

                Ui.SetFills(_files, true);

                var verbs = Ui.Picking(table,
                    new Ui.OnPicked("Name the party on {n} packs", (picked, said) =>
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
                            // again rather than relabelled in place. What was decided on those
                            // rows is kept and put back on the next read: correcting a pack's
                            // party is what somebody does after working through the disputes, and
                            // dropping the reading dropped every one of those answers with it.
                            Keeping();

                            _reading = null;

                            // Read again under the new name. What has been decided is put back.
                            Receive(null);
                        }));
                    }, primary: true),
                    new Ui.OnPicked("Take out {n} packs", (picked, said) =>
                    {
                        var held = picked.OfType<string>().ToList();

                        if (held.Count == 0)
                        {
                            said.Children.Add(Ui.Note("Select at least one pack."));
                            return;
                        }

                        foreach (var one in held) _returns.Remove(one);

                        Keeping();

                        said.Children.Add(Ui.Note(Ui.Count(held.Count, "pack") + " taken out."));

                        _reading = null;

                        // Back to the one-statement shape when the last pack goes; otherwise the
                        // rest are read again without it.
                        if (_returns.Count == 0) Remade();
                        else Receive(null);
                    }));

                Bench.Listed(_files, table, verbs);

                _packsSaid.Children.Add(Ui.Small(
                    _reading == null
                        ? Ui.Count(_returns.Count, "pack") + " chosen"
                        : Ui.Count(_returns.Count, "pack") + " · "
                          + Ui.Count(_reading.Rows.Count, "clash") + " returned"));
            }

            /// <summary>Read every pack chosen, and say what would happen.</summary>
            /// <param name="job">The run, or null when the read follows an edit to the list.</param>
            private void Receive(Job? job)
            {
                var packs = new List<ExchangePack>();

                // The runner clears its own output before the work; a read started by anything
                // else has to clear it here, or two reads' messages stack up.
                if (job == null) _back.Children.Clear();

                foreach (var file in _returns)
                {
                    if (!File.Exists(file))
                    {
                        _back.Children.Add(Ui.Problem("There is no file at " + file + "."));
                        return;
                    }

                    job?.Say("Reading " + Path.GetFileName(file) + "...");

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

                    // The party is the file's own name unless somebody has said otherwise. A
                    // returned file is often named after the thread it came from rather than the
                    // trade that sent it, so the name is a default and not a fact.
                    packs.Add(new ExchangePack(Party(file),
                                               ClashFile.Read(bytes), Path.GetFileName(file)));
                }

                if (packs.Count == 0) return;

                _reading = ClashExchange.Read(packs, Results());

                Restore();

                _back.Children.Add(Ui.Line(_reading.Explain(), 1, true));

                DrawPacks();
                DrawReading();
            }

            /// <summary>
            /// What has been decided on the rows, keyed by clash, so a re-read can put it back.
            ///
            /// Kept across a re-read rather than on the rows, because the rows are rebuilt from
            /// the packs and the decisions are the coordinator's, not the packs'.
            /// </summary>
            private readonly Dictionary<ClashKey, (string? Chosen, string? Note, bool Take)>
                _decided = new Dictionary<ClashKey, (string?, string?, bool)>();

            /// <summary>Take a copy of every decision on the reading.</summary>
            private void Keeping()
            {
                if (_reading == null) return;

                foreach (var row in _reading.Rows)
                {
                    if (row.Chosen == null && string.IsNullOrWhiteSpace(row.Note) && !row.Take)
                        continue;

                    _decided[row.Item.Key] = (row.Chosen, row.Note, row.Take);
                }
            }

            /// <summary>Put the kept decisions back onto a reading that has just been built.</summary>
            private void Restore()
            {
                if (_reading == null || _decided.Count == 0) return;

                foreach (var row in _reading.Rows)
                {
                    if (!_decided.TryGetValue(row.Item.Key, out var had)) continue;

                    // The chosen party is only put back when a pack still answers under that
                    // name. After a rename it does not, and forcing it would settle the row on a
                    // party that is no longer in the reading.
                    if (had.Chosen != null
                        && row.Answers.Any(say => string.Equals(say.Party, had.Chosen,
                                                                StringComparison.OrdinalIgnoreCase)))
                        row.Chosen = had.Chosen;

                    row.Note = had.Note;
                    row.Take = had.Take && row.Settled != null;
                }
            }

            /// <summary>The rows that came back, and what each would do.</summary>
            private void DrawReading()
            {
                _rows.Children.Clear();
                _rowsSaid.Children.Clear();

                DrawShowing();

                if (_apply != null)
                {
                    var ready = _reading != null && _reading.Rows.Count > 0;

                    _apply.IsEnabled = ready;

                    _apply.ToolTip = ready
                        ? null
                        : _reading == null
                            ? "Choose the returned packs first."
                            : "These packs answer no clash on this board.";
                }

                if (_reading == null)
                {
                    _rows.Children.Add(Topped(Ui.Empty("Not read yet.",
                        "Choose the returned packs on the left.")));

                    return;
                }

                var showing = _reading.Rows
                    .Where(one => _showing == Conflicts ? one.IsDisputed
                                : _showing == Changing ? one.Changes || one.IsDisputed
                                : true)
                    .ToList();

                if (showing.Count == 0)
                {
                    _rows.Children.Add(Topped(Ui.Empty(
                        _showing == Everything
                            ? "These packs answer no clash on this board."
                            : "Nothing here to decide.",
                        _showing == Everything
                            ? "The parties may have worked against an earlier model issue."
                            : "Every returned answer either agrees with the board or has been "
                              + "decided.",
                        _showing == Everything ? null : "Show everything returned",
                        _showing == Everything
                            ? (Action?)null
                            : () => Ui.Safely(() => { _showing = Everything; DrawReading(); }))));

                    return;
                }

                var rows = showing.Take(2000).Select(one => new TableRow(one,
                    one.Item.Name ?? one.Item.Key.ToString(),
                    one.Item.TestName,
                    one.Current.ToString(),
                    one.IsDisputed && one.Chosen == null
                        ? (one.IsThreeWay
                            ? Ui.Count(one.Parties, "way") + " conflict"
                            : "Conflict")
                        : one.Would.ToString(),
                    string.Join(", ", one.Answers.Select(say => say.Party))
                    + (string.IsNullOrWhiteSpace(one.Note) ? string.Empty : " · noted"),
                    one.Take ? "Yes" : "No")
                {
                    Tone = one.IsDisputed && one.Chosen == null ? Tone.Warn
                         : one.Changes ? Tone.Good : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                    ("Clash", 140), ("Test", 130), ("On the board", 96),
                    ("Returned", 90), ("Parties", 130), ("Apply", 60)).Multi();

                Ui.SetFills(_rows, true);

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

                var noting = Ui.Link("Say why…", () => { });

                noting.Click += (s, e) => Ui.Safely(() =>
                {
                    var picked = table.PickedMany().OfType<ExchangeRow>().ToList();

                    if (picked.Count == 0)
                    {
                        Ui.Told("Select the rows the note is about first.");
                        return;
                    }

                    // What the coordinator decided, in their own words. The note travels onto
                    // the finding with the answer.
                    //
                    // One note goes onto every picked row, so the prompt says when that replaces
                    // notes already typed on the others rather than seeding from the first row
                    // and overwriting the rest in silence.
                    var written = picked
                        .Select(one => (one.Note ?? string.Empty).Trim())
                        .Where(one => one.Length > 0)
                        .Distinct(StringComparer.Ordinal)
                        .Count();

                    var asking = written > 1
                        ? "What was decided - replaces " + Ui.Count(written, "different note")
                          + " already on these rows"
                        : "What was decided";

                    Parts.Ask(_rows, asking, picked[0].Note, "Keep this note",
                              said => Ui.Safely(() =>
                              {
                                  foreach (var row in picked) row.Note = said;

                                  DrawReading();
                              }));
                });

                var says = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                // Two links with no chrome between them read as one phrase, so the pair is ruled
                // apart rather than left to run together.
                says.Children.Add(settling);
                says.Children.Add(Ui.Vrule());
                says.Children.Add(noting);

                var verbs = Ui.Picking(table, says,
                    new Ui.OnPicked("Accept {n} answers", (picked, said) =>
                        Decide(picked, said, row =>
                        {
                            if (row.Settled != null) row.Take = true;
                        }), primary: true),
                    new Ui.OnPicked("Reject {n} answers", (picked, said) =>
                        Decide(picked, said, row => row.Take = false)));

                var under = Ui.Stack();

                if (_reading.Strangers.Count > 0)
                    under.Children.Add(Ui.Note(
                        Ui.Count(_reading.Strangers.Count, "result")
                        + " reference elements this federation does not hold, which usually "
                        + "means a party worked against an earlier model issue."));

                DrawStatusMapping(under);

                Bench.Listed(_rows, table, verbs, under);

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
            /// <param name="into">Where the two lines are drawn.</param>
            private void DrawStatusMapping(Panel into)
            {
                var words = _reading?.StatusWords;

                if (words == null || words.Count == 0) return;

                into.Children.Add(Ui.Small("Returned statuses become "
                    + string.Join(", ", words.Select(Said)) + "."));

                var guessed = _reading!.Guessed;

                if (guessed.Count == 0) return;

                into.Children.Add(Ui.Note(
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

                    // What this party actually said, on the entry that takes their answer. The
                    // menu listed names alone, so the choice was between three labels with the
                    // answers behind them on a table the menu covers.
                    var said = picked
                        .SelectMany(one => one.Answers)
                        .Where(say => string.Equals(say.Party, here,
                                                    StringComparison.OrdinalIgnoreCase))
                        .Select(say => say.Incoming.ToString()
                                       + (say.AssignedTo == null
                                          ? string.Empty : ", to " + say.AssignedTo))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    var says = said.Count == 1 ? said[0]
                             : said.Count > 1 ? Ui.Count(said.Count, "different answer")
                             : "Their answer";

                    items.Add(Parts.Entry.Saying(here, null, says, () =>
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
                // The group carries no label: the window is already titled with the section's
                // name, and the group's own label is drawn upper-cased.
                var page = section == "profile"
                    ? new Ui.Page("Export profile", () => Bench.Group(string.Empty, Profiles()))
                    : new Ui.Page("Export scope", () => Bench.Group(string.Empty, Statuses()));

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

                var head = Ui.Eyebrow("What each topic carries");

                head.Margin = new Thickness(0, 14, 0, 4);

                box.Children.Add(head);

                // What a topic holds beyond its title, said where the settings that decide it
                // are rather than in the results pane's header.
                box.Children.Add(Ui.Small(
                    "Every clash goes out as a topic with a viewpoint on the clash point."));

                // Said in words rather than as the enum's own names. "North" alone is read as
                // both "looking north" and "the north elevation", which are opposite cameras.
                var facing = Ui.Choice(
                    ((BcfView[])Enum.GetValues(typeof(BcfView))).Select(Facing), Facing(_profile.View),
                    said =>
                    {
                        foreach (BcfView view in Enum.GetValues(typeof(BcfView)))
                            if (string.Equals(said, Facing(view), StringComparison.Ordinal))
                                _profile.View = view;
                    }, 190);

                facing.ToolTip = "Where the camera stands. A reviewer works through a pack faster "
                                 + "when every topic faces the same way.";

                box.Children.Add(Ui.Field("Camera", facing));

                box.Children.Add(Ui.Check("Colour the two clashing elements", _profile.Highlight,
                                          on => _profile.Highlight = on));

                var pictures = Ui.Check("Draw a picture of each finding", _pictures,
                                        on => _pictures = on);

                // Off and unavailable without a document: a picture is a render, and this desk
                // draws from the project file alone when nothing is open.
                if (_session == null)
                {
                    _pictures = false;
                    pictures.IsEnabled = false;
                    pictures.ToolTip = "No model is open, so there is nothing to render.";
                }
                else
                {
                    pictures.ToolTip = "Every coordination tool shows its issue list as "
                                     + "thumbnails. They cost a render each.";
                }

                box.Children.Add(pictures);

                box.Children.Add(Ui.Check("Cut a section box around the clash", _profile.Section,
                                          on => _profile.Section = on));

                var reach = Ui.Text(
                    _profile.SectionMetres.ToString("0.##", CultureInfo.InvariantCulture),
                    null, 70);

                reach.TextChanged += (s, e) => Ui.Safely(() =>
                {
                    // A reach that is not a positive number left the profile on its previous value
                    // while the box on screen read 0, so the pack came back sectioned at 2 m.
                    var said = double.TryParse(reach.Text, NumberStyles.Any,
                                               CultureInfo.InvariantCulture, out var metres);

                    if (said && metres > 0)
                    {
                        _profile.SectionMetres = metres;
                        reach.ToolTip = null;
                        return;
                    }

                    reach.ToolTip = "The box reaches a positive number of metres each way from "
                                    + "the clash. Still cutting at "
                                    + _profile.SectionMetres.ToString("0.##",
                                                                     CultureInfo.InvariantCulture)
                                    + " m.";
                });

                box.Children.Add(Ui.Field("How far the box reaches (m)", reach));

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


            /// <summary>
            /// Where a camera stands, in words.
            ///
            /// The enum's own names are compass points, and a compass point on its own reads as
            /// both the place and the direction.
            /// </summary>
            /// <param name="view">The camera position.</param>
            private static string Facing(BcfView view) => view switch
            {
                BcfView.Plan => "Above, looking down",
                BcfView.North => "From the north",
                BcfView.South => "From the south",
                BcfView.East => "From the east",
                BcfView.West => "From the west",
                _ => "Standing, off to one side",
            };

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
                if (Preferred != null)
                {
                    CheckKey = Preferred.CheckKey;

                    if (Preferred.RunIds.Count > 0)
                    {
                        var wanted = new HashSet<string>(Preferred.RunIds,
                                                         StringComparer.OrdinalIgnoreCase);

                        // By the run's id first. The hand-off has always sent ids and this
                        // matched them against the test names, so a host that ids its tests by
                        // anything but their name arrived with nothing ticked.
                        foreach (var test in _profile.Tests)
                            test.Chosen = wanted.Contains(test.Id ?? test.Name);
                    }

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
