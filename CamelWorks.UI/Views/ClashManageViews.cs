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
using CamelWorks.Core.Store;
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

        /// <summary>
        /// The finding behind each row that was read from the model, by test and result id.
        ///
        /// <b>An index, not a store.</b> The findings themselves live beside the project and are
        /// the record of what somebody decided - the status a published NWD refused, the priority
        /// a reviewer's tool set that the host has no field for, the trail. This is only the way
        /// back from a row on screen to the finding it is a view of, rebuilt on every read because
        /// the host reissues its result ids whenever it feels like it.
        /// </summary>
        private static Dictionary<string, Finding>? _byHost;

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
            _byHost = null;

            _columns.Clear();
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

            /// <summary>
            /// What the test's two sides and its tolerance are, read once with the results.
            ///
            /// Once, because the rail asks every test what it is between on every keystroke in
            /// its search box, and <c>Sides</c> is a host call that walks the test's selection.
            /// Reading it per redraw is a search box that stutters on a job with forty tests.
            /// </summary>
            internal ClashTestSides? Shape { get; set; }

            internal List<Row> Rows { get; } = new List<Row>();

            internal int Of(string status) =>
                Rows.Count(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));

            /// <summary>Take a new name without re-reading anything.</summary>
            /// <param name="named">The test's new name.</param>
            internal void Rename(string named) =>
                Test = new ClashTestInfo(Test.Id, named, Test.Folder, Test.ResultCount,
                                         Test.LastRunTicks);

            /// <summary>Put this test in a folder, or take it to the root.</summary>
            /// <param name="folder">The folder, or null for the root.</param>
            internal void Refile(string? folder) =>
                Test = new ClashTestInfo(Test.Id, Test.Name, folder, Test.ResultCount,
                                         Test.LastRunTicks);
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

            private string _status = string.Empty;
            private string _assigned = string.Empty;
            private string _group = string.Empty;
            private string _lastComment = string.Empty;

            internal string TestId { get; }
            internal string TestName { get; set; }
            internal ClashResultInfo Info { get; }

            // Each of these is something the board searches on, so each throws the search blob
            // away as it is written. Done here rather than at the ten places that write them,
            // because a blob somebody forgot to clear is a row that cannot be found by the value
            // it is now showing.
            internal string Status
            {
                get => _status;
                set { _status = value; Blob = null; }
            }

            internal string Assigned
            {
                get => _assigned;
                set { _assigned = value; Blob = null; }
            }

            internal string Group
            {
                get => _group;
                set { _group = value; Blob = null; }
            }

            internal int Comments { get; set; }

            internal string LastComment
            {
                get => _lastComment;
                set { _lastComment = value; Blob = null; }
            }

            /// <summary>Write one custom column's value, and forget the search blob.</summary>
            /// <param name="columnId">Which column.</param>
            /// <param name="value">What it read.</param>
            internal void SetCustom(string columnId, string value)
            {
                Custom[columnId] = value;
                Blob = null;
            }

            /// <summary>The file's own item, kept so a markup save writes back what was read.</summary>
            internal ClashItem? FileItem { get; set; }

            /// <summary>The authored comment trail, held in full only for file rows.</summary>
            internal List<ClashComment> Trail { get; } = new List<ClashComment>();

            /// <summary>
            /// What a reviewer set that the host has no field for, or null.
            ///
            /// Laid over the row from the project file rather than read from the document, which
            /// is the only way a board can show a due date the host has nowhere to keep.
            /// </summary>
            internal ClashAside? Review { get; set; }

            /// <summary>
            /// Everything the board's search looks at, as one string, or null until it is wanted.
            ///
            /// Thrown away by anything that changes what it holds, so it can never answer for a
            /// row as it used to be.
            /// </summary>
            internal string? Blob { get; set; }

            /// <summary>Custom column values, by column id, once "Read the values" has run.</summary>
            internal Dictionary<string, string> Custom { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // -----------------------------------------------------------------------------------

        private sealed class Screen
        {
            private readonly Session _session;

            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _headline = Ui.Stack();
            private readonly StackPanel _sync = Ui.Stack();

            // The two halves of the tab, redrawn independently: picking a test redraws the board
            // and leaves the rail alone, and a write redraws both because it moved a count.
            /// <summary>
            /// Where the table goes. A grid, not a stack: a stack gives its child the height the
            /// child asks for, and the table has to be given the pane's height instead so the
            /// list inside it knows how many rows it has room to realise.
            /// </summary>
            private readonly Grid _board = new Grid();

            private readonly TextBox _path;
            private readonly TextBox _markups;

            private double _markerAcross = 300;

            /// <summary>
            /// Whether Focus also drops a ball where it puts the camera.
            ///
            /// On, because the thing Focus is for is finding a clash, and a clash between a duct
            /// and a beam looks exactly like a duct and a beam until something says which point
            /// of it the engine objected to.
            /// </summary>
            private bool _markOnFocus = true;

            /// <summary>
            /// How far back Focus stands the camera, in metres.
            ///
            /// Four by default, which frames a duct against a beam. A coordinator working a
            /// riser wants less and one working a car park wants more, and there is no way to
            /// guess which from the clash - so it is a number they set once and forget.
            /// </summary>
            private double _focusBack = 4000;

            /// <summary>
            /// Whether clicking a row moves the camera to it.
            ///
            /// Off by default, because picking and looking are different acts: somebody
            /// Shift-picking forty rows to set a status does not want the camera walking through
            /// all forty. On, it is the fastest way to read a board - click down the list and
            /// watch the model.
            /// </summary>
            private bool _focusOnClick;

            /// <summary>
            /// What this person chose last time, read once when the screen is built.
            ///
            /// Static, so switching tab and coming back does not re-read the file, and so the
            /// same choices are in force on every board in the session.
            /// </summary>
            private static readonly UserChoices Choices = UserChoices.Read();

            private const string ChoiceFocusBack = "clash.focusBackMm";
            private const string ChoiceMarkerAcross = "clash.markerAcrossMm";
            private const string ChoiceMarkOnFocus = "clash.markOnFocus";
            private const string ChoiceFocusOnClick = "clash.focusOnClick";

            /// <summary>Take up what this person chose the last time they were here.</summary>
            private void Remembered()
            {
                _focusBack = Choices.Get(ChoiceFocusBack, _focusBack);
                _markerAcross = Choices.Get(ChoiceMarkerAcross, _markerAcross);
                _markOnFocus = Choices.Get(ChoiceMarkOnFocus, _markOnFocus);
                _focusOnClick = Choices.Get(ChoiceFocusOnClick, _focusOnClick);
            }

            /// <summary>
            /// Keep what has just been chosen, beside the person rather than in the project.
            ///
            /// None of these is a fact about the job: how far the camera stands back and how big
            /// a marker is are how one coordinator likes to work, and writing them into the
            /// project file would impose them on everybody else who opens the same model.
            /// </summary>
            private void Remember()
            {
                Choices.Set(ChoiceFocusBack, _focusBack);
                Choices.Set(ChoiceMarkerAcross, _markerAcross);
                Choices.Set(ChoiceMarkOnFocus, _markOnFocus);
                Choices.Set(ChoiceFocusOnClick, _focusOnClick);

                Choices.Save();
            }

            /// <summary>What the board currently shows, for the spreadsheet writer.</summary>
            private List<Row> _shown = new List<Row>();

            private readonly StackPanel _columnList = Ui.Stack();
            private readonly StackPanel _fill = Ui.Stack();

            private string _newTab = string.Empty;
            private string _newProp = string.Empty;
            private string _newSide = BothSides;

            // ---- The rail: which test, and how the list of them is narrowed ----

            private string? _current;
            private string _railFind = string.Empty;
            private string _sort = MostNew;
            private bool _panel;

            private readonly HashSet<string> _byParty =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private readonly HashSet<string> _byFolder =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private bool _openOnly;

            // ---- The board: how the picked test's results are narrowed and arranged ----
            //
            // Every one of these is reset when the current test changes. A filter left over from
            // the last test is a board that says "nothing matches" about a test somebody has not
            // filtered, and the only clue is a chip they did not put there.

            private string? _status;
            private string? _party;
            private string _boardFind = string.Empty;

            /// <summary>What the filters last left, and the state it was worked out for.</summary>
            private BoardShape? _shape;
            private string? _shapeKey;

            /// <summary>Bumped whenever a row's own contents change, which no filter can see.</summary>
            private int _edits;

            /// <summary>
            /// The columns somebody has dragged. Those are pixels from then on, not a share of
            /// what is left - a star takes its width from what the other columns are doing, which
            /// is the wrong answer for one that has just been given a width by hand.
            /// </summary>
            private readonly HashSet<string> _sized = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Which column is being dragged, how wide it was, and where the drag began.</summary>
            /// <summary>What a drag is carrying, or null when nothing is being dragged.</summary>
            private List<Row>? _carrying;

            /// <summary>Where the press that might become a drag started.</summary>
            private Point _pressAt;

            /// <summary>The clipboard format a row drag travels under, private to this board.</summary>
            private const string CarryFormat = "CamelWorks.ClashRows";

            private string? _dragging;
            private double _dragFrom;
            private double _dragAt;

            /// <summary>The header's own column grid, which is what a drag measures against.</summary>
            private Grid? _headCells;

            /// <summary>The column grids of the rows that exist right now, for a drag to resize.</summary>
            private readonly List<WeakReference<Grid>> _cells = new List<WeakReference<Grid>>();

            /// <summary>Where the header row sits, so its tri-state can be redrawn on its own.</summary>
            private readonly ContentControl _head = new ContentControl();

            /// <summary>The property columns by their id, for the filter that narrows on one.</summary>
            private Dictionary<string, PropColumn> _columnsById =
                new Dictionary<string, PropColumn>(StringComparer.Ordinal);

            /// <summary>Waits for the typing to stop before the board acts on it.</summary>
            private readonly System.Windows.Threading.DispatcherTimer _typing =
                new System.Windows.Threading.DispatcherTimer();

            /// <summary>The lines the board is showing, and the list that draws the visible ones.</summary>
            private List<BoardLine>? _lines;
            private BoardList? _list;
            private string _arrange = ByNothing;
            private string _showing = BothKinds;

            /// <summary>
            /// The clash whose two items the board is narrowed to, or null.
            ///
            /// Not a status or a party but the same kind of thing: a narrowing, held with them,
            /// cleared with them, and reset the moment another test is picked.
            /// </summary>
            private Row? _sameAs;

            /// <summary>
            /// The property columns the board is narrowed by, column id to the value wanted.
            ///
            /// A dictionary rather than one field, because the columns are somebody's own — two
            /// of them narrowing at once (System Name "CHW", Workset "L03 MEP") is the question a
            /// custom column exists to be able to ask.
            /// </summary>
            private readonly Dictionary<string, string> _byColumn =
                new Dictionary<string, string>(StringComparer.Ordinal);

            private readonly HashSet<string> _open = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _picked = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Groups made on this board in this session, by name, for the "grouped by you" pill.</summary>
            private readonly HashSet<string> _made = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Where a prompt opens from, which has to be something long-lived on screen.</summary>
            private readonly Border _anchor = new Border();

            private const string MostNew = "Most new";
            private const string MostOpen = "Most open";
            private const string ByName = "Name";
            private const string LastRun = "Last run";
            private const string MostReviewed = "Most reviewed";
            private const string MostApproved = "Most approved";
            private const string MostClosed = "Most resolved";

            private const string BothKinds = "Both";
            private const string JustGrouped = "Grouped";
            private const string JustLoose = "Loose";

            private const string ByNothing = "Nothing";
            private const string ByAssignee = "Assignee";
            private const string ByStatus = "Status";
            private const string ByHowBad = "How bad";

            private const string Anyone = "Anyone";
            private const string Nobody = "—";

            private const string FirstSide = "First item";
            private const string SecondSide = "Second item";
            private const string BothSides = "Both";

            internal Screen(Session session)
            {
                _session = session;

                // Before anything is drawn, so the first board is already the way this person
                // left it rather than the way it looked the first time they ever opened it.
                Remembered();

                // A seat with no clash engine has exactly one source; the switch would be a lie.
                if (!session.Clash.IsAvailable) _source = FromFile;

                _path = Ui.Text(_file?.Path ?? string.Empty, null, 190);
                _markups = Ui.Text(string.Empty, null, 190);

                // Both search boxes are built here and placed once. Rebuilding a text box inside
                // the handler for its own keystroke takes the caret out of it, which is a search
                // that accepts one letter at a time.
                _railBox = Ui.Text(string.Empty, "Search tests by name or discipline", text =>
                {
                    _railFind = text;
                    RedrawRail();
                }, 150);

                _boardBox = Ui.Text(string.Empty, "Search", text =>
                {
                    _boardFind = text;

                    // Once the typing stops. Every keystroke used to filter the whole test and
                    // rebuild the board, so typing a six-letter word did that six times - five of
                    // them for a string nobody was looking for.
                    _typing.Stop();
                    _typing.Start();
                }, 128);

                _typing.Interval = TimeSpan.FromMilliseconds(160);
                _typing.Tick += (s, e) => Ui.Safely(() =>
                {
                    _typing.Stop();
                    RedrawRows();
                });

                _boardBox.MaxWidth = 128;
                _boardBox.VerticalAlignment = VerticalAlignment.Center;
                _boardBox.Margin = new Thickness(0, 0, 6, 0);
            }

            private readonly TextBox _railBox;
            private readonly TextBox _boardBox;

            private bool OnFile => _source == FromFile;

            /// <summary>
            /// The tab: a test finder on the left, one board on the right, one footer under both.
            ///
            /// <b>What this replaced.</b> Two tables side by side with seventeen always-visible
            /// symbols under them, and the two facts a coordinator changes every minute — a
            /// result's status and who it is with — behind a window opened by one of those
            /// symbols. The symbols were greyed rather than hidden, which is the wall of buttons
            /// that read as pressed rather than off.
            ///
            /// So: the rail finds the test, the board works it, and a verb appears when there is
            /// something for it to act on and not before.
            /// </summary>
            internal UIElement Build()
            {
                Indexed();

                // One caption for both sources: the results are what gets read either way, and
                // the tool registry promises this exact action.
                var already = Current() != null;

                // "Read the results" is the promise the tool registry makes and the right words
                // on an empty board. Once the board is full it is the wrong question — somebody
                // looking at 168 results does not need telling to fetch them — so it becomes what
                // pressing it again is actually for.
                // Two calls rather than one with a ternary in its caption slot, so each caption
                // is a literal the harness check can hold a picture to.
                var read = already
                    ? Ui.RunParts("Read again", _log, Read, primary: false)
                    : Ui.RunParts("Read the results", _log, Read, primary: false);

                read.Buttons.ToolTip = already
                    ? "Reads every test and result out of the document again. Worth pressing "
                      + "after somebody runs a test in Navisworks' own clash window, or when the "
                      + "board might have moved under you — nothing on this screen goes stale on "
                      + "its own."
                    : "Pulls every test and every result out of the document, with status, "
                      + "assignee, comments and grouping.";

                if (Current() == null)
                {
                    _headline.Children.Add(Bench.Ready(
                        OnFile
                            ? "Reading opens an exported clash file for markup: every verb here "
                              + "works on the file's own results, and the markup saves beside it "
                              + "for whoever runs the sync."
                            : "Reading pulls every test and every result out of the document, with "
                              + "status, assignee, comments and grouping — the tests down the rail "
                              + "on the left, the picked test's results on the board.",
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

                Redraw();

                var tab = Bench.Split(null, Rail(), BoardPane(), Footer(read), RailWidth);

                // After the footer, not before: the button it arms is built in there, and arming
                // a button that does not exist yet is how it came up saying "Save the changes"
                // on a published NWD that cannot take one.
                ArmSync();

                return tab;
            }

            /// <summary>How wide the test finder starts.</summary>
            private const double RailWidth = 372;

            // -------------------------------------------------------------------------------
            // Region 1 — the test finder
            //
            // One row per test, read down rather than across: a name, one line of hint, and the
            // four figures as a band. Eighteen tests in a table meant five numeric columns and
            // the name squeezed into what was left, which is the wrong way round — the name is
            // the long part and the numbers are the glance.
            // -------------------------------------------------------------------------------

            private readonly StackPanel _rows = Ui.Stack();
            private readonly Border _railFoot = new Border();
            private readonly StackPanel _railTools = new StackPanel { Orientation = Orientation.Horizontal };
            private readonly Border _railPanel = new Border();

            /// <summary>The rail, as one pane: head, rows, footer.</summary>
            private UIElement Rail()
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 0) };

                DockPanel.SetDock(_railTools, Dock.Right);
                top.Children.Add(Ui.Loose(_railTools));
                top.Children.Add(Ui.Loose(_railBox));

                var head = new Border
                {
                    Padding = new Thickness(14, 10, 14, 10),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = Ui.Stack(top, _railPanel),
                };

                head.Tint(Border.BorderBrushProperty, "Bd");
                grid.Children.Add(head);

                var list = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _rows,
                };

                Grid.SetRow(list, 1);
                grid.Children.Add(list);

                Grid.SetRow(_railFoot, 2);
                grid.Children.Add(Ui.Loose(_railFoot));

                var pane = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Child = grid,
                };

                pane.Tint(Border.BackgroundProperty, "Card");
                pane.Tint(Border.BorderBrushProperty, "Bd");

                return pane;
            }

            /// <summary>
            /// The rail's head: sort, the filter panel, and the chips in force.
            ///
            /// The search box itself is never rebuilt. It is a field, placed once, because a
            /// box that is replaced on the keystroke that changed it is a box the caret leaves
            /// after one letter — and this one narrows as you type.
            /// </summary>
            private void RedrawRail()
            {
                var read = Current();

                _railTools.Children.Clear();

                _railTools.Children.Add(Parts.MoreMenu(Glyphs.Sort, "Sort: " + _sort,
                    new[]
                        {
                            MostNew, MostOpen, MostReviewed, MostApproved, MostClosed,
                            ByName, LastRun,
                        }
                        .Select(o => Parts.Entry.Act(o, null, () => { _sort = o; RedrawRail(); }))
                        .ToList(),
                    on: _sort != MostNew));

                var narrow = Ui.Icon(Glyphs.Filter, WhatIsNarrowed(), () =>
                {
                    _panel = !_panel;
                    RedrawRail();
                });

                // The funnel says whether it is doing anything. With the chips gone this is the
                // only thing on the closed rail that does, and a list quietly hiding half its
                // tests with nothing on screen admitting it is the fault the chips were for.
                if (Narrowed) narrow.Content = Ui.Glyph(Glyphs.Filter, 15, "Accent");

                _railTools.Children.Add(narrow);

                _railPanel.Child = _panel ? Narrowing(read) : null;

                RedrawRailList();
                RedrawRailFoot();
            }

            /// <summary>Whether anything is narrowing the rail.</summary>
            private bool Narrowed =>
                _byParty.Count > 0 || _byFolder.Count > 0 || _openOnly || _railFind.Trim().Length > 0;

            /// <summary>What the funnel says it is doing, in full, on hover.</summary>
            private string WhatIsNarrowed()
            {
                if (!Narrowed) return "Narrow the list";

                var said = new List<string>();

                if (_byParty.Count > 0) said.Add(string.Join(", ", _byParty));
                if (_byFolder.Count > 0) said.Add(string.Join(", ", _byFolder));
                if (_openOnly) said.Add("only tests with something open");
                if (_railFind.Trim().Length > 0) said.Add("matching " + Quoted(_railFind.Trim()));

                return "Narrowed to " + string.Join(" · ", said) + ". Press to change it.";
            }

            /// <summary>
            /// The rail's filter panel: the disciplines and folders this read actually holds.
            ///
            /// The design asks for Level here. Nothing in a clash test carries one, and a level
            /// guessed out of a test's name is a filter that hides tests for a reason it cannot
            /// state. The folder a test was filed in is the grouping the host does carry, and it
            /// is where coordinators put the level, so the chips are values rather than inferences.
            /// </summary>
            /// <param name="read">Everything read, or null before the first read.</param>
            private UIElement Narrowing(List<TestEntry>? read)
            {
                var body = Ui.Stack();

                body.Children.Add(Ui.Eyebrow("Discipline"));

                var parties = new WrapPanel();

                foreach (var party in Trades(read))
                {
                    var here = party;

                    parties.Children.Add(Parts.Pick(here, _byParty.Contains(here), () =>
                    {
                        if (!_byParty.Remove(here)) _byParty.Add(here);
                        RedrawRail();
                    }));
                }

                body.Children.Add(parties);

                var folders = Folders(read);

                if (folders.Count > 0)
                {
                    body.Children.Add(Ui.Eyebrow("Folder"));

                    var chips = new WrapPanel();

                    foreach (var folder in folders)
                    {
                        var here = folder;

                        chips.Children.Add(Parts.Pick(here, _byFolder.Contains(here), () =>
                        {
                            if (!_byFolder.Remove(here)) _byFolder.Add(here);
                            RedrawRail();
                        }));
                    }

                    body.Children.Add(chips);
                }

                body.Children.Add(Ui.Check("Only tests with something open", _openOnly, on =>
                {
                    _openOnly = on;
                    RedrawRail();
                }));

                // Where the filters are made is where they come off. The chips that used to do
                // this sat in a row of their own above the list, which is a row of tests.
                if (Narrowed)
                    body.Children.Add(Ui.Link("Clear all", () =>
                    {
                        _byParty.Clear();
                        _byFolder.Clear();
                        _openOnly = false;
                        _railBox.Text = string.Empty;
                        RedrawRail();
                    }));

                var panel = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 8, 0, 0),
                    Child = body,
                };

                panel.Tint(Border.BackgroundProperty, "Bg");
                panel.Tint(Border.BorderBrushProperty, "Bd");

                return panel;
            }

            /// <summary>The column labels over the rail's rows.</summary>
            private UIElement RailLabels()
            {
                var row = new DockPanel { LastChildFill = true };

                var figures = new StackPanel { Orientation = Orientation.Horizontal };

                // Each figure's label is the order it stands for. The sort menu holds the same
                // six and says which is on; these are the one-press way to the four that are
                // already on screen as columns.
                var orders = new[]
                {
                    ("New", MostNew), ("Active", MostOpen), ("Rev.", MostReviewed),
                    ("Appr.", MostApproved), ("Res.", MostClosed),
                };

                foreach (var (label, order) in orders)
                {
                    var here = order;

                    var head = new TextBlock
                    {
                        Text = label,
                        TextAlignment = TextAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    head.Wearing("Hint");
                    if (_sort == here) head.Tint(TextBlock.ForegroundProperty, "Accent");

                    var press = new Border
                    {
                        MinWidth = 26,
                        Margin = new Thickness(10, 0, 0, 0),
                        Background = System.Windows.Media.Brushes.Transparent,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = _sort == here ? "Sorted by " + here.ToLowerInvariant()
                                                : "Sort by " + here.ToLowerInvariant(),
                        Child = head,
                    };

                    press.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                    {
                        _sort = here;
                        RedrawRail();
                    });

                    figures.Children.Add(press);
                }

                DockPanel.SetDock(figures, Dock.Right);
                row.Children.Add(figures);

                var name = new TextBlock { Text = "Test", VerticalAlignment = VerticalAlignment.Center };
                name.Wearing("Hint");
                if (_sort == ByName) name.Tint(TextBlock.ForegroundProperty, "Accent");

                var byName = new Border
                {
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = _sort == ByName ? "Sorted by name" : "Sort by name",
                    Child = name,
                };

                byName.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    _sort = ByName;
                    RedrawRail();
                });

                row.Children.Add(byName);

                var band = new Border
                {
                    Padding = new Thickness(27, 5, 14, 5),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = row,
                };

                band.Tint(Border.BackgroundProperty, "Card2");
                band.Tint(Border.BorderBrushProperty, "Bd");

                return band;
            }

            /// <summary>The rows: one test each, in the order the sort asks for.</summary>
            private void RedrawRailList()
            {
                _rows.Children.Clear();
                _rows.Children.Add(RailLabels());

                var shown = Shown();

                if (shown.Count == 0)
                {
                    _rows.Children.Add(new Border
                    {
                        Padding = new Thickness(14),
                        Child = Current() == null
                            ? Ui.Empty("Nothing has been read yet.",
                                       "Press \"Read the results\" on the footer, and the tests "
                                       + "land here.")
                            : Ui.Empty("No test matches this.",
                                       "Take a chip off above and the list comes back."),
                    });

                    return;
                }

                // Which run time most of them share, so a row can stay quiet about the ones that
                // ran with everything else and call out only the one that did not.
                var common = shown
                    .GroupBy(When, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? string.Empty;

                // Folders first, because a coordinator with eighty tests files them before they
                // sort them. The root's tests come first with no heading over them; every folder
                // after that gets one. A rail with nothing filed looks exactly as it always did.
                var filed = shown
                    .GroupBy(e => (e.Test.Folder ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key.Length == 0 ? 0 : 1)
                    .ThenBy(g => g.Key, StringComparer.CurrentCulture)
                    .ToList();

                foreach (var group in filed)
                {
                    if (group.Key.Length > 0)
                        _rows.Children.Add(FolderBand(group.Key, group.Count()));

                    foreach (var entry in group) _rows.Children.Add(RailRow(entry, common));
                }
            }

            /// <summary>
            /// A folder's heading on the rail.
            ///
            /// This project's folder, not the host's. Navisworks has folders of its own in the
            /// clash detective, but a published NWD will not take a write into them, so filing a
            /// test there simply refused on the seat where most review happens. These are kept in
            /// the project database and laid over the read, which means they show here and
            /// nowhere else - the native clash detective knows nothing about them.
            /// </summary>
            /// <param name="folder">The folder's name.</param>
            /// <param name="count">How many tests are in it.</param>
            private UIElement FolderBand(string folder, int count)
            {
                var row = new DockPanel { LastChildFill = true };

                var tally = new TextBlock
                {
                    Text = count.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                tally.Wearing("Hint");
                DockPanel.SetDock(tally, Dock.Right);
                row.Children.Add(tally);

                row.Children.Add(Ui.Glyph(Glyphs.Folder, 13, "Muted", new Thickness(0, 0, 7, 0)));

                var name = new TextBlock
                {
                    Text = folder,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                name.Wearing("Hint");
                row.Children.Add(name);

                var band = new Border
                {
                    Padding = new Thickness(12, 6, 14, 5),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    ToolTip = folder + " — a CamelWorks folder, kept in this project",
                    Child = row,
                };

                band.Tint(Border.BackgroundProperty, "Card2");
                band.Tint(Border.BorderBrushProperty, "Bd");

                return band;
            }

            /// <summary>One test, as a row of the rail.</summary>
            /// <param name="entry">The test.</param>
            /// <param name="common">The run time most tests share, which goes unsaid.</param>
            private UIElement RailRow(TestEntry entry, string common)
            {
                var on = string.Equals(entry.Test.Id, _current, StringComparison.OrdinalIgnoreCase);
                var open = entry.Of("New") + entry.Of("Active");

                var name = new TextBlock
                {
                    Text = entry.Test.Name,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };

                var when = When(entry);

                var hint = new TextBlock
                {
                    Text = Tolerance(entry)
                           + (when.Length == 0 || when == common ? string.Empty : " · run " + when),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(0, 1, 0, 0),
                };

                var words = Ui.Stack(name.Wearing("Body"), hint.Wearing("Hint"));

                var row = new DockPanel { LastChildFill = true };

                var band = Parts.CountBand(
                    new[]
                    {
                        entry.Of("New"), entry.Of("Active"), entry.Of("Reviewed"),
                        entry.Of("Approved"), entry.Of("Resolved"),
                    },
                    open == 0);

                var tick = TestTick(entry);
                DockPanel.SetDock(tick, Dock.Left);
                row.Children.Add(tick);

                DockPanel.SetDock(band, Dock.Right);
                row.Children.Add(band);
                row.Children.Add(words);

                var tone = new Border
                {
                    Padding = new Thickness(12, 8, 14, 8),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Child = row,
                };

                var line = new Border
                {
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = entry.Test.Name + " — " + Sides(entry) + ", hard clash at "
                              + Tolerance(entry)
                              + (when.Length == 0 ? string.Empty : ", run " + when),
                    Child = tone,
                };

                line.Tint(Border.BorderBrushProperty, "Bd");

                // The tone bar down the left edge: red while anything is new, amber while
                // anything is open, green when the test is finished — and the accent when it is
                // the one on the board, because "where am I" beats "how is it" on that one row.
                tone.BorderBrush = on ? Skin.Of("Accent")
                    : entry.Of("New") > 0 ? Skin.Of("RedTx")
                    : open > 0 ? Skin.Of("AmberTx")
                    : Skin.Of("Ok");

                if (on) line.Tint(Border.BackgroundProperty, "NavActive");
                else line.Background = System.Windows.Media.Brushes.Transparent;

                line.MouseLeftButtonUp += (s, e) => Ui.Safely(() => Pick(entry.Test.Id));

                if (!OnFile)
                    line.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount != 2) return;

                        e.Handled = true;

                        Prompt("Rename this test", entry.Test.Name, "Rename",
                               renamed => RenameTest(entry, renamed));
                    };

                // The same verbs the rail's symbol holds, on the row they act on. Right-clicking
                // a test that is not the one on the board picks it first, so the menu and the
                // board are never talking about different tests.
                line.ContextMenu = Parts.Context(() => TestMenu(entry),
                                                 () => Pick(entry.Test.Id));

                return line;
            }

            /// <summary>Put a test on the board, and forget everything set on the last one.</summary>
            /// <param name="testId">Which test.</param>
            private void Pick(string testId)
            {
                if (string.Equals(testId, _current, StringComparison.OrdinalIgnoreCase)) return;

                _current = testId;

                // Nothing carries over. A filter left from the last test is a board saying
                // "nothing matches" about a test nobody filtered, with a chip they did not set.
                _status = null;
                _party = null;
                _arrange = ByNothing;
                _showing = BothKinds;
                _sameAs = null;
                _byColumn.Clear();
                _open.Clear();
                _picked.Clear();
                _pickedFrom = null;
                _made.Clear();

                // The box, not the field behind it: clearing the field alone leaves the last
                // test's search showing over a board it is no longer filtering.
                _boardBox.Text = string.Empty;

                RedrawRailList();
                RedrawBoard();
            }

            /// <summary>The rail's footer: what the list adds up to, and the verbs on a test.</summary>
            /// <summary>
            /// Everything one test offers, behind the rail's symbol and on its right button alike.
            ///
            /// Every line here is a host operation the seam actually carries — run, rename,
            /// renumber, move, compact, delete. None of them is a finding, because none of them
            /// is a decision about a clash: they are things done to the test itself, and a
            /// document that will not take them has nothing to hold on their behalf.
            /// </summary>
            /// <param name="entry">The test the verbs act on, or null when none is picked.</param>
            private List<Parts.Entry> TestMenu(TestEntry? entry)
            {
                var read = Current() ?? new List<TestEntry>();

                if (OnFile)
                    return new List<Parts.Entry>
                    {
                        Parts.Entry.Heading("Export"),
                        Parts.Entry.Act("Marker spheres as .obj", Glyphs.Sphere,
                                        () => ExportTestMarkers(read)),
                        Parts.Entry.Act("Copy the list", Glyphs.Copy, () => CopyTests(read)),
                    };

                var reaching = Reaching(entry);
                var many = reaching.Count > 1;
                var what = many ? Ui.Count(reaching.Count, "test") : "this test";

                return new List<Parts.Entry>
                {
                    Parts.Entry.Heading(many ? "Do to " + what : "This test"),
                    Parts.Entry.Act(many ? "Run them again" : "Run this test again", Glyphs.Play,
                                    () => RunTests(reaching, _log)),
                    Parts.Entry.Act("Read every test's results again", Glyphs.Read,
                                    () => _readButtons?.Children
                                        .OfType<System.Windows.Controls.Primitives.ButtonBase>()
                                        .FirstOrDefault()?
                                        .RaiseEvent(new RoutedEventArgs(
                                            System.Windows.Controls.Primitives.ButtonBase.ClickEvent))),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Name and file"),
                    many
                        ? Parts.Entry.Barred("Rename", Glyphs.Pencil,
                            "One test at a time - a rename is one name.")
                        : Parts.Entry.Act("Rename this test", Glyphs.Pencil,
                            () => Prompt("Rename this test", entry?.Test.Name ?? string.Empty,
                                         "Rename", name => RenameTest(entry, name))),
                    Parts.Entry.Act(many ? "Renumber their results" : "Renumber the results",
                        Glyphs.Number,
                        () => Prompt("Renumber the results", string.Empty, "Renumber",
                                     prefix => RenameResults(reaching, prefix, _log))),
                    Parts.Entry.Act(many ? "Move them into a folder" : "Move into a folder",
                        Glyphs.MoveIn,
                        () => Prompt("Move into a folder", entry?.Test.Folder ?? string.Empty,
                                     "Move", folder => MoveTests(reaching, folder, _log))),
                    Parts.Entry.Act(many ? "Compact them - drop what is finished"
                                         : "Compact - drop what is finished",
                                    Glyphs.Compact, () => CompactTests(reaching, _log)),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Across the job"),
                    Parts.Entry.Act("Find duplicates across tests", Glyphs.Compact, Duplicates),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Export"),
                    Parts.Entry.Act("Marker spheres as .obj", Glyphs.Sphere,
                                    () => ExportTestMarkers(Entries(reaching))),
                    Parts.Entry.Act("Export a snapshot of every result", Glyphs.Camera,
                                    () => Snapshots(Entries(reaching)
                                                    .SelectMany(t => t.Rows)
                                                    .Cast<object>().ToList(), _log)),
                    Parts.Entry.Act("Copy the list", Glyphs.Copy, () => CopyTests(read)),

                    Parts.Entry.Rule(),
                    Parts.Entry.Danger(many ? "Delete " + what : "Delete this test", Glyphs.Bin,
                                       () => DeleteTests(reaching, _log)),
                };
            }

            private void RedrawRailFoot()
            {
                var read = Current() ?? new List<TestEntry>();
                var entry = Entry();

                var row = new DockPanel { LastChildFill = true };

                var verbs = new StackPanel { Orientation = Orientation.Horizontal };

                if (!OnFile)
                {
                    verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run all the tests again",
                        () => RunTests(read.Cast<object>().ToList(), _log)));

                    verbs.Children.Add(Parts.MoreMenu(Glyphs.More,
                        "Everything else you can do to tests", TestMenu(entry), up: true));
                }
                else
                {
                    verbs.Children.Add(Ui.Icon(Glyphs.Copy, "Copy the list", () => CopyTests(read)));
                }

                DockPanel.SetDock(verbs, Dock.Right);
                row.Children.Add(verbs);

                // The edits this document would not take, for the test on the board. Kept a
                // link because it is the one fact here somebody acts on, and kept per-test
                // because "14 waiting" over eighteen tests never said which one to open.
                var waiting = OnFile || entry == null
                    ? 0
                    : Waiting.Count(f => string.Equals(f.Rule, entry.Test.Name,
                                                       StringComparison.OrdinalIgnoreCase));

                if (waiting > 0)
                {
                    var dot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 5, 0),
                    };

                    dot.Tint(System.Windows.Shapes.Shape.FillProperty, "RedTx");

                    var held = Ui.Link(Skin.Num(waiting) + " waiting", ShowHeld);
                    held.Margin = new Thickness(0);
                    held.VerticalAlignment = VerticalAlignment.Center;
                    held.ToolTip = Ui.Count(waiting, "edit") + " on this test that the document "
                                   + "would not take. Press to see them.";

                    DockPanel.SetDock(dot, Dock.Right);
                    DockPanel.SetDock(held, Dock.Right);
                    row.Children.Add(held);
                    row.Children.Add(dot);
                }

                var shown = Shown().Count;

                var tests = read.Count == 0
                    ? "Nothing read yet."
                    : shown < read.Count
                        ? shown + " of " + Ui.Count(read.Count, "test")
                        : Ui.Count(read.Count, "test");

                // What the selected test holds, under the list it was selected from. The grouped
                // and loose split is the one number the board cannot show from its own rows,
                // because the board is filtered and this is not.
                var about = entry == null
                    ? string.Empty
                    : " · " + Skin.Num(entry.Rows.Count) + " results, "
                      + Skin.Num(entry.Rows.Count(r => r.Group.Length == 0)) + " loose";

                var say = new TextBlock
                {
                    Text = tests + about,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    ToolTip = entry == null ? null : entry.Test.Name + " — " + about.Substring(3),
                };

                row.Children.Add(say.Wearing("Hint"));

                _railFoot.Padding = new Thickness(14, 8, 14, 8);
                _railFoot.BorderThickness = new Thickness(0, 1, 0, 0);
                _railFoot.Child = row;
                _railFoot.Tint(Border.BackgroundProperty, "Pane");
                _railFoot.Tint(Border.BorderBrushProperty, "Bd");
            }

            private List<TestEntry>? Current() => OnFile ? _file?.Entries : _read;

            // -------------------------------------------------------------------------------
            // Region 2 — the board
            //
            // Two kinds of row at the same level, and the whole screen is built to keep them
            // apart.
            //
            // A CLASH GROUP is a real thing in the test: saved, exported, carrying its own status
            // and assignee, and changed only by grouping and ungrouping. ARRANGING THE VIEW is a
            // way of looking at the same rows — it draws bands and touches nothing. Somebody who
            // believes arranging by assignee regrouped their board will hand a report to the wrong
            // party, so a band looks nothing like a group row: no tick, no chevron, no tone bar.
            // -------------------------------------------------------------------------------

            /// <summary>One row of the board: a clash group, or a single loose result.</summary>
            /// <summary>
            /// One line of the board, as a plain object.
            ///
            /// The board hands these to <see cref="BoardList"/>, which turns the ones on screen into
            /// rows and leaves the rest as they are. They are deliberately cheap: making the whole
            /// list of them for a five-thousand-result test is a few thousand small objects, which is
            /// nothing, where making the visuals for them was the freeze.
            /// </summary>
            private sealed class BoardLine
            {
                private BoardLine(BoardRow? group, Row? clash, string id, bool inside,
                                  Func<UIElement>? band)
                {
                    GroupRow = group;
                    Clash = clash;
                    Id = id;
                    Inside = inside;
                    Band = band;
                }

                /// <summary>A clash group's own row.</summary>
                /// <param name="row">The group.</param>
                internal static BoardLine Group(BoardRow row) =>
                    new BoardLine(row, null, row.Id, false, null);

                /// <summary>One result.</summary>
                /// <param name="row">The result.</param>
                /// <param name="id">What picking it keys off.</param>
                /// <param name="inside">True when it sits inside an opened group.</param>
                internal static BoardLine Result(Row row, string id, bool inside) =>
                    new BoardLine(null, row, id, inside, null);

                /// <summary>
                /// A band across the rows.
                ///
                /// How to build it, not the built thing: the list recycles containers, and one
                /// element handed to a second container while the first still holds it is the
                /// "already the logical child of another element" throw.
                /// </summary>
                /// <param name="build">Builds the band.</param>
                internal static BoardLine Banner(Func<UIElement> build) =>
                    new BoardLine(null, null, string.Empty, false, build);

                internal BoardRow? GroupRow { get; }

                internal Row? Clash { get; }

                /// <summary>What picking this line keys off, or empty for a band.</summary>
                internal string Id { get; }

                /// <summary>True when it sits inside an opened group.</summary>
                internal bool Inside { get; }

                /// <summary>How to build the band, when this is one.</summary>
                internal Func<UIElement>? Band { get; }
            }
            private sealed class BoardRow
            {
                internal BoardRow(string id, string name, bool group, List<Row> rows)
                {
                    Id = id;
                    Name = name;
                    IsGroup = group;
                    Rows = rows;
                }

                /// <summary>What picking and expanding key off — a group's name, or a result's id.</summary>
                internal string Id { get; }

                internal string Name { get; }

                internal bool IsGroup { get; }

                /// <summary>The clashes: every member for a group, and itself for a loose result.</summary>
                internal List<Row> Rows { get; }

                /// <summary>The one result behind a loose row.</summary>
                internal Row Only => Rows[0];

                /// <summary>
                /// The status the row reads as.
                ///
                /// A group's members can disagree, and the worst of them is what the row says:
                /// a group showing "Resolved" because four of its five are is how an unresolved
                /// conflict leaves the board green and ends up in the building.
                /// </summary>
                internal string Status =>
                    Rows.OrderBy(r => Rank(r.Status)).Select(r => r.Status).FirstOrDefault() ?? "New";

                /// <summary>Who it is with, or empty when its members disagree.</summary>
                internal string Assigned
                {
                    get
                    {
                        var names = Rows.Select(r => r.Assigned)
                                        .Where(a => a.Length > 0)
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .Take(2)
                                        .ToList();

                        return names.Count == 1 ? names[0] : string.Empty;
                    }
                }

                /// <summary>Where a status sits in the promote-only lattice, worst first.</summary>
                /// <param name="status">The status.</param>
                private static int Rank(string status) => status switch
                {
                    "New" => 0,
                    "Active" => 1,
                    "Reviewed" => 2,
                    "Approved" => 3,
                    _ => 4,
                };
            }

            private readonly StackPanel _title = Ui.Stack();

            /// <summary>The read button and its Stop, kept so the footer can fold them away.</summary>
            private Panel? _readButtons;
            private readonly ContentControl _strip = new ContentControl();
            private readonly ContentControl _view = new ContentControl
            {
                VerticalAlignment = VerticalAlignment.Center,
            };

            private readonly ContentControl _sortBy = new ContentControl
            {
                VerticalAlignment = VerticalAlignment.Center,
            };

            /// <summary>
            /// The marker toggle, worded, and re-worded every time it is pressed.
            ///
            /// It was a symbol whose tooltip changed, on a page that is only rebuilt when the page
            /// is reopened - so pressing it hid the markers and left a button that still said
            /// "hide". Nothing about it said it would give them back, which is why it read as a
            /// one-way press.
            /// </summary>
            private readonly ContentControl _markerToggle = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
            };


            private readonly Border _bar = new Border();

            /// <summary>The board, as one pane: head, table, and the bar for what is picked.</summary>
            private UIElement BoardPane()
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // One line, not two. The name, the counts, the search and the verbs all sit on
                // the same row - the two rows they were on cost forty pixels of the thing this
                // window is actually for, which is the results.
                var head = new Border
                {
                    Padding = new Thickness(16, 8, 16, 4),
                    Child = Ui.Stack(_anchor, _toast, _headline, _title),
                };

                grid.Children.Add(head);

                // The rows scroll inside themselves, not inside this pane.
                //
                // A vertically scrolling parent hands its content unbounded height, and a
                // virtualising list given unbounded height realises every row it has to fill it -
                // which is virtualisation switched off by the container. The board takes the
                // pane's height and does its own scrolling, so what exists is what is on screen.
                var body = new Border
                {
                    Padding = new Thickness(16, 10, 16, 16),
                    Child = _board,
                };

                Grid.SetRow(body, 1);
                grid.Children.Add(body);

                Grid.SetRow(_bar, 2);
                grid.Children.Add(Ui.Loose(_bar));

                var ground = new Border { Child = grid };
                ground.Tint(Border.BackgroundProperty, "Bg");

                return ground;
            }

            /// <summary>Everything on the right-hand side, from the title down to the footnote.</summary>
            private void RedrawBoard()
            {
                _title.Children.Clear();

                var entry = Entry();

                if (entry == null)
                {
                    _board.Children.Clear();

                    if (Current() != null)
                        _board.Children.Add(Ui.Empty("No test is on the board.",
                            "Pick one on the left, and its results land here."));

                    RedrawSelection();
                    return;
                }

                _toleranceMm = ToleranceMm(entry);

                _title.Children.Add(Title(entry));

                _strip.Content = Strip(entry);

                // Two controls, not one, and each says whether it is doing anything.
                //
                // All of this used to hide behind a single stack-of-layers symbol, which named
                // neither of the two things it held. It was reported as "the clash results have
                // no filtering options, only sorting" - the filtering was there, behind a symbol
                // nobody reads as a filter, with nothing on screen saying it was in force.
                _view.Content = Parts.MoreMenu(Glyphs.Filter, WhatIsShown(entry),
                                               FilterMenu(entry), on: Filtering);

                _sortBy.Content = Parts.MoreMenu(Glyphs.Sort, WhatIsSorted(),
                                                 SortMenu(), on: InAnOrder);

                RedrawRows();
            }

            /// <summary>
            /// The rows, the chips over them and the bar under them — everything a keystroke or a
            /// tick moves, and nothing it does not.
            /// </summary>
            private void RedrawRows()
            {
                _board.Children.Clear();
                _cells.Clear();

                var entry = Entry();

                if (entry == null)
                {
                    _lines = null;
                    RedrawSelection();
                    return;
                }

                // One pass over the rows, kept, because four things wanted it: the rows
                // themselves, the count over them, the "of n" under the loose band and the bar
                // below. Each used to walk every result of the test through every filter again.
                var shape = Shape(entry);

                _shown = shape.Groups.SelectMany(g => g.Rows)
                              .Concat(shape.Loose.Select(r => r.Only)).ToList();

                _board.Children.Add(Table(entry, shape));

                RedrawSelection();
            }

            /// <summary>Rebuild the by-id column lookup the row filter uses.</summary>
            private void Indexed()
            {
                _columnsById = _columns.GroupBy(c => c.Id, StringComparer.Ordinal)
                                       .ToDictionary(g => g.Key, g => g.First(),
                                                     StringComparer.Ordinal);

                // A column added or taken away changes which rows the filters leave.
                Moved();
            }

            /// <summary>
            /// Put the widths the drag arrived at onto every row that exists.
            ///
            /// A column drag reports continuously, and rebuilding the board on each report was a
            /// board that could not be dragged. Only the rows on screen exist, so this is a
            /// screenful of grids taking a number.
            /// </summary>
            private void Resize()
            {
                var live = new List<WeakReference<Grid>>(_cells.Count);
                var widths = Widths();

                foreach (var held in _cells)
                {
                    if (!held.TryGetTarget(out var grid)) continue;

                    live.Add(held);

                    if (grid.ColumnDefinitions.Count != widths.Count) continue;

                    // Only what actually differs. Assigning a GridLength that is already there
                    // still invalidates the row's layout, so writing all ten columns on every
                    // mouse-move of a drag re-measured every realised row ten times over - which
                    // is the lag the cursor was running ahead of.
                    for (var i = 0; i < widths.Count; i++)
                    {
                        var column = grid.ColumnDefinitions[i];

                        if (column.Width != widths[i].Width) column.Width = widths[i].Width;
                        if (column.MinWidth != widths[i].Least) column.MinWidth = widths[i].Least;
                    }
                }

                _cells.Clear();
                _cells.AddRange(live);
            }

            /// <summary>
            /// Redraw one row, the header's tri-state and the picked bar, and nothing else.
            /// </summary>
            /// <param name="id">The row that changed.</param>
            private void Repaint(string id)
            {
                if (_lines != null && _list != null)
                {
                    foreach (var line in _lines)
                    {
                        if (!string.Equals(line.Id, id, StringComparison.Ordinal)) continue;

                        _list.Redraw(line);
                        break;
                    }
                }

                var entry = Entry();

                if (entry != null)
                {
                    var shape = Shape(entry);
                    _head.Content = Header(shape.Groups, shape.Loose);
                }

                RedrawSelection();
            }

            /// <summary>
            /// Every line the board is showing, in order, as plain objects.
            ///
            /// The visuals are made by <see cref="BoardList"/> for the lines that reach the
            /// screen. A group that is open contributes its own line and one per member, so every
            /// line is one row and the list can measure and recycle them.
            /// </summary>
            /// <param name="entry">The test on the board.</param>
            /// <param name="shape">What the filters left.</param>
            private List<BoardLine> Lines(TestEntry entry, BoardShape shape)
            {
                var lines = new List<BoardLine>();

                void Add(BoardRow row)
                {
                    if (!row.IsGroup)
                    {
                        lines.Add(BoardLine.Result(row.Only, row.Id, false));
                        return;
                    }

                    lines.Add(BoardLine.Group(row));

                    if (!_open.Contains(row.Id)) return;

                    foreach (var member in row.Rows)
                        lines.Add(BoardLine.Result(member, Id(member), true));
                }

                if (_arrange != ByNothing)
                {
                    foreach (var band in Bands(entry, shape.Groups, shape.Loose))
                    {
                        var rows = band.Value;

                        var named = band.Key;

                        lines.Add(BoardLine.Banner(() => Parts.ViewBand(
                            Glyphs.Columns, "View · " + _arrange, named,
                            Ui.Count(rows.Count, "row"), () => PickAll(rows))));

                        foreach (var row in rows) Add(row);
                    }

                    return lines;
                }

                foreach (var group in shape.Groups) Add(group);

                if (shape.Loose.Count > 0)
                {
                    // Never behind a tab. Grouping the loose results is the job, so the rows it is
                    // done to have to be on screen without anybody finding them first.
                    lines.Add(BoardLine.Banner(() => Parts.ViewBand(
                        Glyphs.Clash, "Not grouped",
                        Ui.Count(shape.Loose.Count, "loose result"),
                        "of " + Skin.Num(shape.LooseTotal) + " — tick two or more and press Group",
                        () => PickAll(shape.Loose))));

                    foreach (var row in shape.Loose) Add(row);
                }

                return lines;
            }

            /// <summary>The row that one line shows, built when the list scrolls to it.</summary>
            /// <param name="item">The line.</param>
            private UIElement? Drawn(object item)
            {
                if (!(item is BoardLine line)) return null;

                if (line.Band != null) return line.Band();

                if (line.GroupRow != null) return Group(line.GroupRow);

                return line.Clash == null ? null : Clash(line.Clash, line.Id, line.Inside);
            }

            /// <summary>The test, what it found, and the four verbs over the whole board.</summary>
            /// <param name="entry">The test on the board.</param>
            private UIElement Title(TestEntry entry)
            {
                var row = new DockPanel { LastChildFill = true };

                var verbs = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                // The search and the status chips come up here with the name and the verbs. They
                // had a row of their own under this one, which is a strip of window spent on two
                // controls.
                _boardBox.VerticalAlignment = VerticalAlignment.Center;
                _boardBox.Margin = new Thickness(10, 0, 10, 0);
                verbs.Children.Add(Ui.Loose(_boardBox));

                _strip.VerticalAlignment = VerticalAlignment.Center;
                _strip.Margin = new Thickness(0, 0, 10, 0);
                verbs.Children.Add(Ui.Loose(_strip));

                if (!OnFile)
                    verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run this test again",
                        () => RunTests(One(entry), _log)));

                verbs.Children.Add(Ui.Icon(Glyphs.Columns, "Which columns the board shows",
                    () => Configure("columns")));

                // Up here with the rest of the controls that open something, rather than beside
                // the search box where it read as a button that did something to the board.
                verbs.Children.Add(Ui.Loose(_sortBy));
                verbs.Children.Add(Ui.Loose(_view));

                verbs.Children.Add(Ui.Icon(Glyphs.Tune, "This test's source and tolerance",
                    () => Configure("source")));

                DockPanel.SetDock(verbs, Dock.Right);
                row.Children.Add(verbs);

                var total = entry.Rows.Count;
                var count = Counted(entry);

                var said = new TextBlock
                {
                    Text = count == total
                        ? Ui.Count(total, "result")
                        : "Showing " + Skin.Num(count) + " of " + Skin.Num(total),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(10, 0, 10, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };

                var head = new TextBlock
                {
                    Text = entry.Test.Name,
                    VerticalAlignment = VerticalAlignment.Bottom,

                    // The sentence that used to sit beside it: what the test is between, what it
                    // ran at and when. None of it changes while the board is worked, so it was a
                    // line of chrome the eye read once and then had to read past. It is on the
                    // title's tooltip, and the place beside the title now carries the one number
                    // that does change.
                    ToolTip = entry.Test.Name + " — " + Sides(entry) + ", hard clash at "
                              + Tolerance(entry)
                              + (When(entry).Length == 0 ? string.Empty : ", run " + When(entry)),
                };

                var words = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(head, Dock.Left);

                if (!OnFile)
                {
                    head.Cursor = System.Windows.Input.Cursors.Hand;

                    head.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2)
                            Prompt("Rename this test", entry.Test.Name, "Rename",
                                   name => RenameTest(entry, name));
                    };
                }

                words.Children.Add(head.Wearing("Head"));
                words.Children.Add(said.Wearing("Hint"));

                row.Children.Add(words);

                return row;
            }

            /// <summary>
            /// The status strip: five segments, and the only way the board is filtered by status.
            ///
            /// One row with the search and the view menu, and it does not wrap. Two rows of
            /// controls over a table is the table losing a line of itself to chrome at every
            /// window width.
            /// </summary>
            /// <param name="entry">The test on the board.</param>
            private UIElement Strip(TestEntry entry)
            {
                var strip = new StackPanel { Orientation = Orientation.Horizontal };

                var tabs = new (string Label, int Count, string Key, string? Status)[]
                {
                    ("all", entry.Rows.Count, "Text", null),
                    ("new", entry.Of("New"), "RedTx", "New"),
                    ("active", entry.Of("Active"), "Text", "Active"),
                    ("reviewed", entry.Of("Reviewed"), "AmberTx", "Reviewed"),
                    ("approved", entry.Of("Approved"), "Ok", "Approved"),
                    ("resolved", entry.Of("Resolved"), "Ok", "Resolved"),
                };

                for (var i = 0; i < tabs.Length; i++)
                {
                    var tab = tabs[i];
                    var on = string.Equals(_status, tab.Status, StringComparison.Ordinal);

                    var figure = new TextBlock
                    {
                        Text = Skin.Num(tab.Count),
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Bottom,
                    };

                    figure.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty,
                                    FontNumeralAlignment.Tabular);

                    figure.Tint(TextBlock.ForegroundProperty, tab.Key);

                    var label = new TextBlock
                    {
                        Text = tab.Label,
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(4, 0, 0, 0),
                    };

                    label.Tint(TextBlock.ForegroundProperty, on ? "Text" : "Faint");

                    var words = new StackPanel { Orientation = Orientation.Horizontal };
                    words.Children.Add(figure.Wearing("Body"));
                    words.Children.Add(label);

                    var segment = new Border
                    {
                        Padding = new Thickness(8, 6, 8, on ? 4 : 6),
                        BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, on ? 2 : 0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Child = words,
                    };

                    segment.Tint(Border.BorderBrushProperty, on ? "Accent" : "Bd");

                    if (on) segment.Tint(Border.BackgroundProperty, "AccentWash");
                    else segment.Background = System.Windows.Media.Brushes.Transparent;

                    var status = tab.Status;

                    segment.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                    {
                        _status = on ? null : status;
                        _picked.Clear();
                        _pickedFrom = null;
                        RedrawBoard();
                    });

                    strip.Children.Add(segment);
                }

                var pill = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,

                    // Left, or the pill is stretched to the width of the window and the last
                    // segment reads as a button running off across the screen.
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = strip,
                };

                pill.Tint(Border.BackgroundProperty, "Card");
                pill.Tint(Border.BorderBrushProperty, "Bd");

                // A hard clip, or the segment washes paint over the rounded corners the strip
                // just drew.
                pill.SetValue(UIElement.ClipToBoundsProperty, true);

                return pill;
            }

            /// <summary>What the view menu is currently doing, for the symbol that opens it.</summary>
            /// <param name="entry">The test on the board.</param>
            private string WhatIsShown(TestEntry entry)
            {
                var said = new List<string>();

                if (_showing != BothKinds) said.Add(_showing.ToLowerInvariant() + " only");
                if (_party != null) said.Add("assigned to " + _party);
                if (_sameAs != null) said.Add("sharing an item with " + Both(_sameAs));

                foreach (var narrowed in _byColumn)
                {
                    var column = _columns.FirstOrDefault(c => c.Id == narrowed.Key);
                    if (column != null) said.Add(column.Title + " " + Quoted(narrowed.Value));
                }
                _ = entry;

                return said.Count == 0
                    ? "Narrow this board — grouped or loose, whose it is, what its columns say"
                    : "Showing " + string.Join(", ", said) + ". Press to change it.";
            }

            /// <summary>
            /// One property column's section of the view menu: the values this test actually has.
            ///
            /// The values are read off the board rather than typed, so the menu can only offer a
            /// narrowing that will match something. A column nobody has filled yet says so instead
            /// of showing an empty list — the values arrive when "Read values" is pressed on the
            /// Columns page, because reading a property off every clashing element is a walk of
            /// the federation and not something a menu opens.
            /// </summary>
            /// <param name="entry">The test on the board.</param>
            /// <param name="column">The property column.</param>
            private List<Parts.Entry> ColumnMenu(TestEntry entry, PropColumn column)
            {
                const int Most = 20;

                var wanted = _byColumn.TryGetValue(column.Id, out var value) ? value : null;

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Rule(),
                    Parts.Entry.Heading(column.Title + ": " + (wanted ?? Anyone)),
                };

                var values = entry.Rows
                    .Select(r => Custom(r, column))
                    .Where(v => v.Length > 0)
                    .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .ToList();

                if (values.Count == 0)
                {
                    items.Add(Parts.Entry.Barred("Nothing read into this column yet", null,
                        "Press \"Read values\" on the Columns page. It reads the property off "
                        + "every clashing element, which is a walk of the federation."));

                    return items;
                }

                items.Add(Parts.Entry.Act(Anyone, null, () =>
                {
                    _byColumn.Remove(column.Id);
                    _picked.Clear();
                    _pickedFrom = null;
                    RedrawBoard();
                }));

                // The commonest first, then capped and put back in reading order. A column over a
                // real federation carries two hundred distinct values, and a menu that long is a
                // list nobody reaches the end of — the search box matches these columns too.
                foreach (var one in values.Take(Most).OrderBy(v => v, StringComparer.CurrentCulture))
                {
                    var here = one;

                    items.Add(Parts.Entry.Act(here, null, () =>
                    {
                        _byColumn[column.Id] = here;
                        _picked.Clear();
                        _pickedFrom = null;
                        RedrawBoard();
                    }));
                }

                if (values.Count > Most)
                    items.Add(Parts.Entry.Barred(
                        Skin.Num(values.Count - Most) + " more values", null,
                        "The twenty commonest are listed. Type one into the search box for the "
                        + "rest — it matches these columns too."));

                return items;
            }

            /// <summary>Whether the board is in any order but the one the test holds.</summary>
            private bool InAnOrder => _by != null || _arrange != ByNothing;

            /// <summary>What the sort control is doing, for the symbol that opens it.</summary>
            private string WhatIsSorted()
            {
                var said = new List<string>();

                if (_by != null) said.Add("by " + _by + (_back ? ", backwards" : string.Empty));
                if (_arrange != ByNothing) said.Add("arranged by " + _arrange.ToLowerInvariant());

                return said.Count == 0
                    ? "Sort the results"
                    : "Sorted " + string.Join(", ", said) + ". Press to change it.";
            }

            /// <summary>
            /// How the board is ordered: by a column, and banded by a facet.
            ///
            /// Its own control rather than a section of the filter menu, because sorting and
            /// narrowing are different questions and a coordinator reaches for them at different
            /// moments. The rail has had the two apart since it was built; the board had them in
            /// one place behind a symbol that named neither.
            /// </summary>
            private List<Parts.Entry> SortMenu()
            {
                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading("Order: " + (_by ?? "as the test holds them")),
                };

                foreach (var column in Widths())
                {
                    var here = column.Name;

                    items.Add(Parts.Entry.Act(
                        here + (string.Equals(_by, here, StringComparison.Ordinal)
                                    ? _back ? " (backwards)" : " (forwards)"
                                    : string.Empty),
                        null, () => SortBy(here)));
                }

                if (_by != null)
                {
                    items.Add(Parts.Entry.Rule());

                    items.Add(Parts.Entry.Act("Back to the test's own order", Glyphs.Close, () =>
                    {
                        _by = null;
                        _back = false;
                        RedrawBoard();
                    }));
                }

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Band the rows by: " + _arrange));

                foreach (var by in new[] { ByNothing, ByAssignee, ByStatus, ByHowBad })
                {
                    var here = by;

                    items.Add(Parts.Entry.Act(here, null, () =>
                    {
                        _arrange = here;
                        RedrawBoard();
                    }));
                }

                return items;
            }

            /// <summary>What is behind the board's filter menu: show, assigned to, the columns.</summary>
            /// <param name="entry">The test on the board.</param>
            private List<Parts.Entry> FilterMenu(TestEntry entry)
            {
                var items = new List<Parts.Entry> { Parts.Entry.Heading("Show: " + _showing) };

                foreach (var kind in new[] { BothKinds, JustGrouped, JustLoose })
                {
                    var here = kind;

                    items.Add(Parts.Entry.Act(here, null, () =>
                    {
                        _showing = here;
                        _picked.Clear();
                        _pickedFrom = null;
                        RedrawBoard();
                    }));
                }

                if (_sameAs != null)
                {
                    items.Add(Parts.Entry.Rule());
                    items.Add(Parts.Entry.Heading("Sharing an item with " + Both(_sameAs)));

                    items.Add(Parts.Entry.Act("Every clash again, not just these", Glyphs.Close,
                        () =>
                        {
                            _sameAs = null;
                            RedrawBoard();
                        }));
                }

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Assigned to: " + (_party ?? Anyone)));

                foreach (var who in new[] { Anyone }.Concat(Assignees(entry)))
                {
                    var here = who;

                    items.Add(Parts.Entry.Act(here, null, () =>
                    {
                        _party = here == Anyone ? null : here;
                        _picked.Clear();
                        _pickedFrom = null;
                        RedrawBoard();
                    }));
                }

                foreach (var column in _columns) items.AddRange(ColumnMenu(entry, column));

                // Where the narrowing is set is where it comes off, including the two set
                // elsewhere: the status segment above and the text in the search box.
                if (Filtering)
                {
                    items.Add(Parts.Entry.Rule());

                    items.Add(Parts.Entry.Act("Show everything again", Glyphs.EyeOff, () =>
                    {
                        _status = null;
                        _party = null;
                        _showing = BothKinds;
                        _arrange = ByNothing;
                        _sameAs = null;
                        _byColumn.Clear();
                        _boardBox.Text = string.Empty;
                        RedrawBoard();
                    }));
                }

                return items;
            }

            // -------------------------------------------------------------------------------
            // The table
            // -------------------------------------------------------------------------------

            /// <summary>One row's worth of columns, in the order the header names them.</summary>
            private Grid Cells()
            {
                var grid = new Grid();

                // Kept, weakly, so a column drag can set the widths on the rows that exist rather
                // than building every row again. Weakly because the list recycles containers and
                // a strong reference here would stop the ones scrolled away from ever going.
                _cells.Add(new WeakReference<Grid>(grid));

                foreach (var column in Widths())
                    grid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = column.Width,
                        MinWidth = column.Least,
                    });

                return grid;
            }

            /// <summary>
            /// How wide each column is, as one list, so a row and a drag cannot disagree.
            /// </summary>
            private List<(string Name, GridLength Width, double Least)> Widths()
            {
                var columns = new List<(string, GridLength, double)>();

                void Fixed(double width) =>
                    columns.Add((string.Empty, new GridLength(width), 0));

                // A column somebody has dragged is that many pixels wide from then on, whatever
                // it was before. Sharing what is left is the right resting behaviour and the
                // wrong thing to do to a column that has just been given a width by hand: a star
                // takes its size from what the others are doing, so dragging one moved the rest
                // and the edge under the pointer went where the arithmetic sent it.
                void Named2(string name, double share, double least)
                {
                    if (_sized.Contains(name))
                    {
                        columns.Add((name, new GridLength(Wide(name, least)), least));
                        return;
                    }

                    columns.Add((name, new GridLength(share, GridUnitType.Star), least));
                }

                void Flexible(double share, double least) =>
                    columns.Add((string.Empty, new GridLength(share, GridUnitType.Star), least));

                _ = (Action<double, double>)Flexible;

                Fixed(26);

                // The name takes twice what either item does, and all three give ground before
                // the table starts scrolling sideways.
                //
                // The design fixes Item 1 and Item 2 at 118 apiece and lets only the name flex,
                // which adds up to 862 — wider than the board is at the window's own default
                // size. The picture that came back was a table scrolled off its own status and
                // assignee columns, which are the two things this screen was rebuilt to put on
                // the row. Horizontal scroll is meant to be the last resort here, not the
                // resting state.
                // The three that give ground give more of it once there are property columns to
                // make room for. One custom column at the window's own width is the difference
                // between a board that fits and a board scrolled off its row verbs, and a column
                // somebody added on purpose should not cost them the verbs.
                var tight = _columns.Count > 0;

                Named2(Named, 2, tight ? 110 : 130);
                Named2(ItemA, 1, tight ? 58 : 74);
                Named2(ItemB, 1, tight ? 58 : 74);

                columns.Add((Clashes, new GridLength(Wide(Clashes, tight ? 44 : 48)), 0));
                columns.Add((Worst, new GridLength(Wide(Worst, tight ? 60 : 64)), 0));

                // The property columns land between Worst and Status, where the eye is already
                // reading facts about the elements rather than decisions about them.
                // Flexible, like the name and the two items, and for the same reason: a fixed
                // width adds itself to the table, and a table that grows by 95 pixels every time
                // somebody adds a column is a table that scrolls sideways off its own row verbs
                // on the second one. A property column takes a share of what there is and
                // ellipsises, with the whole value on the cell's tooltip.
                foreach (var column in _columns) Named2(column.Title, 1, 60);

                columns.Add((Status, new GridLength(Wide(Status, 104)), 0));
                columns.Add((Assigned, new GridLength(Wide(Assigned, 104)), 0));

                // Four row buttons, at the tighter margin the bar below sets on them. The
                // design's 92 holds three and cuts the fourth, which in the picture read as a
                // row that had lost its Focus verb.
                Fixed(120);

                return columns;
            }

            /// <summary>Put one cell in a row.</summary>
            /// <param name="grid">The row.</param>
            /// <param name="cell">What goes in it.</param>
            /// <param name="column">Which column.</param>
            private static void Put(Grid grid, UIElement cell, int column)
            {
                if (cell is FrameworkElement element)
                {
                    element.Margin = new Thickness(0, 0, 8, 0);
                    element.VerticalAlignment = VerticalAlignment.Center;
                }

                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }

            /// <summary>Where Status, Assigned and the verbs sit once the custom columns are in.</summary>
            private int StatusColumn => 6 + _columns.Count;

            // ---- The columns, and what somebody has done to them ----

            // Short enough to survive the column it labels. "Clash group or loose result" is what
            // the design writes, and at the board's own width it came out cut mid-word, which
            // reads as a bug rather than as a long label.
            private const string Named = "Group or result";
            private const string ItemA = "Item 1";
            private const string ItemB = "Item 2";
            private const string Clashes = "Clashes";
            private const string Worst = "Worst";
            private const string Status = "Status";
            private const string Assigned = "Assigned";

            /// <summary>
            /// How wide each column is: a share for the three that flex, pixels for the rest.
            ///
            /// Held here rather than written into <see cref="Cells"/> because a table whose
            /// columns cannot be dragged is a table that is the wrong shape for somebody — the
            /// names are long on one job and the assignees are long on the next.
            /// </summary>
            private readonly Dictionary<string, double> _wide =
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    { Named, 2 }, { ItemA, 1 }, { ItemB, 1 },
                    { Clashes, 48 }, { Worst, 64 }, { Status, 104 }, { Assigned, 104 },
                };

            /// <summary>
            /// How wide a column is: what was dragged, or what it starts at.
            ///
            /// Through a lookup rather than an indexer because a property column is somebody's
            /// own — it did not exist when this table was written and it will not be there
            /// tomorrow, so it has no entry until the first time it is dragged.
            /// </summary>
            /// <param name="column">The column.</param>
            /// <param name="starting">Its width, or its share, before anybody moved it.</param>
            private double Wide(string column, double starting) =>
                _wide.TryGetValue(column, out var was) ? was : starting;

            /// <summary>Which column the board is sorted by, or null for the board's own order.</summary>
            private string? _by;

            /// <summary>Whether the sort runs the other way.</summary>
            private bool _back;

            /// <summary>Sort by a column, or turn it round when it is already the one.</summary>
            /// <param name="column">Which column.</param>
            private void SortBy(string column)
            {
                if (string.Equals(_by, column, StringComparison.Ordinal))
                {
                    // Third press puts it back the way the test holds it. A sort with no way off
                    // is a board somebody has to reload to see in its own order again.
                    if (_back) { _by = null; _back = false; }
                    else _back = true;
                }
                else
                {
                    _by = column;
                    _back = false;
                }

                RedrawRows();
            }

            /// <summary>The rows in whatever order the column headers ask for.</summary>
            /// <param name="rows">The rows.</param>
            private List<BoardRow> Sorted(List<BoardRow> rows)
            {
                if (_by == null) return rows;

                var sorted = rows.OrderBy(Key, StringComparer.CurrentCulture).ToList();

                if (_back) sorted.Reverse();

                return sorted;
            }

            /// <summary>What one row sorts on, for the column in force.</summary>
            /// <param name="row">The row.</param>
            private string Key(BoardRow row)
            {
                switch (_by)
                {
                    case ItemA: return row.IsGroup ? string.Empty : row.Only.Info.NameA ?? string.Empty;
                    case ItemB: return row.IsGroup ? string.Empty : row.Only.Info.NameB ?? string.Empty;
                    case Status: return row.Status;
                    case Assigned: return row.Assigned;

                    // Numbers, ordered as numbers. Sorting "-9 mm" against "-48 mm" as text puts
                    // the shallower one first, which is the wrong end of the board entirely.
                    case Clashes: return Ordered(row.IsGroup ? row.Rows.Count : 0);
                    case Worst: return Ordered((int)(Deep(row) ?? 0));

                    default:
                        // A property column, sorted on the value it read. A group takes its
                        // first member's, which is the only answer available: the column is a
                        // fact about an element and a group is many of them.
                        var column = _columns.FirstOrDefault(
                            c => string.Equals(c.Title, _by, StringComparison.Ordinal));

                        return column != null ? Custom(row.Rows[0], column) : row.Name;
                }
            }

            /// <summary>A number as text that sorts like a number, negatives included.</summary>
            /// <param name="value">The number.</param>
            private static string Ordered(int value) =>
                (value + 1000000000L).ToString("D10", CultureInfo.InvariantCulture);

            /// <summary>
            /// The whole table: the header, the rows, and one card round both.
            ///
            /// The rows go into a <see cref="BoardList"/>, which builds only the ones on screen.
            /// The card sits inside a sideways scroller with vertical scrolling switched off, so
            /// the card is given the pane's height rather than infinite height - which is what
            /// lets the list inside it know how many rows it has room for. Without that it would
            /// be handed unbounded space, realise every row to fill it, and virtualise nothing.
            /// </summary>
            /// <param name="entry">The test on the board.</param>
            /// <param name="shape">What the filters left.</param>
            private UIElement Table(TestEntry entry, BoardShape shape)
            {
                var frame = new Grid();
                frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                _head.Content = Header(shape.Groups, shape.Loose);

                // The header is outside the scroller and the rows are inside it, so the rows
                // stop at the scrollbar and the header did not. The last column is the row's
                // verbs, and what that looked like was the "..." menu clipped under the bar.
                _head.Margin = new Thickness(0, 0, SystemParameters.VerticalScrollBarWidth, 0);

                // Loose first. The header is kept in a field so its tri-state can be redrawn on
                // its own, and this builds a new frame every draw - so the second draw hands the
                // same element to a second parent while the first still holds it.
                frame.Children.Add(Ui.Loose(_head));

                if (shape.Groups.Count == 0 && shape.Loose.Count == 0)
                {
                    _lines = null;
                    _list = null;

                    var nothing = new Border
                    {
                        Padding = new Thickness(16),
                        Child = Filtering
                            ? Ui.Empty("Nothing on this board matches what you are showing.",
                                       "Take a chip off above and the rows come back.")
                            : Ui.Empty("Nothing on this board matches what you are showing.",
                                       "This test found nothing at " + Tolerance(entry) + "."),
                    };

                    Grid.SetRow(nothing, 1);
                    frame.Children.Add(nothing);
                }
                else
                {
                    _lines = Lines(entry, shape);
                    _list = new BoardList(Drawn) { ItemsSource = _lines };

                    Grid.SetRow(_list, 1);
                    frame.Children.Add(Ui.Loose(_list));
                }

                var card = new Border
                {
                    // The columns' own floors, plus the 12 of padding either side of a row.
                    // Left out, the card came up two dozen pixels narrower than its contents and
                    // the last column lost a symbol off its right-hand end.
                    MinWidth = (_columns.Count == 0 ? 744 : 686 + (_columns.Count * 60)) + 26,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Child = frame,
                };

                card.Tint(Border.BackgroundProperty, "Card");
                card.Tint(Border.BorderBrushProperty, "Bd");
                card.SetValue(UIElement.ClipToBoundsProperty, true);

                // Only the table scrolls sideways. Wrapping the whole board in a horizontal
                // scroller hands everything in it its desired width instead of the pane's, which
                // is how the footnote under the table came out cut off mid-sentence.
                var sideways = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = card,
                };

                // Width, bound to the viewport, and MinWidth left where it is.
                //
                // A scroller measures its content against infinite width, and a star column
                // measured against infinity sizes to its own text — so the table came out wider
                // than the board at every width, scrolled sideways off its status and assignee
                // columns even when there was room for them. Saying how wide the card is puts a
                // real number back in front of the stars. MinWidth still beats Width in WPF's
                // own arithmetic, so the columns' floor is what makes it scroll, and only when
                // the board is genuinely too narrow to hold them.
                card.SetBinding(FrameworkElement.WidthProperty,
                                new System.Windows.Data.Binding("ViewportWidth")
                                {
                                    Source = sideways,
                                });

                return sideways;
            }

            /// <summary>The header row: the tri-state tick, and one label per column.</summary>
            /// <param name="groups">The clash groups the filters left.</param>
            /// <param name="loose">The loose results the filters left.</param>
            private UIElement Header(List<BoardRow> groups, List<BoardRow> loose)
            {
                var grid = Cells();

                _headCells = grid;

                var top = groups.Concat(loose).ToList();
                var picked = top.Count(r => _picked.Contains(r.Id));

                var all = new CheckBox
                {
                    IsThreeState = true,
                    IsChecked = picked == 0 ? false : picked == top.Count ? true : (bool?)null,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Pick every row on the board",
                };

                all.Click += (s, e) => Ui.Safely(() =>
                {
                    var on = picked < top.Count;

                    _picked.Clear();
                    _pickedFrom = null;
                    if (on) foreach (var row in top) _picked.Add(row.Id);

                    RedrawRows();
                });

                Put(grid, all, 0);
                Put(grid, Head(Named, false), 1);
                Put(grid, Head(ItemA, false), 2);
                Put(grid, Head(ItemB, false), 3);
                Put(grid, Head(Clashes, true), 4);
                Put(grid, Head(Worst, true), 5);

                for (var i = 0; i < _columns.Count; i++)
                    Put(grid, Head(_columns[i].Title, false), 6 + i);

                Put(grid, Head(Status, false), StatusColumn);
                Put(grid, Head(Assigned, false), StatusColumn + 1);

                var band = new Border
                {
                    Padding = new Thickness(12, 8, 12, 8),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = grid,
                };

                band.Tint(Border.BackgroundProperty, "Card2");
                band.Tint(Border.BorderBrushProperty, "Bd");

                return band;
            }

            /// <summary>
            /// One column header: press it to sort, drag its edge to resize it.
            ///
            /// Both, because a header that does neither is a label, and a table of four hundred
            /// rows is worked by ordering it. The sort has three states rather than two — one way,
            /// the other, and back to the order the test itself holds — so there is a way off it
            /// that is not re-reading the board.
            /// </summary>
            /// <param name="column">Which column, which is also what it says.</param>
            /// <param name="right">True for a figure, whose label sits over its digits.</param>
            private UIElement Head(string column, bool right)
            {
                var on = string.Equals(_by, column, StringComparison.Ordinal);

                var label = new TextBlock
                {
                    Text = column,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };

                label.Wearing("Hint");
                if (on) label.Tint(TextBlock.ForegroundProperty, "Accent");

                var words = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var mark = on
                    ? Ui.Glyph(_back ? Glyphs.Up : Glyphs.Down, 10, "Accent",
                               right ? new Thickness(0, 0, 3, 0) : new Thickness(4, 0, 0, 0))
                    : null;

                if (right && mark != null) words.Children.Add(mark);

                words.Children.Add(label);

                if (!right && mark != null) words.Children.Add(mark);

                var press = new Border
                {
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = !on ? "Sort by " + column
                            : _back ? "Sorted by " + column + ", the other way round. Press again "
                                      + "for the order the test holds."
                            : "Sorted by " + column + ". Press again to turn it round.",
                    Child = words,
                };

                press.MouseLeftButtonUp += (s, e) => Ui.Safely(() => SortBy(column));

                // The drag handle on the right edge. A Thumb rather than a GridSplitter: every
                // row builds its own Grid out of these widths, so there is no one grid for a
                // splitter to live in — what is being dragged is the width itself, not a column
                // of one particular row.
                var grip = new System.Windows.Controls.Primitives.Thumb
                {
                    Width = 5,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Cursor = System.Windows.Input.Cursors.SizeWE,
                    Opacity = 0,
                    ToolTip = "Drag to resize " + column,
                };

                // Measured from the pointer, not from the thumb.
                //
                // The thumb sits at the right edge of the column it resizes, so resizing moves it
                // - and a Thumb reports each change from wherever it now is. Feeding that back
                // into the width made the edge lag, overshoot, and on a column whose neighbours
                // took up the slack, travel the opposite way to the hand. The pointer does not
                // move when the column does, so the whole drag is measured against where it
                // started.
                grip.DragStarted += (s, e) => Ui.Safely(() =>
                {
                    _dragging = column;
                    _dragFrom = Rendered(column);
                    _dragAt = System.Windows.Input.Mouse.GetPosition(_head).X;
                });

                grip.DragDelta += (s, e) => Ui.Safely(() =>
                {
                    if (!string.Equals(_dragging, column, StringComparison.Ordinal)) return;

                    var now = System.Windows.Input.Mouse.GetPosition(_head).X;

                    Widen(column, _dragFrom + (now - _dragAt));
                });

                grip.DragCompleted += (s, e) => Ui.Safely(() => _dragging = null);

                var cell = new Grid();
                cell.Children.Add(press);
                cell.Children.Add(grip);

                return cell;
            }

            /// <summary>
            /// Set one column to the width the drag has reached.
            ///
            /// An absolute width, in pixels, not a nudge. The old arithmetic added the drag to a
            /// star share through a divide-by-sixty, so a column's width depended on what every
            /// other column was doing and the number under the pointer was never the number on
            /// screen. From the first drag a column is simply as wide as it was dragged to.
            /// </summary>
            /// <param name="column">Which column.</param>
            /// <param name="to">How wide it should be now, in pixels.</param>
            private void Widen(string column, double to)
            {
                // Whole pixels. A drag reports sub-pixel moves, and a width that changes by a
                // fifth of a pixel is a full measure and arrange of every row on the board for a
                // change nobody can see.
                var want = Math.Round(Math.Max(Least(column), Math.Min(640, to)));

                if (_wide.TryGetValue(column, out var already) && already == want
                    && _sized.Contains(column))
                    return;

                _sized.Add(column);
                _wide[column] = want;

                // Every realised row shares these column definitions, so setting the width once
                // moves all of them. This used to rebuild every row on the board on every
                // mouse-move of the drag.
                Resize();
            }

            /// <summary>How wide a column is on screen right now.</summary>
            /// <param name="column">Which column.</param>
            private double Rendered(string column)
            {
                var at = Widths().FindIndex(c => string.Equals(c.Name, column, StringComparison.Ordinal));

                if (at < 0 || !(_head.Content is FrameworkElement) || _headCells == null) return Least(column);

                return at < _headCells.ColumnDefinitions.Count
                    ? _headCells.ColumnDefinitions[at].ActualWidth
                    : Least(column);
            }

            /// <summary>The narrowest a column may be dragged, so a header stays readable.</summary>
            /// <param name="column">Which column.</param>
            private double Least(string column) =>
                column == Named ? 90
                : column == ItemA || column == ItemB ? 56
                : 44;

            /// <summary>A column label.</summary>
            /// <param name="text">What it says.</param>
            private static TextBlock Hint(string text) =>
                new TextBlock
                {
                    Text = text,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                }.Wearing("Hint");

            /// <summary>Push a cell to the right of its column, for a figure.</summary>
            /// <param name="cell">The cell.</param>
            private static TextBlock Right(TextBlock cell)
            {
                cell.TextAlignment = TextAlignment.Right;
                return cell;
            }

            /// <summary>A row and, when it is an open clash group, the clashes inside it.</summary>
            /// <param name="row">The row.</param>
            private UIElement Line(BoardRow row)
            {
                if (!row.IsGroup) return Clash(row.Only, row.Id, false);

                var stack = Ui.Stack(Group(row));

                if (_open.Contains(row.Id))
                    foreach (var member in row.Rows)
                        stack.Children.Add(Clash(member, Id(member), true));

                return stack;
            }

            /// <summary>
            /// A clash group: a real object in the test, and drawn as one.
            ///
            /// The group glyph appears here and nowhere else on the board. A grouping icon on a
            /// loose row would say the row is a group, which is the confusion this screen exists
            /// to remove.
            /// </summary>
            /// <param name="row">The group.</param>
            private UIElement Group(BoardRow row)
            {
                var grid = Cells();
                var on = _picked.Contains(row.Id);
                var open = _open.Contains(row.Id);

                Put(grid, Tick(row.Id, on), 0);

                var words = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };

                words.Children.Add(Ui.Glyph(open ? Glyphs.Down : Glyphs.Right, 13, "Muted",
                                            new Thickness(0, 0, 7, 0)));

                words.Children.Add(Ui.Glyph(Glyphs.Group, 15, "Accent", new Thickness(0, 0, 7, 0)));

                var name = new TextBlock
                {
                    Text = row.Name,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                };

                name.ToolTip = row.Name + " — double-click to rename it.";

                name.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount != 2) return;

                    e.Handled = true;

                    Prompt("Rename the clash group", row.Name, "Rename",
                           renamed => Regroup(row.Rows.Cast<object>().ToList(), renamed,
                                              "Renamed the clash group to"));
                };

                words.Children.Add(name.Wearing("Body"));

                if (_made.Contains(row.Id)) words.Children.Add(Parts.Note("grouped by you"));

                words.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    if (!_open.Remove(row.Id)) _open.Add(row.Id);
                    RedrawRows();
                });

                Put(grid, words, 1);

                // Em-dashes, not the first member's items. A group aggregates many different item
                // pairs and has no pair of its own; borrowing one member's would be a lie the eye
                // reads as fact.
                Put(grid, Right(Hint("—")), 2);
                Put(grid, Right(Hint("—")), 3);
                Put(grid, Figure(Skin.Num(row.Rows.Count), false), 4);
                Put(grid, Figure(Deepest(row), Bad(row)), 5);

                for (var i = 0; i < _columns.Count; i++)
                    Put(grid, Hint(Custom(row.Only, _columns[i])), 6 + i);

                return Finish(grid, row, on, false);
            }

            /// <summary>One clash: loose on the board, or a member of an open group.</summary>
            /// <param name="result">The result.</param>
            /// <param name="id">What picking it keys off.</param>
            /// <param name="inside">True when it is drawn under the group it belongs to.</param>
            private UIElement Clash(Row result, string id, bool inside)
            {
                var grid = Cells();
                var on = _picked.Contains(id);

                Put(grid, Tick(id, on), 0);

                var name = new TextBlock
                {
                    Text = result.Info.Name ?? string.Empty,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(inside ? 22 : 0, 0, 8, 0),
                };

                name.Wearing("Mono");
                name.Tint(TextBlock.ForegroundProperty, "Faint");
                name.ToolTip = "Double-click to rename it.";

                name.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount != 2) return;

                    e.Handled = true;

                    Prompt("Rename this clash", result.Info.Name ?? string.Empty, "Rename",
                           renamed => Renumber(new List<Row> { result }, renamed));
                };

                Grid.SetColumn(name, 1);
                grid.Children.Add(name);

                var itemA = result.Info.NameA ?? string.Empty;
                var itemB = result.Info.NameB ?? string.Empty;

                Put(grid, Item(itemA), 2);
                Put(grid, Item(itemB), 3);

                // A loose clash is not a count of anything, so the column that counts is empty
                // rather than showing one.
                Put(grid, Right(Hint("—")), 4);
                Put(grid, Figure(Millimetres(result), Bad(result)), 5);

                for (var i = 0; i < _columns.Count; i++)
                    Put(grid, Hint(Custom(result, _columns[i])), 6 + i);

                var wrapper = new BoardRow(id, result.Info.Name ?? "this clash", false,
                                           new List<Row> { result });

                return Finish(grid, wrapper, on, inside);
            }

            /// <summary>
            /// The three columns every row ends with: status, assignee and the verbs.
            ///
            /// Shared, because a group row and a clash row differ in what they are and not in what
            /// can be done to them — and a board where the same verb sits in two places on two
            /// kinds of row is a board somebody has to look at twice.
            /// </summary>
            /// <param name="grid">The row so far.</param>
            /// <param name="row">What the row is about.</param>
            /// <param name="on">Whether it is picked.</param>
            /// <param name="inside">True when it is a member of an open group.</param>
            private UIElement Finish(Grid grid, BoardRow row, bool on, bool inside)
            {
                Put(grid, Parts.InlineChip(row.Status, Statuses,
                        value => SetStatus(row, value), Reads(row.Status), "Set status"),
                    StatusColumn);

                Put(grid, Parts.InlineChip(row.Assigned, Assignees(Entry()),
                        value => SetAssignee(row, value), Tone.Plain, "Assign"),
                    StatusColumn + 1);

                var verbs = Verbs(row);
                verbs.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                Put(grid, verbs, StatusColumn + 2);

                // No gutter on the last column. There is nothing to its right to be kept away
                // from, and the eight pixels were taken off the first symbol in the row.
                verbs.Margin = new Thickness(0);

                // Two borders, because the tone bar down the left and the hairline underneath
                // are different colours and one Border has one brush. Drawn as one they were the
                // same colour, which made every New row's underline red and every Active row's
                // disappear.
                var tone = new Border
                {
                    Padding = new Thickness(12, inside ? 7 : 9, 12, inside ? 7 : 9),
                    BorderThickness = new Thickness(inside ? 0 : 3, 0, 0, 0),
                    BorderBrush = inside
                        ? System.Windows.Media.Brushes.Transparent
                        : Edge(row.Status),
                    Child = grid,
                };

                var line = new Border
                {
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = tone,
                };

                line.Tint(Border.BorderBrushProperty, "Bd");

                void Paint()
                {
                    if (on) line.Tint(Border.BackgroundProperty, "NavActive");
                    else if (inside) line.Tint(Border.BackgroundProperty, "Card2");
                    else line.Background = System.Windows.Media.Brushes.Transparent;
                }

                Paint();

                // The right button offers exactly what the symbols offer, off the same list. When
                // the row is one of several picked it offers what the bar offers instead —
                // right-clicking inside a selection and being handed a menu about one row is how
                // somebody regroups the wrong thing.
                line.ContextMenu = Parts.Context(
                    () => _picked.Count > 1 && _picked.Contains(row.Id)
                        ? PickedMenu(Picked())
                        : RowMenu(row),
                    () =>
                    {
                        // Right-clicking a row outside the selection moves the selection to it,
                        // which is what every table in Windows does.
                        if (_picked.Count == 0 || _picked.Contains(row.Id)) return;

                        _picked.Clear();
                        _pickedFrom = null;
                        _picked.Add(row.Id);
                        RedrawRows();
                    });

                // Dragging a row onto a group puts it in that group.
                //
                // Moving a result into a group meant typing the group's name into a box, which is
                // a coordinator reading a name off one row so they can spell it at another. The
                // rows and the groups are both on the screen; the gesture everybody already knows
                // is to drag one onto the other.
                line.MouseMove += (s, e) => Ui.Safely(() =>
                {
                    if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                    if (_carrying != null) return;

                    var at = e.GetPosition(line);

                    // A threshold, or every click that wobbles a pixel becomes a drag and the
                    // row can no longer simply be pressed.
                    if (Math.Abs(at.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance
                        && Math.Abs(at.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance)
                        return;

                    // What is dragged is the whole selection when this row is part of it, and
                    // this row alone otherwise - the same rule the right-click menu follows.
                    _carrying = _picked.Contains(row.Id) && _picked.Count > 1
                        ? Picked()
                        : row.Rows.ToList();

                    try
                    {
                        DragDrop.DoDragDrop(line, new DataObject(CarryFormat, row.Id),
                                            DragDropEffects.Move);
                    }
                    finally
                    {
                        _carrying = null;
                    }
                });

                line.MouseLeftButtonDown += (s, e) => _pressAt = e.GetPosition(line);

                if (row.IsGroup)
                {
                    line.AllowDrop = true;

                    line.DragOver += (s, e) => Ui.Safely(() =>
                    {
                        var take = _carrying != null && e.Data.GetDataPresent(CarryFormat);

                        e.Effects = take ? DragDropEffects.Move : DragDropEffects.None;
                        e.Handled = true;

                        if (take) line.Tint(Border.BorderBrushProperty, "Accent");
                    });

                    line.DragLeave += (s, e) => Ui.Safely(Paint);

                    line.Drop += (s, e) => Ui.Safely(() =>
                    {
                        e.Handled = true;

                        var carried = _carrying;
                        if (carried == null || carried.Count == 0) return;

                        // A group dropped on itself is not a move.
                        if (carried.All(r => string.Equals(r.Group, row.Name,
                                                           StringComparison.Ordinal)))
                            return;

                        Regroup(carried.Cast<object>().ToList(), row.Name, "Moved {0} into");
                    });
                }

                // Clicking a row picks it.
                //
                // The board had a tick box, a hover tint and a right-click, and no left-click at
                // all - so pressing a row did nothing and left nothing behind, and the row you
                // had just pressed was indistinguishable from the one above it. Every table
                // anybody uses selects on click; this one did not.
                line.Cursor = System.Windows.Input.Cursors.Hand;

                line.MouseLeftButtonUp += (s, e) => Ui.Safely(() => Clicked(row.Id));

                // Hidden rather than greyed, and revealed on hover. A permanent row of off verbs
                // is the wall of grey this screen was rebuilt to be rid of.
                line.MouseEnter += (s, e) =>
                {
                    verbs.Visibility = Visibility.Visible;
                    if (!on) line.Tint(Border.BackgroundProperty, "Hover");
                };

                line.MouseLeave += (s, e) =>
                {
                    if (!on) verbs.Visibility = Visibility.Hidden;
                    Paint();
                };

                return line;
            }

            /// <summary>
            /// What one press on a row does to the selection.
            ///
            /// The three gestures every table in Windows has, and this one is no different:
            ///
            /// <list type="bullet">
            /// <item><description>A plain press picks that row and nothing else, and becomes the
            /// place a run is measured from.</description></item>
            /// <item><description>Ctrl adds one row, or takes it back out, and moves the place a
            /// run is measured from to it.</description></item>
            /// <item><description>Shift picks the whole run between there and here.</description>
            /// </item>
            /// </list>
            ///
            /// <b>The run is what is on the screen</b>, in the order it is on the screen - the
            /// board's own flattened lines, so a run across an opened clash group takes the group
            /// and the members it is showing, and a run over a closed one takes the group alone.
            /// Anything else would be a run through rows nobody can see.
            /// </summary>
            /// <param name="id">The row that was pressed.</param>
            private void Clicked(string id)
            {
                var keys = System.Windows.Input.Keyboard.Modifiers;
                var adding = (keys & System.Windows.Input.ModifierKeys.Control) != 0;
                var run = (keys & System.Windows.Input.ModifierKeys.Shift) != 0;

                if (run && _pickedFrom != null)
                {
                    var between = Between(_pickedFrom, id);

                    if (between.Count > 0)
                    {
                        // Shift replaces what is picked with the run, as it does everywhere else.
                        // Ctrl with it adds the run to what is already picked, which is how
                        // somebody builds a selection out of two stretches of a long board.
                        if (!adding) _picked.Clear();

                        foreach (var one in between) _picked.Add(one);

                        RedrawRows();
                        return;
                    }
                }

                if (!adding) _picked.Clear();

                if (!_picked.Add(id) && adding) _picked.Remove(id);

                // Where the next run is measured from. Not moved by a Shift press, so a run can
                // be stretched and shrunk from the same end rather than walking away from it.
                _pickedFrom = id;

                RedrawRows();

                // Last, and only on a plain press of one row. A run or a Ctrl-press is somebody
                // building a selection, not asking to be taken somewhere.
                if (_focusOnClick && !adding && !run) Look(id);
            }

            /// <summary>Put the camera on one picked row, for focus-on-click.</summary>
            /// <param name="id">The row.</param>
            private void Look(string id)
            {
                var line = _lines?.FirstOrDefault(
                    l => string.Equals(l.Id, id, StringComparison.Ordinal));

                var rows = line?.GroupRow?.Rows
                           ?? (line?.Clash != null ? new List<Row> { line.Clash } : null);

                if (rows != null && rows.Count > 0) Focus(rows.Cast<object>().ToList(), _toast);
            }

            /// <summary>
            /// Every pickable line from one row to another, as the board is showing them.
            /// </summary>
            /// <param name="from">One end.</param>
            /// <param name="to">The other.</param>
            /// <returns>The ids in between, both ends included, or empty when either is not shown.</returns>
            private List<string> Between(string from, string to)
            {
                var lines = _lines;

                if (lines == null) return new List<string>();

                var shown = lines.Where(l => l.Id.Length > 0).Select(l => l.Id).ToList();

                var one = shown.IndexOf(from);
                var other = shown.IndexOf(to);

                // A run measured from a row that has since been filtered away, or collapsed into
                // a group, has no ends to measure between. The press falls through to picking the
                // one row, which is the honest answer rather than a guessed run.
                if (one < 0 || other < 0) return new List<string>();

                if (one > other) (one, other) = (other, one);

                return shown.GetRange(one, other - one + 1);
            }

            /// <summary>
            /// The row a run of them is measured from, or null before anything has been pressed.
            /// </summary>
            private string? _pickedFrom;

            /// <summary>The three verbs on a row, and the menu holding the rest.</summary>
            /// <param name="row">What the verbs act on.</param>
            private FrameworkElement Verbs(BoardRow row)
            {
                var reaching = Reaching(row);
                var many = reaching.Count > row.Rows.Count;

                var what = many
                    ? Ui.Count(_picked.Count, "picked row")
                    : row.IsGroup ? "this clash group" : "this clash";

                var picked = reaching;

                var bar = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };

                bar.Children.Add(Ui.Icon(Glyphs.Focus, "Focus " + what,
                    () => Focus(picked, _toast)));

                bar.Children.Add(Ui.Icon(Glyphs.Eye, "Isolate " + what,
                    () => Isolate(picked, _toast)));

                // One clash opens its conversation; a group opens a box that says the same thing
                // on all of its members, because a group has no trail of its own.
                bar.Children.Add(row.IsGroup || row.Rows.Count != 1
                    ? Ui.Icon(Glyphs.Comment, "Comment on " + what,
                        () => Prompt("Comment on " + what, string.Empty, "Comment",
                                     body => CommentPicked(picked, body, _toast)))
                    : Ui.Icon(Glyphs.Comment, "Read and write the comments on this clash",
                        () => Conversation(row.Rows[0])));

                bar.Children.Add(Parts.MoreMenu(Glyphs.More, "Everything else for " + what,
                                                RowMenu(row)));

                // Ui.Icon spaces symbols for a row of twelve, where a striped block of them is
                // the thing to avoid. There are four here, and the six pixels a side were coming
                // out of the clash group's name.
                foreach (var verb in bar.Children.OfType<FrameworkElement>())
                    verb.Margin = new Thickness(2, 2, 2, 2);

                return bar;
            }

            /// <summary>
            /// Everything one row offers, behind its own symbol and on its right button alike.
            ///
            /// The same list the bar for picked rows builds, so one row and forty of them offer
            /// the same verbs. What differs is only the grouping one, which depends on what this
            /// row already is.
            /// </summary>
            /// <param name="row">The row.</param>
            private List<Parts.Entry> RowMenu(BoardRow row)
            {
                var reaching = Reaching(row);

                var what = _picked.Count > 1 && _picked.Contains(row.Id)
                    ? Ui.Count(_picked.Count, "picked row")
                    : row.IsGroup ? "this clash group" : "this clash";

                var picked = reaching;
                var rest = Doing(Rows(reaching), what);

                rest.Add(Parts.Entry.Rule());

                if (row.IsGroup)
                {
                    rest.Add(Parts.Entry.Act("Rename the clash group", Glyphs.Pencil,
                        () => Prompt("Rename the clash group", row.Name, "Rename",
                                     name => Regroup(picked, name, "Renamed the clash group to"))));

                    rest.Add(Parts.Entry.Act("Dissolve the group", Glyphs.Group,
                        () => Regroup(picked, string.Empty,
                                      "Dissolved the clash group and put {0} back loose.")));
                }
                else if (row.Only.Group.Length > 0)
                {
                    rest.Add(Parts.Entry.Act("Remove from the group", Glyphs.MoveIn,
                        () => Regroup(picked, string.Empty,
                                      "Took {0} out of their clash group.")));
                }
                else
                {
                    rest.Add(Parts.Entry.Act("Group with the picked rows", Glyphs.Group,
                        () => Prompt("Name the clash group", Suggested(), "Group",
                                     name => Regroup(WithPicked(picked), name, "Grouped {0} into"))));
                }

                if (!row.IsGroup)
                {
                    // One clash is rarely one problem. A duct that hits a beam usually hits the
                    // three beside it and the hanger under them, and answering "what else is this
                    // element in" meant reading the board with an element name held in the head.
                    rest.Add(Parts.Entry.Rule());

                    rest.Add(Parts.Entry.Act("Find other clashes with these items", Glyphs.Find,
                                             () => SameItems(row.Only)));
                }

                return rest;
            }

            /// <summary>
            /// Narrow the board to every clash touching either of one clash's two items.
            ///
            /// The other narrowings are lifted when this goes on, because the question being
            /// asked is "what else is this element in" and the answer has to be all of it. A
            /// status filter left over from a minute ago would quietly answer a different
            /// question, and the row that proved the point would be the one it hid.
            /// </summary>
            /// <param name="origin">The clash whose items to look for.</param>
            private void SameItems(Row origin)
            {
                _sameAs = origin;
                _status = null;
                _party = null;
                _showing = BothKinds;
                _boardBox.Text = string.Empty;
                _picked.Clear();
                _pickedFrom = null;

                RedrawBoard();

                var entry = Entry();
                if (entry == null) return;
            }

            /// <summary>
            /// One rail row's tick, so a verb can be aimed at several tests at once.
            ///
            /// Ticking is not picking: the board still follows the row you click, and the ticks
            /// only say which tests a verb reaches. Untick everything and every verb is back to
            /// meaning the one test on the board.
            /// </summary>
            /// <param name="entry">The test the tick stands for.</param>
            private UIElement TestTick(TestEntry entry)
            {
                var box = new CheckBox
                {
                    IsChecked = _tests.Contains(entry.Test.Id),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 9, 0),
                    ToolTip = "Tick tests to run, move or export several at once",
                };

                box.Click += (s, e) => Ui.Safely(() =>
                {
                    if (!_tests.Remove(entry.Test.Id)) _tests.Add(entry.Test.Id);
                    RedrawRailList();
                });

                return box;
            }

            /// <summary>One row's tick.</summary>
            /// <param name="id">What picking it keys off.</param>
            /// <param name="on">Whether it is picked.</param>
            private UIElement Tick(string id, bool on)
            {
                var box = new CheckBox
                {
                    IsChecked = on,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                box.Click += (s, e) => Ui.Safely(() =>
                {
                    if (!_picked.Remove(id)) _picked.Add(id);

                    // The row that was ticked, the header's tri-state, and the bar that counts
                    // what is picked. Nothing else on the board changed, and rebuilding all of it
                    // to shade one line is what made a tick on a long board feel like a hang.
                    Repaint(id);
                });

                return box;
            }

            /// <summary>One of a clash's two items, as the host named it.</summary>
            /// <param name="name">The item's name.</param>
            private static TextBlock Item(string name) =>
                new TextBlock
                {
                    Text = name,
                    ToolTip = name.Length == 0 ? null : name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                }.Wearing("Lbl");

            /// <summary>A right-aligned figure, red once it is past this test's tolerance.</summary>
            /// <param name="text">The figure, already formatted.</param>
            /// <param name="bad">Whether it is past tolerance.</param>
            private static TextBlock Figure(string text, bool bad)
            {
                var cell = new TextBlock
                {
                    Text = text,
                    TextAlignment = TextAlignment.Right,
                };

                cell.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty,
                              FontNumeralAlignment.Tabular);

                cell.Wearing("Body");
                cell.Tint(TextBlock.ForegroundProperty, bad ? "RedTx" : "Muted");

                return cell;
            }

            // -------------------------------------------------------------------------------
            // Region 3 — the bar for what is picked
            // -------------------------------------------------------------------------------

            /// <summary>Draw the selection bar, or take it away when nothing is picked.</summary>
            private void RedrawSelection()
            {
                if (_picked.Count == 0)
                {
                    _bar.Child = null;
                    return;
                }

                var rows = Picked();
                var picked = rows.Cast<object>().ToList();

                _bar.Child = Parts.SelectionBar(_picked.Count, "row",
                    () => { _picked.Clear(); _pickedFrom = null; RedrawRows(); },
                    Parts.InlineChip(null, Statuses,
                        value => SetMany(picked, value, true), Tone.Plain, "Set status"),
                    Parts.InlineChip(null, Assignees(Entry()),
                        value => SetMany(picked, value, false), Tone.Plain, "Assign to"),
                    Grouping(rows),
                    Parts.Verb("Raise as issues", Glyphs.Issue, false,
                               () => RaiseIssue(picked, false)),
                    Parts.MoreMenu(Glyphs.More, "Everything else for the picked rows",
                                   PickedMenu(rows), up: true));
            }

            /// <summary>
            /// The one grouping verb the picked rows actually have.
            ///
            /// There is exactly one sensible answer at any moment, and the bar offers that one
            /// rather than a Group button that is wrong four times out of five and says so only
            /// after it has been pressed. Loose rows can become a group; rows already in one can
            /// leave it; a whole group can be dissolved; two or more can be merged.
            /// </summary>
            /// <param name="rows">The picked results.</param>
            private UIElement Grouping(List<Row> rows)
            {
                var verb = GroupVerb(rows);

                // Always the same weight. Which grouping verb is offered depends on what is
                // picked, and should - but it was also switching between a primary and an
                // ordinary button, so the bar changed size and colour between one row picked and
                // two. The bar is furniture; only its words should move.
                return Parts.Verb(verb.Text, verb.Glyph ?? Glyphs.Group, primary: false,
                                  () => Ui.Safely(verb.OnAct));
            }

            /// <summary>
            /// The one grouping verb the picked rows have, as a menu line.
            ///
            /// <b>All four are the same host write.</b> Navisworks has no merge, no dissolve and
            /// no rename for a clash group — it has "put these results in a group called X", and
            /// an empty name takes them out of one. Grouping, ungrouping, moving, merging and
            /// renaming are all that call with a different name, which is why every one of them
            /// lands in the document rather than only on this screen.
            ///
            /// <see cref="Parts.Entry.IsDanger"/> is doing duty for "this is the primary" here,
            /// which the button reads and the menu ignores.
            /// </summary>
            /// <param name="rows">The picked results.</param>
            private Parts.Entry GroupVerb(List<Row> rows)
            {
                var groups = rows.Where(r => r.Group.Length > 0)
                                 .Select(r => r.Group)
                                 .Distinct(StringComparer.Ordinal)
                                 .ToList();

                var loose = LooseIn(rows);
                var picked = rows.Cast<object>().ToList();
                var inside = rows.Where(r => r.Group.Length > 0).Cast<object>().ToList();

                // Whole groups and nothing else: dissolve the one, or merge the several.
                if (loose.Count == 0 && groups.Count > 0 && Whole(rows, groups))
                {
                    if (groups.Count == 1)
                        return Parts.Entry.Act("Dissolve the group", Glyphs.Group,
                            () => Regroup(picked, string.Empty,
                                          "Dissolved the clash group and put {0} back loose."));

                    return Parts.Entry.Danger("Merge the groups", Glyphs.Group,
                        () => Prompt("Name the merged clash group", groups[0], "Merge",
                                     name => Regroup(picked, name,
                                                     "Merged " + groups.Count + " clash groups, "
                                                     + "putting {0} into")));
                }

                // Anything already in a group, and the way out of it is the verb that matters.
                if (groups.Count > 0)
                    return Parts.Entry.Act("Remove from the group", Glyphs.MoveIn,
                        () => Regroup(inside, string.Empty, "Took {0} out of their clash group."));

                if (loose.Count > 1)
                    return Parts.Entry.Danger("Group", Glyphs.Group,
                        () => Prompt("Name the clash group", Suggested(), "Group",
                                     name => Regroup(picked, name, "Grouped {0} into")));

                return Parts.Entry.Barred("Group", Glyphs.Group,
                    "Grouping takes two or more loose results. One on its own is already as "
                    + "grouped as it can be.");
            }

            /// <summary>Everything the picked rows can be told to do, grouping included.</summary>
            /// <param name="rows">The picked results.</param>
            private List<Parts.Entry> PickedMenu(List<Row> rows)
            {
                var menu = Doing(rows, "the picked rows");

                menu.Add(Parts.Entry.Rule());
                menu.Add(GroupVerb(rows));

                return menu;
            }

            /// <summary>Whether every result of every named group is picked.</summary>
            /// <param name="rows">The picked results.</param>
            /// <param name="groups">The groups they belong to.</param>
            private bool Whole(List<Row> rows, List<string> groups)
            {
                var entry = Entry();
                if (entry == null) return false;

                var have = new HashSet<string>(rows.Select(Id), StringComparer.Ordinal);

                foreach (var group in groups)
                    foreach (var row in entry.Rows)
                        if (string.Equals(row.Group, group, StringComparison.Ordinal)
                            && !have.Contains(Id(row)))
                            return false;

                return true;
            }

            /// <summary>
            /// Everything that can be done to results, whether it is one of them or forty.
            ///
            /// One list, built once, so the menu on a row and the menu on the bar cannot drift
            /// apart: picking three clashes has to offer what picking one of them offers, and the
            /// two menus were written separately once and immediately disagreed.
            /// </summary>
            /// <param name="rows">The results to act on.</param>
            /// <param name="what">What they are called, for the captions.</param>
            private List<Parts.Entry> Doing(List<Row> rows, string what)
            {
                var picked = rows.Cast<object>().ToList();

                return new List<Parts.Entry>
                {
                    // Grouped by what the line does to the world, in the order somebody works:
                    // look at it, say something about it, file it, send it somewhere.
                    Parts.Entry.Heading("Look at " + what),
                    Parts.Entry.Act("Focus", Glyphs.Focus, () => Focus(picked, _toast)),
                    Parts.Entry.Act("Isolate", Glyphs.Eye, () => Isolate(picked, _toast)),
                    Parts.Entry.Act(MarkerToggle(), Glyphs.EyeOff, () =>
                    {
                        Unmark(_toast);
                        RedrawMarkerToggle();
                    }),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Decide"),
                    rows.Count == 1
                        ? Parts.Entry.Act("Comments", Glyphs.Comment, () => Conversation(rows[0]))
                        : Parts.Entry.Act("Comment on all of them", Glyphs.Comment,
                            () => Prompt("Comment on " + what, string.Empty, "Comment",
                                         body => CommentPicked(picked, body, _toast))),
                    Parts.Entry.Act("Raise as an issue", Glyphs.Issue,
                                    () => RaiseIssue(picked, false)),
                    Parts.Entry.Act("Attach to an existing issue", Glyphs.Link,
                                    () => AttachToIssue(picked, _toast)),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("File"),
                    Parts.Entry.Act("Move into an existing group", Glyphs.MoveIn,
                        () => Prompt("Move into which clash group", string.Empty, "Move",
                                     name => Regroup(picked, name, "Moved {0} into"))),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Export"),
                    Parts.Entry.Act("Export a snapshot of each", Glyphs.Camera,
                                    () => Snapshots(picked, _toast)),
                    Parts.Entry.Act("Marker spheres as .obj", Glyphs.Sphere,
                                    () => ExportMarkers(rows, what, _toast)),
                    Parts.Entry.Act("Copy as columns", Glyphs.Copy, () => CopyRows(rows)),
                };
            }

            // -------------------------------------------------------------------------------
            // Region 4 — the footer
            // -------------------------------------------------------------------------------

            private readonly Border _send = new Border { VerticalAlignment = VerticalAlignment.Center };

            /// <summary>The state of the week on the left, and the two ways out on the right.</summary>
            /// <param name="read">The read runner, which carries its own progress and Stop.</param>
            /// <summary>
            /// The foot: what the read found on the left, what to do next on the right, on one
            /// line.
            ///
            /// <b>Why the runner arrives in pieces.</b> A runner stacked whole is a button with a
            /// progress bar and a status line growing underneath it. Dropped into a row of
            /// ordinary buttons that made the row three lines tall, left Export and Save centred
            /// against a bar that was not there yet, and moved both of them the moment a read
            /// started. So the buttons join the row and the progress goes down the left, under the
            /// counts it is about, where it can appear and disappear without shifting anything.
            /// </summary>
            /// <param name="read">The read button and its progress, apart.</param>
            private UIElement Footer(Ui.RunPair read)
            {
                var row = new DockPanel { LastChildFill = true };

                var right = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                read.Buttons.VerticalAlignment = VerticalAlignment.Center;
                read.Buttons.Margin = new Thickness(0, 0, 8, 0);

                // Kept, because whether it is shown changes with every read - and the footer is
                // built once.
                _readButtons = read.Buttons;

                right.Children.Add(Ui.Loose(read.Buttons));

                _send.VerticalAlignment = VerticalAlignment.Center;
                right.Children.Add(Ui.Loose(_send));

                _saveButton = Ui.Primary("Save the changes", () =>
                {
                    if (OnFile) SaveMarkup();
                    else SaveWork();
                });
                _saveButton.Margin = new Thickness(8, 0, 0, 0);
                _saveButton.VerticalAlignment = VerticalAlignment.Center;
                right.Children.Add(Ui.Loose(_saveButton));

                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);

                // The counts and the bar, together, filling what the buttons leave. Centred
                // against them so a one-line foot and a three-line one both read as one row.
                var left = Ui.Stack(_log, read.Progress);

                left.VerticalAlignment = VerticalAlignment.Center;
                left.Margin = new Thickness(0, 0, 16, 0);

                // The runner puts eight pixels above its progress for the stacked case, where it
                // separates the bar from the button. Here there is nothing above it to separate
                // from, and the gap is the foot growing for no reason.
                read.Progress.Margin = new Thickness(0);

                row.Children.Add(left);

                return row;
            }

            /// <summary>
            /// The footer's facts, every one of them derived.
            ///
            /// Derived and not typed: a footer claiming a run the rail contradicts is worse than a
            /// footer that says less. The design's revision line is not here for that reason —
            /// nothing in a Navisworks document carries an issued revision, and inventing one is
            /// the kind of number somebody reads out in a meeting.
            /// </summary>
            private void RedrawFoot()
            {
                // Only before the first read. Once the results are on the screen the press that
                // put them there is the least useful thing in the footer, and "Read the results
                // again" is on the test's own menu, where somebody who wants it is looking.
                if (_readButtons != null)
                    _readButtons.Visibility = Current() == null
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                // Nothing on the left. What used to be there was four totals over every test in
                // the document - how many were assigned out, how many were in groups - and a
                // total across eighteen tests is a number nobody is working on. What is worth
                // knowing is about the test on the board, and it belongs under the list of tests
                // rather than in the far corner of the window from it.
                _send.Child = Parts.MenuButton("Export", Glyphs.Onward, Sending());
            }

            /// <summary>The ways the board leaves the product, with the one that cannot say why.</summary>
            private List<Parts.Entry> Sending()
            {
                var why = WhySync();

                var lines = new List<Parts.Entry?>
                {
                    // The sync is the footer's primary, not a line in here — but when the
                    // document will not take one, the reason belongs where somebody goes looking
                    // for the way out, and not only in a tooltip on a button that looks broken.
                    why == null
                        ? null
                        : Parts.Entry.Barred("Sync into the document", Glyphs.Save, why),
                    Parts.Entry.Saying(OnFile ? "Save the markup" : "Save as a clash file (BCF)",
                        Glyphs.Round, "The offline review round — out and back", SaveChanges),
                    Parts.Entry.Barred("Write a report", Glyphs.Report,
                        "Written on Clash Manager - Report, over this same board."),
                    Parts.Entry.Saying("The board to a spreadsheet", Glyphs.Table,
                        "Exactly as it is filtered, columns and all", Sheet),
                    Parts.Entry.Saying("Set up the review round", Glyphs.Round,
                        "What a baseline carries, and where a markup comes back to",
                        () => Configure("round")),
                    Parts.Entry.Rule(),
                    Parts.Entry.Saying("Raise the board as issues", Glyphs.Issue,
                        "Into the Issue Manager's register",
                        () => RaiseIssue(_shown.Cast<object>().ToList(), false)),
                };

                return lines.Where(l => l != null).Select(l => l!).ToList();
            }

            // -------------------------------------------------------------------------------
            // Reading
            // -------------------------------------------------------------------------------

            private void Read(Job job)
            {
                if (OnFile)
                {
                    ReadFile(job);

                    // Only a file board. Its rows arrived as element keys with no engine behind
                    // them, so Focus and Isolate have nothing to reach for until the federation
                    // is indexed - and this is the job with a progress bar and a Stop.
                    //
                    // A board read from the model never needs it. The engine is holding both
                    // elements of every result, so every verb on this screen goes to it by id.
                    // Building an index there was a walk of the whole federation for a table
                    // nothing on the board ever consults, on the one press where the user is
                    // waiting to see their clashes.
                    var board = Current();

                    if (board != null)
                        Ui.Ready(job, board.SelectMany(e => e.Rows)
                                           .SelectMany(r => new[] { r.Info.A, r.Info.B }));

                    return;
                }

                ReadModel(job);
            }

            /// <summary>
            /// Pull every test and every result out of the document, in one walk.
            ///
            /// <b>What this used to be.</b> A loop over the tests calling Results and Sides for
            /// each, both of which looked their test up by walking the whole tree of tests - so
            /// the read was quadratic in how many tests a job has, and Tests() had already
            /// materialised every result in the document once just to count them. Describing what
            /// each test was between compared its two sides against every saved set in the
            /// document, building a selection source per comparison.
            ///
            /// Nothing else on this press is allowed to grow either. This is the moment somebody
            /// is waiting to see their clashes.
            /// </summary>
            private void ReadModel(Job job)
            {
                var carried = Harvest(_read);
                var entries = new List<TestEntry>();

                foreach (var read in _session.Clash.ReadEverything(
                             seen => job.Step(seen, "results read")))
                {
                    var entry = new TestEntry(read.Test) { Shape = read.Shape };

                    foreach (var result in read.Results)
                        entry.Rows.Add(Restored(new Row(read.Test.Id, read.Test.Name, result),
                                                carried));

                    entries.Add(entry);
                }

                if (job.IsCancelled) return;

                _read = entries;

                // This project's own names over the host's, before anything is drawn.
                Renamed(entries);

                Moved();

                var kept = Keep(entries, job);

                Redraw();

                // One line. It was three - the counts, what was kept, and how long it took -
                // stacked in a footer whose height is results nobody can see.
                _log.Children.Add(Ui.Line(
                    Ui.Count(entries.Count, "test") + ", "
                    + Ui.Count(entries.Sum(e => e.Rows.Count), "result") + " read."
                    + (kept == null ? string.Empty : "  " + kept), 1, true));
            }

            /// <summary>
            /// Put what this read found into the findings register, and keep it.
            ///
            /// <b>Why a read writes anything.</b> The register is what survives the session, the
            /// document being a published NWD, and the next run renumbering every result. A read
            /// is the observation that keeps it current: a clash already known keeps the decision
            /// somebody made and takes this run's fresh facts, and one this run no longer reports
            /// is marked absent rather than deleted.
            ///
            /// Scoped to the tests that were actually read, so reading three of twelve never
            /// closes the other nine.
            /// </summary>
            private string? Keep(IReadOnlyList<TestEntry> entries, Job job)
            {
                if (OnFile) return null;

                var read = new HashSet<string>(entries.Select(e => e.Test.Name),
                                               StringComparer.Ordinal);

                var found = new List<Finding>();

                foreach (var entry in entries)
                    found.AddRange(ClashFindings.ForTest(entry.Test.Id, entry.Test.Name,
                                                         entry.Rows.Select(r => r.Info)));

                job.Say("Keeping what was found...");

                var register = _session.Findings.Findings;

                var intake = register.Observe(
                    FindingSource.Clash, found, DateTime.UtcNow.Ticks,
                    origin: _session.Store.Path,
                    scope: f => read.Contains(f.Rule));

                Index(register);

                var carried = Carried(register);

                if (carried > 0) Index(register);

                foreach (var entry in entries)
                    foreach (var row in entry.Rows)
                        Apply(Found(row), row);

                var rejoined = Rejoin(entries);

                if (!_session.Findings.Save())
                    return "These results are on the board but could not be kept: "
                           + (_session.Findings.LastSaveProblem ?? "there was nowhere to write.");

                return intake.Explain()
                       + (rejoined > 0
                           ? " " + Ui.Count(rejoined, "issue") + " raised before findings existed "
                             + "now names its findings rather than its clash results."
                           : string.Empty)
                       + (carried > 0
                           ? " " + Ui.Count(carried, "clash") + " took back what an earlier "
                             + "version of this tool was keeping in the project file."
                           : string.Empty);
            }

            /// <summary>
            /// Build the way back from a row to its finding.
            ///
            /// Only findings this run reported: one marked absent points at a result the host no
            /// longer has, and an index entry for it would hand a row to the wrong clash the next
            /// time the engine reused the id.
            /// </summary>
            private static void Index(FindingRegister register)
            {
                var index = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);

                foreach (var finding in register.From(FindingSource.Clash))
                {
                    if (!finding.IsPresent) continue;

                    if (finding.Facts.TryGetValue(ClashFindings.HostTest, out var test)
                        && finding.Facts.TryGetValue(ClashFindings.HostId, out var result)
                        && test != null && result != null)
                        index[Where(test, result)] = finding;
                }

                _byHost = index;
            }

            /// <summary>
            /// Take back what the two old side registers were holding, once each run.
            ///
            /// Somebody who marked up a published NWD before findings existed has that work in
            /// the project file under keys nothing reads any more. Folding is idempotent, so this
            /// runs on every read and neither doubles anything nor deletes the old keys.
            /// </summary>
            private int Carried(FindingRegister register)
            {
                var section = _session.Store.Section(ProjectStore.ClashSection);

                var held = section[ClashLegacy.HeldKey];
                var asides = section[ClashLegacy.AsideKey];

                if (held.Kind != JsonKind.Object && asides.Kind != JsonKind.Object) return 0;

                return ClashLegacy.Fold(held, asides,
                                        register.From(FindingSource.Clash).Where(f => f.IsPresent));
            }

            /// <summary>
            /// Swap an older issue's clash keys for the finding ids this read just made.
            ///
            /// A clash key names two elements and a place but not the test that found them, so
            /// nothing can turn one into a finding id on its own - it takes a read, where both are
            /// in hand at once. Idempotent, so it runs on every read; an issue whose clash this
            /// run did not report keeps its key and waits for a wider one.
            /// </summary>
            private int Rejoin(IReadOnlyList<TestEntry> entries)
            {
                var byKey = new Dictionary<string, Finding>(StringComparer.Ordinal);

                foreach (var entry in entries)
                    foreach (var row in entry.Rows)
                    {
                        if (row.Info.A.IsEmpty || row.Info.B.IsEmpty) continue;

                        var finding = Found(row);

                        if (finding != null) byKey[row.Info.Key().ToString()] = finding;
                    }

                if (byKey.Count == 0) return 0;

                var register = IssueViews.Read(_session);

                var moved = IssueFindings.Carry(register,
                    key => byKey.TryGetValue(key.ToString(), out var finding) ? finding : null);

                // Not saving is not a failure worth stopping a read for: the issue still says
                // which clash it is about, and the next read will offer the swap again.
                if (moved > 0) IssueViews.Write(_session, register);

                return moved;
            }

            /// <summary>The finding a row is a view of, or null when this read kept none.</summary>
            private static Finding? Found(Row row) => Found(row.TestId, row.Info.Id);

            /// <summary>
            /// Make the finding a row has not got, so a decision on it has somewhere to live.
            ///
            /// The register is written by the read, so a row is normally already in it. The ones
            /// that are not are the awkward cases - a result the engine renumbered since the read,
            /// a board whose read could not save - and until now a decision on one of those was
            /// applied to the row on screen and kept nowhere. On a document that accepts the write
            /// that is invisible; on a published NWD, which accepts none of them, it is the whole
            /// decision gone at the next read.
            ///
            /// Raised into the same register the read writes, with the same facts on it, so the
            /// next read matches it to its result rather than making a second one.
            /// </summary>
            /// <param name="row">The row being edited.</param>
            /// <returns>The new finding, or null when the register would not take one.</returns>
            private Finding? Raised(Row row)
            {
                var entry = Current()?.FirstOrDefault(
                    e => string.Equals(e.Test.Id, row.TestId, StringComparison.OrdinalIgnoreCase));

                var made = ClashFindings.ForTest(row.TestId, entry?.Test.Name ?? row.TestName,
                                                 new[] { row.Info }).FirstOrDefault();

                if (made == null) return null;

                var register = _session.Findings.Findings;

                register.Observe(FindingSource.Clash, new[] { made }, DateTime.UtcNow.Ticks,
                                 origin: _session.Store.Path,
                                 scope: f => false);

                Index(register);

                return Found(row);
            }

            /// <summary>The finding behind one of the host's results, or null.</summary>
            private static Finding? Found(string? testId, string? resultId) =>
                _byHost != null && testId != null && resultId != null
                && _byHost.TryGetValue(Where(testId, resultId), out var finding)
                    ? finding
                    : null;

            // A unit separator: it cannot appear in a host id, so no pair of ids can collide with
            // another pair by running into each other.
            private static string Where(string test, string result) => test + (char)31 + result;

            /// <summary>Every clash finding the host has not taken everything from.</summary>
            private IEnumerable<Finding> Waiting =>
                _session.Findings.Findings.From(FindingSource.Clash).Where(f => f.IsHeld);

            /// <summary>How many clashes are waiting for the document.</summary>
            private int WaitingCount => Waiting.Count();

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

                if (page == null) return;

                Ui.Configure(page.Page.Name, Redraw, page.Page);
            }

            /// <summary>
            /// The two sections left, and the symbol on the board that opens each.
            ///
            /// There were six. Four of them set up things the board now does where they happen:
            /// the filters are the status strip and the chips, what the status and assign verbs
            /// write is the value picked on the chip itself, and the markers page had one field
            /// left once its prose moved onto the verb that uses it. What is left is the two the
            /// board's own header names — where the results come from, and which columns show.
            /// </summary>
            private IReadOnlyList<Section> Pages() => new[]
            {
                new Section("source", new Ui.Page("Source and tolerance", SourcePage)),
                new Section("columns", new Ui.Page("Columns", ColumnsPage)),
                new Section("round", new Ui.Page("The review round", RoundPage)),
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
                    : null,

                // The marker size, which had a settings page of its own with one field on it. A
                // page per field is how an action row ends up with six symbols, each opening a
                // window holding one control.
                ModelPage());

            /// <summary>
            /// Which columns the board shows, behind the board's own columns symbol.
            ///
            /// The nine columns the board draws are not configurable and are not meant to be: they
            /// are the clash, both its items, how bad it is and what has been decided about it,
            /// which is the whole of what a result is. What this page adds is the column the host
            /// does not have — a property read off the clashing elements themselves, so a board
            /// can carry the system name, the workset or the fire rating beside the decision.
            ///
            /// The grouping chain that used to live here is gone. It bucketed the results table by
            /// a chain of values, which is what "Arrange view by" on the board does now — in one
            /// press, on the rows themselves, and saying in the band that no clash group moved.
            /// </summary>
            private UIElement ColumnsPage()
            {
                RedrawColumnList();

                return Bench.Group("Property columns",
                    Ui.Field("Tab", Pickers.ClashTab(_newTab, pick => _newTab = pick)),
                    Ui.Field("Property", Pickers.ClashProperty(_newProp, pick => _newProp = pick)),
                    Ui.Field("Read from", Ui.Choice(new[] { BothSides, FirstSide, SecondSide },
                                                    _newSide, pick => _newSide = pick, 150)),
                    Ui.Actions(Ui.Icon(Glyphs.Add, "Add the column", AddColumn)),
                    _columnList,
                    Ui.Runner("Read values", _fill, primary: false, work: FillColumns),
                    _fill,
                    Ui.Small("A column belongs to the board, not to one test: it lands on every "
                             + "test's results, and the names offered above are what the clashing "
                             + "elements of every test actually carry. Each column reads that "
                             + "property off the clashing elements themselves, and lands "
                             + "between Worst and Status - "
                             + "where the eye is already reading facts about the elements rather "
                             + "than decisions about them."));
            }

            private UIElement ModelPage()
            {
                RedrawMarkerToggle();
                return ModelPageBody();
            }

            private UIElement ModelPageBody() => Ui.Stack(
                Bench.Group("Focus",
                    Ui.Field("Stand back", Ui.Text(
                        _focusBack.ToString(CultureInfo.InvariantCulture), text =>
                        {
                            if (double.TryParse(text, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var mm) && mm > 0)
                            {
                                _focusBack = mm;
                                Remember();
                            }
                        }, 90), "mm from the clash"),
                    Ui.Check("Focus the camera when a row is clicked", _focusOnClick,
                             v => { _focusOnClick = v; Remember(); }),
                    Ui.Small("With this off, clicking a row picks it and the camera stays where "
                             + "it is - Focus, on the row or in its menu, is what moves it.")),

                Bench.Group("Markers",
                    Ui.Field("How big", Ui.Text(
                        _markerAcross.ToString(CultureInfo.InvariantCulture), text =>
                        {
                            if (double.TryParse(text, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out var mm) && mm > 0)
                            {
                                _markerAcross = mm;
                                Remember();
                            }
                        }, 90), "mm across"),
                    Ui.Check("Drop one where the camera lands on Focus", _markOnFocus,
                             v => { _markOnFocus = v; Remember(); }),

                    // A switch, not a button whose words are its state. It was a text button that
                    // said "Hide the markers", and after pressing it the only thing telling you
                    // what it would do next was the same words.
                    Ui.Loose(_markerToggle),

                    Ui.Small("A marker is a status-coloured ball drawn over the model where the "
                             + "two elements met. It is not geometry: nothing is added to the "
                             + "document and nothing joins the selection tree. Export ▸ Marker "
                             + "spheres as .obj writes the same balls as a file, for somebody who "
                             + "has neither this plug-in nor Navisworks.")));

            private UIElement RoundPage() => OnFile
                ? Bench.Group("The review round",
                    Ui.Small("You are on the file leg: mark the results up and press \"Save "
                             + "markup\" on the Source page. A Manage seat reads it back."))
                : Ui.Stack(
                    Bench.Group("Send it out",
                        // What "Save the changes" writes, said where somebody deciding to send a
                        // file is looking. It used to sit two levels down behind a symbol and a
                        // text button, which is a strange place for a setting that changes what
                        // the foot's own button produces.
                        Ui.Check("Draw a picture of each clash into the baseline", _pictures,
                                 v => _pictures = v),
                        Ui.Small("Pictures are what every coordination tool shows its issue list "
                                 + "as, so a baseline without them arrives as rows of grey boxes. "
                                 + "They cost a view change and a render each, so a board of "
                                 + "thousands is worth starting before a break."),
                        Ui.Actions(Ui.Quiet("Export...", () =>
                        {
                            ClashExportWindow.Preferred = Entry() == null
                                ? new List<string>()
                                : new List<string> { Entry()!.Test.Id };
                            Ui.Told(Shell.AppWindow.Show("issues", "clashxml"));
                        }))),
                    Bench.Group("Bring it back",
                        Ui.PathField("Markups", _markups, PathKind.Open,
                                     ClashFile.Filter,
                                     "a markup file, or the baseline — every markup beside it is read"),
                        Ui.Runner("Sync markups", _sync, primary: false, work: Sync),
                        _sync,
                        Ui.Check("Raise topics I cannot place as issues", _raiseStrangers,
                                 v => _raiseStrangers = v),
                        Ui.Check("Note what this model has no field for, on the clash",
                                 _noteAsides, v => _noteAsides = v),
                        Ui.Small("Reviewers on Simulate open the exported file on this tab, mark "
                                 + "it up, and save. This reads their markups back and writes the "
                                 + "statuses, comments and assignments into the model's own clash "
                                 + "data."),
                        Ui.Small("A reviewer working in their own tool can also raise problems "
                                 + "this model has never heard of, and set a priority or a due "
                                 + "date a clash result cannot hold. The first goes to the Issue "
                                 + "Manager, which is where a problem with no clash behind it "
                                 + "belongs; the second becomes a comment on the clash, which is "
                                 + "the one field the host does have and which travels back out "
                                 + "on the next export.")));

            /// <summary>Whether topics with no clash behind them become issues. On by default.</summary>
            private static bool _raiseStrangers = true;

            /// <summary>
            /// Whether a reviewer's priority, due date, stage, retitle or tags become a comment on
            /// the clash. On by default: silently dropping what somebody set is the failure this
            /// round trip exists to avoid.
            /// </summary>
            private static bool _noteAsides = true;

            /// <summary>
            /// Whether a baseline carries a picture per clash.
            ///
            /// Off by default, and remembered with the bench: it is the right answer for the file
            /// somebody sends out and the wrong one for the twenty exports they do while setting
            /// the filters up.
            /// </summary>
            private static bool _pictures;

            /// <summary>The board's own message strip, over the table it is about.</summary>
            private readonly StackPanel _toast = Ui.Stack();

            /// <summary>This test's tolerance in millimetres, for what counts as bad on a row.</summary>
            private double _toleranceMm = 25;

            // -------------------------------------------------------------------------------
            // Drawing
            // -------------------------------------------------------------------------------

            /// <summary>Redraw the whole tab: the rail, the board and the footer's facts.</summary>
            private void Redraw()
            {
                // The standing headline is what the tab says before its first read. Once there is
                // something to look at it is in the way of it.
                if (Current() != null) _headline.Children.Clear();

                RedrawRail();
                RedrawBoard();
                RedrawFoot();
                ArmSync();
            }

            /// <summary>The test on the board, picking the first one when nothing is picked yet.</summary>
            private TestEntry? Entry()
            {
                var read = Current();
                if (read == null || read.Count == 0) return null;

                var found = read.FirstOrDefault(
                    e => string.Equals(e.Test.Id, _current, StringComparison.OrdinalIgnoreCase));

                if (found != null) return found;

                // A read that lands on an empty board is a read somebody has to click to see the
                // result of. The first row of the rail, in whatever order the rail is in, is the
                // one they were going to press.
                var first = Shown().FirstOrDefault() ?? read[0];
                _current = first.Test.Id;

                return first;
            }

            /// <summary>One test, as the list the test verbs take.</summary>
            /// <param name="entry">The test, or null when none is picked.</param>
            private static IReadOnlyList<object> One(TestEntry? entry) =>
                entry == null ? Array.Empty<object>() : new object[] { entry };

            /// <summary>
            /// What a verb pressed on one test actually acts on.
            ///
            /// The ticked tests when this one is among them, and this one alone otherwise - the
            /// same rule the board's rows follow. Running, moving, compacting and exporting are
            /// all things somebody wants over eighteen tests at once, and doing them one at a
            /// time is the afternoon this window exists to give back.
            /// </summary>
            /// <param name="entry">The test the verb was pressed on.</param>
            private IReadOnlyList<object> Reaching(TestEntry? entry) =>
                entry != null && _tests.Count > 1 && _tests.Contains(entry.Test.Id)
                    ? (Current() ?? new List<TestEntry>())
                        .Where(t => _tests.Contains(t.Test.Id)).Cast<object>().ToList()
                    : One(entry);

            /// <summary>Which tests are ticked on the rail.</summary>
            private readonly HashSet<string> _tests =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Every result of a test, whatever the board is filtered to.</summary>
            /// <param name="entry">The test.</param>
            private static List<Row> All(TestEntry entry) => entry.Rows;

            // -------------------------------------------------------------------------------
            // What the rail shows
            // -------------------------------------------------------------------------------

            /// <summary>The tests the rail's search, chips and sort leave.</summary>
            private List<TestEntry> Shown()
            {
                var read = Current();
                if (read == null) return new List<TestEntry>();

                var find = _railFind.Trim();

                var shown = read.Where(e =>
                    (find.Length == 0
                     || e.Test.Name.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0
                     || Sides(e).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0)
                    && (_byParty.Count == 0 || Trades(e).Any(d => _byParty.Contains(d)))
                    && (_byFolder.Count == 0 || _byFolder.Contains(e.Test.Folder ?? string.Empty))
                    && (!_openOnly || e.Of("New") + e.Of("Active") > 0)).ToList();

                switch (_sort)
                {
                    case MostOpen:
                        return shown.OrderByDescending(e => e.Of("New") + e.Of("Active")).ToList();

                    case ByName:
                        return shown.OrderBy(e => e.Test.Name, StringComparer.CurrentCulture).ToList();

                    case MostReviewed:
                        return shown.OrderByDescending(e => e.Of("Reviewed")).ToList();

                    case MostApproved:
                        return shown.OrderByDescending(e => e.Of("Approved")).ToList();

                    case MostClosed:
                        return shown.OrderByDescending(e => e.Of("Resolved")).ToList();

                    case LastRun:
                        return shown.OrderByDescending(e => e.Test.LastRunTicks).ToList();

                    default:
                        return shown.OrderByDescending(e => e.Of("New")).ToList();
                }
            }

            /// <summary>
            /// The two disciplines one test is between, as the host records them.
            ///
            /// <b>Where they come from.</b> A clash test's two sides are read once, with the
            /// results, through <c>IClashSource.Sides</c>. Each side is either a <b>saved
            /// selection set</b> — which on a real job is what carries the discipline, because
            /// the sets are called Mechanical, Structure, Fire — or, when the side is whole
            /// models rather than a set, the <b>model's own name</b>. Nothing is derived and
            /// nothing is parsed: a side that is neither of those returns nothing, and the test
            /// simply has no discipline to filter on.
            /// </summary>
            /// <param name="entry">The test.</param>
            private List<string> Trades(TestEntry entry)
            {
                var found = new List<string>();

                void Add(string? name)
                {
                    var trimmed = (name ?? string.Empty).Trim();

                    if (trimmed.Length > 0
                        && !found.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        found.Add(trimmed);
                }

                var shape = entry.Shape;

                if (shape != null)
                {
                    Add(shape.SetA ?? shape.ScopesA.FirstOrDefault());
                    Add(shape.SetB ?? shape.ScopesB.FirstOrDefault());
                }

                // Nothing else. A test built by hand out of an ad-hoc selection has no set and
                // no model behind either side, and it offers no discipline — it does not get one
                // guessed out of its name.
                //
                // That fallback was here, splitting "L03 — Mech v Struct" on " v ". It reads well
                // on tests named that way and invents a discipline on every test not named that
                // way, which is the same fault as inferring a level: a chip that hides tests for
                // a reason it cannot state. The rail says what the test records, or says nothing.
                return found;
            }

            /// <summary>Every discipline the read carries, for the rail's chips.</summary>
            /// <param name="read">Everything read, or null before the first read.</param>
            private List<string> Trades(List<TestEntry>? read)
            {
                var found = new List<string>();

                foreach (var entry in read ?? new List<TestEntry>())
                    foreach (var name in Trades(entry))
                        if (!found.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(name);

                found.Sort(StringComparer.CurrentCulture);

                return found;
            }

            /// <summary>Every clash folder the read carries, for the rail's chips.</summary>
            /// <param name="read">Everything read, or null before the first read.</param>
            private static List<string> Folders(List<TestEntry>? read)
            {
                var found = new List<string>();

                foreach (var entry in read ?? new List<TestEntry>())
                {
                    var folder = entry.Test.Folder ?? string.Empty;

                    if (folder.Length > 0
                        && !found.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        found.Add(folder);
                }

                found.Sort(StringComparer.CurrentCulture);

                return found;
            }

            /// <summary>What a test is between, as one phrase.</summary>
            /// <param name="entry">The test.</param>
            private string Sides(TestEntry entry)
            {
                var both = Trades(entry);

                // Not "everything against everything", which is a claim about the test. A test
                // whose sides are an ad-hoc selection records nothing this can read, and saying
                // so is the only honest line.
                return both.Count >= 2 ? both[0] + " v " + both[1]
                     : both.Count == 1 ? both[0] + " against a selection this test does not name"
                     : "two selections this test does not name";
            }

            /// <summary>This test's tolerance, in the units somebody set it in.</summary>
            /// <param name="entry">The test.</param>
            private string Tolerance(TestEntry entry)
            {
                var metres = entry.Shape?.ToleranceMetres;

                if (!metres.HasValue) return "the tolerance it was run at";

                var mm = Math.Abs(metres.Value) * 1000;

                return mm >= 1000
                    ? (mm / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " m"
                    : mm.ToString("0.##", CultureInfo.InvariantCulture) + " mm";
            }

            /// <summary>This test's tolerance in millimetres, or the usual 25 when it has none.</summary>
            /// <param name="entry">The test, or null.</param>
            private static double ToleranceMm(TestEntry? entry)
            {
                var metres = entry?.Shape?.ToleranceMetres;

                return metres.HasValue && Math.Abs(metres.Value) > 0
                    ? Math.Abs(metres.Value) * 1000
                    : 25;
            }

            /// <summary>When the host last ran a test, or empty when it has never said.</summary>
            /// <param name="entry">The test.</param>
            private static string When(TestEntry entry)
            {
                var ticks = entry.Test.LastRunTicks;

                if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return string.Empty;

                var when = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();

                // A run this week reads as a day and a time, which is how somebody talks about it.
                // Older than that and the day of the week stops identifying anything.
                return DateTime.Now - when < TimeSpan.FromDays(6)
                    ? when.ToString("ddd HH:mm", CultureInfo.CurrentCulture)
                    : when.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
            }

            /// <summary>Text in the quotation marks a chip puts round a search.</summary>
            /// <param name="text">What was typed.</param>
            private static string Quoted(string text) => "“" + text + "”";

            // -------------------------------------------------------------------------------
            // What the board shows
            // -------------------------------------------------------------------------------

            /// <summary>
            /// Whether a result answers the board's search box.
            ///
            /// Every column the board can show, and the ones it cannot: the trail, the categories
            /// and whatever a reviewer set. A search that only matched the result's own name is
            /// the search that reads as broken the first time somebody types an element name into
            /// it, which is what they will type.
            /// </summary>
            /// <param name="row">The result.</param>
            /// <param name="find">What was typed.</param>
            /// <summary>
            /// Whether one result answers to what is typed in the board's search box.
            ///
            /// Against one string built once per row rather than eleven fields and every custom
            /// value scanned separately. A board search used to be up to a dozen case-insensitive
            /// scans per row per keystroke, over every result of the test.
            /// </summary>
            /// <param name="row">The result.</param>
            /// <param name="find">What is typed, already trimmed.</param>
            private bool Hits(Row row, string find) =>
                Searchable(row).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;

            /// <summary>
            /// Everything about one row that the search looks at, as one string.
            ///
            /// Built when it is first wanted and kept on the row, thrown away by whatever changed
            /// it - a status written, a value read into a column.
            /// </summary>
            /// <param name="row">The result.</param>
            private static string Searchable(Row row)
            {
                if (row.Blob != null) return row.Blob;

                var parts = new List<string?>
                {
                    row.Info.Name, row.TestName, row.Status, row.Group, row.Assigned,
                    row.LastComment, row.Info.NameA, row.Info.NameB,
                    row.Info.CategoryA, row.Info.CategoryB, row.Review?.Describe(),
                };

                parts.AddRange(row.Custom.Values);

                row.Blob = string.Join("\u001f", parts.Where(v => !string.IsNullOrEmpty(v)));

                return row.Blob;
            }

            /// <summary>Whether anything at all is narrowing the board.</summary>
            private bool Filtering =>
                _status != null || _party != null || _boardFind.Trim().Length > 0
                || _showing != BothKinds || _sameAs != null || _byColumn.Count > 0;

            /// <summary>Whether one result survives every filter in force.</summary>
            /// <param name="row">The result.</param>
            private bool Keep(Row row)
            {
                // One segment, one status. The strip used to fold Approved into Resolved on the
                // grounds that the difference is a reviewer's rather than a coordinator's - which
                // is true of the colour and not of the filter. Navisworks has five statuses, the
                // chip on the row writes all five, and a status that can be written and not found
                // is a row somebody loses.
                if (_status != null
                    && !string.Equals(row.Status, _status, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (_party != null
                    && !string.Equals(row.Assigned, _party, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (_sameAs != null && !Shares(row, _sameAs)) return false;

                foreach (var narrowed in _byColumn)
                {
                    // By id, not by scanning the column list. This ran per narrowed column per
                    // row, so a board narrowed on two columns walked the column list twice for
                    // every result of the test on every keystroke.
                    if (!_columnsById.TryGetValue(narrowed.Key, out var column)) continue;

                    if (!string.Equals(Custom(row, column), narrowed.Value,
                                       StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                var find = _boardFind.Trim();

                return find.Length == 0 || Hits(row, find);
            }

            /// <summary>
            /// Whether a result touches either of another result's two items.
            ///
            /// <b>By key, not by name.</b> Two different ducts are called "Duct 400x250" on every
            /// job there has ever been, and a filter that matched on the name would answer "what
            /// else clashes with this duct" with every duct of that size in the federation. The
            /// element key is the identity; the name is only what it is called.
            ///
            /// Names are the fallback for the one case the key cannot cover: a board read from
            /// somebody else's file, where the keys did not survive the round trip.
            /// </summary>
            /// <param name="row">The result being tested.</param>
            /// <param name="origin">The result whose items are being looked for.</param>
            private static bool Shares(Row row, Row origin)
            {
                bool Touches(ElementKey key, string? name)
                {
                    if (!key.IsEmpty)
                        return key.Equals(origin.Info.A) || key.Equals(origin.Info.B);

                    if (string.IsNullOrEmpty(name)) return false;

                    return string.Equals(name, origin.Info.NameA, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, origin.Info.NameB, StringComparison.OrdinalIgnoreCase);
                }

                return Touches(row.Info.A, row.Info.NameA) || Touches(row.Info.B, row.Info.NameB);
            }

            /// <summary>What the two items of the clash being matched against are called.</summary>
            /// <param name="origin">The clash.</param>
            private static string Both(Row origin)
            {
                var a = origin.Info.NameA ?? string.Empty;
                var b = origin.Info.NameB ?? string.Empty;

                return a.Length > 0 && b.Length > 0 ? a + " and " + b
                     : a.Length > 0 ? a
                     : b.Length > 0 ? b
                     : origin.Info.Name ?? "this clash";
            }

            /// <summary>
            /// What the filters leave, worked out once.
            ///
            /// The board used to ask for this three or four times per draw - once for the rows,
            /// once for the count over them, once for the "of n" under the loose band - and every
            /// ask walked every result of the test through every filter again. On a test with
            /// thousands of results that is the same work done four times for one keystroke.
            /// </summary>
            private sealed class BoardShape
            {
                internal BoardShape(List<BoardRow> groups, List<BoardRow> loose,
                                    int counted, int looseTotal)
                {
                    Groups = groups;
                    Loose = loose;
                    Counted = counted;
                    LooseTotal = looseTotal;
                }

                /// <summary>The clash groups the filters left.</summary>
                internal List<BoardRow> Groups { get; }

                /// <summary>The loose results the filters left.</summary>
                internal List<BoardRow> Loose { get; }

                /// <summary>How many results the board is showing, groups counted by their members.</summary>
                internal int Counted { get; }

                /// <summary>How many loose results the test has before any filter.</summary>
                internal int LooseTotal { get; }
            }

            /// <summary>
            /// Work out what the board is showing, once, and remember it until something moves.
            /// </summary>
            /// <param name="entry">The test on the board.</param>
            private BoardShape Shape(TestEntry entry)
            {
                // Keyed on everything that decides the answer, rather than cleared by hand
                // wherever one of them is set. A cache somebody has to remember to invalidate is
                // a cache that shows last keystroke's rows on the day somebody adds a filter.
                var now = string.Join("\u001f", new[]
                {
                    entry.Test.Id,
                    _status ?? string.Empty,
                    _party ?? string.Empty,
                    _boardFind.Trim(),
                    _showing,
                    _arrange,
                    _by ?? string.Empty,
                    _back ? "1" : "0",
                    _sameAs == null ? string.Empty : Id(_sameAs),
                    string.Join(",", _byColumn.Select(n => n.Key + "=" + n.Value)),
                    entry.Rows.Count.ToString(CultureInfo.InvariantCulture),
                    _edits.ToString(CultureInfo.InvariantCulture),
                });

                if (_shape != null && string.Equals(_shapeKey, now, StringComparison.Ordinal))
                    return _shape;

                var groups = Sorted(GroupRows(entry));
                var loose = Sorted(LooseRows(entry));

                _shape = new BoardShape(
                    groups, loose,
                    groups.Sum(g => g.Rows.Count) + loose.Count,
                    entry.Rows.Count(r => r.Group.Length == 0));

                _shapeKey = now;

                return _shape;
            }

            /// <summary>
            /// Say that the rows themselves changed, not just what is being shown of them.
            ///
            /// A status written, a regroup, a re-read. The filters have not moved but their answer
            /// has, and the signature above cannot see inside a row.
            /// </summary>
            private void Moved() => _edits++;

            /// <summary>The clash groups the filters leave, each with the members they leave.</summary>
            /// <param name="entry">The test on the board.</param>
            private List<BoardRow> GroupRows(TestEntry entry)
            {
                if (_showing == JustLoose) return new List<BoardRow>();

                return entry.Rows
                    .Where(r => r.Group.Length > 0)
                    .Where(Keep)
                    .GroupBy(r => r.Group, StringComparer.Ordinal)
                    .Select(g => new BoardRow(GroupId(g.Key), g.Key, true, g.ToList()))
                    .ToList();
            }

            /// <summary>The results in no group that the filters leave.</summary>
            /// <param name="entry">The test on the board.</param>
            private List<BoardRow> LooseRows(TestEntry entry)
            {
                if (_showing == JustGrouped) return new List<BoardRow>();

                return entry.Rows
                    .Where(r => r.Group.Length == 0)
                    .Where(Keep)
                    .Select(r => new BoardRow(Id(r), r.Info.Name ?? "this clash", false,
                                              new List<Row> { r }))
                    .ToList();
            }

            /// <summary>How many results the board is showing.</summary>
            /// <param name="entry">The test on the board.</param>
            private int Counted(TestEntry entry) => Shape(entry).Counted;

            /// <summary>
            /// The rows bucketed by whatever the view is arranged by, first seen first.
            ///
            /// A view arrangement and nothing else: no clash group is created, dissolved or
            /// touched by any of this, which is what the band above each bucket and the footnote
            /// under the table both say in as many words.
            /// </summary>
            /// <param name="entry">The test on the board.</param>
            /// <param name="groups">The clash groups the filters left.</param>
            /// <param name="loose">The loose results the filters left.</param>
            private List<KeyValuePair<string, List<BoardRow>>> Bands(
                TestEntry entry, List<BoardRow> groups, List<BoardRow> loose)
            {
                var order = new List<string>();
                var into = new Dictionary<string, List<BoardRow>>(StringComparer.Ordinal);

                foreach (var row in groups.Concat(loose))
                {
                    var key = Bucket(entry, row);

                    if (!into.TryGetValue(key, out var list))
                    {
                        list = new List<BoardRow>();
                        into[key] = list;
                        order.Add(key);
                    }

                    list.Add(row);
                }

                return order
                    .Select(k => new KeyValuePair<string, List<BoardRow>>(k, into[k]))
                    .ToList();
            }

            /// <summary>Which band a row falls in.</summary>
            /// <param name="entry">The test on the board.</param>
            /// <param name="row">The row.</param>
            private string Bucket(TestEntry entry, BoardRow row)
            {
                switch (_arrange)
                {
                    case ByAssignee:
                        return row.Assigned.Length == 0 ? "Nobody yet" : row.Assigned;

                    case ByStatus:
                        return row.Status;

                    case ByHowBad:
                        // Against this test's own tolerance, not a number typed once into a
                        // design: a 50 mm drainage test and a 10 mm ceiling test do not share a
                        // definition of bad.
                        return Past(Deep(row))
                            ? "Worse than " + Tolerance(entry)
                            : "Within " + Tolerance(entry);

                    default:
                        return string.Empty;
                }
            }

            // -------------------------------------------------------------------------------
            // Identity, distance and tone
            // -------------------------------------------------------------------------------

            /// <summary>What picking one result keys off.</summary>
            /// <param name="row">The result.</param>
            private static string Id(Row row) => Where(row.TestId, row.Info.Id);

            /// <summary>
            /// What picking a clash group keys off.
            ///
            /// It can never collide with a result's id, whatever a group is called: a result's is
            /// two host ids either side of a unit separator, and no group name reaches this
            /// carrying one.
            /// </summary>
            /// <param name="name">The group's name.</param>
            private static string GroupId(string name) => "group" + name;

            /// <summary>One result's overlap in millimetres, negative for a penetration.</summary>
            /// <param name="row">The result.</param>
            private double? Deep(Row row)
            {
                if (!row.Info.Distance.HasValue) return null;

                return row.Info.Distance.Value * (OnFile ? 1.0 : _session.MetresPerUnit) * 1000;
            }

            /// <summary>The worst overlap among a row's results.</summary>
            /// <param name="row">The row.</param>
            private double? Deep(BoardRow row)
            {
                double? worst = null;

                foreach (var member in row.Rows)
                {
                    var mm = Deep(member);
                    if (mm.HasValue && (!worst.HasValue || mm.Value < worst.Value)) worst = mm;
                }

                return worst;
            }

            /// <summary>Whether an overlap is past this test's tolerance.</summary>
            /// <param name="mm">The overlap in millimetres, or null when the host has none.</param>
            private bool Past(double? mm) => mm.HasValue && mm.Value <= -_toleranceMm;

            /// <summary>One result's overlap, as the board prints it.</summary>
            /// <param name="row">The result.</param>
            private string Millimetres(Row row)
            {
                var mm = Deep(row);

                return mm.HasValue
                    ? mm.Value.ToString("0", CultureInfo.InvariantCulture) + " mm"
                    : "—";
            }

            /// <summary>A group's worst overlap, as the board prints it.</summary>
            /// <param name="row">The group.</param>
            private string Deepest(BoardRow row)
            {
                var mm = Deep(row);

                return mm.HasValue
                    ? mm.Value.ToString("0", CultureInfo.InvariantCulture) + " mm"
                    : "—";
            }

            /// <summary>Whether a result is past tolerance.</summary>
            /// <param name="row">The result.</param>
            private bool Bad(Row row) => Past(Deep(row));

            /// <summary>Whether a row's worst is past tolerance.</summary>
            /// <param name="row">The row.</param>
            private bool Bad(BoardRow row) => Past(Deep(row));

            /// <summary>How a status reads.</summary>
            /// <param name="status">The status.</param>
            private static Tone Reads(string status) => status switch
            {
                "New" => Tone.Bad,
                "Reviewed" => Tone.Warn,
                "Approved" => Tone.Good,
                "Resolved" => Tone.Good,
                _ => Tone.Plain,
            };

            /// <summary>The tone bar down the left of a row, or nothing while it is only active.</summary>
            /// <param name="status">The row's status.</param>
            private static System.Windows.Media.Brush Edge(string status) => status switch
            {
                "New" => Skin.Of("RedTx"),
                "Reviewed" => Skin.Of("AmberTx"),
                "Approved" => Skin.Of("Ok"),
                "Resolved" => Skin.Of("Ok"),
                _ => System.Windows.Media.Brushes.Transparent,
            };

            /// <summary>One custom column's value on a result, or empty.</summary>
            /// <param name="row">The result.</param>
            /// <param name="column">The column.</param>
            private static string Custom(Row row, PropColumn column) =>
                row.Custom.TryGetValue(column.Id, out var value) ? value : string.Empty;

            // -------------------------------------------------------------------------------
            // What is picked
            // -------------------------------------------------------------------------------

            /// <summary>Every result under what is ticked, each one once.</summary>
            private List<Row> Picked()
            {
                var entry = Entry();
                if (entry == null) return new List<Row>();

                var rows = new List<Row>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var row in GroupRows(entry).Concat(LooseRows(entry)))
                {
                    if (!_picked.Contains(row.Id)) continue;

                    foreach (var member in row.Rows)
                        if (seen.Add(Id(member))) rows.Add(member);
                }

                // A member of an open group, ticked on its own row rather than through its group.
                foreach (var member in entry.Rows)
                    if (_picked.Contains(Id(member)) && seen.Add(Id(member))) rows.Add(member);

                return rows;
            }

            /// <summary>The results among these that are in no clash group.</summary>
            /// <param name="rows">The results.</param>
            private static List<Row> LooseIn(IEnumerable<Row> rows) =>
                rows.Where(r => r.Group.Length == 0).ToList();

            /// <summary>Tick every row of a band.</summary>
            /// <param name="rows">The band's rows.</param>
            private void PickAll(IEnumerable<BoardRow> rows)
            {
                foreach (var row in rows) _picked.Add(row.Id);
                RedrawRows();
            }

            /// <summary>These results, plus whatever else is already ticked.</summary>
            /// <param name="picked">The row the menu was opened on.</param>
            private IReadOnlyList<object> WithPicked(IReadOnlyList<object> picked)
            {
                var rows = Rows(picked);
                var seen = new HashSet<string>(rows.Select(Id), StringComparer.Ordinal);

                foreach (var row in Picked()) if (seen.Add(Id(row))) rows.Add(row);

                return rows.Cast<object>().ToList();
            }

            /// <summary>The next unused "New group N" for this test.</summary>
            private string Suggested()
            {
                var entry = Entry();

                var taken = new HashSet<string>(
                    (entry?.Rows ?? new List<Row>()).Select(r => r.Group).Where(g => g.Length > 0),
                    StringComparer.OrdinalIgnoreCase);

                var n = 1;
                while (taken.Contains("New group " + n)) n++;

                return "New group " + n;
            }

            // -------------------------------------------------------------------------------
            // Asking for a word before doing something with it
            // -------------------------------------------------------------------------------

            /// <summary>
            /// Ask for one line of text, beside the board rather than in a window.
            ///
            /// It used to be a field on a settings page behind a symbol: type the name there,
            /// come back, pick the rows, press Group. Three of the four steps were the interface's
            /// and not the job's.
            /// </summary>
            /// <param name="title">What is being asked for.</param>
            /// <param name="starting">What the box starts with.</param>
            /// <param name="caption">What the button says.</param>
            /// <param name="onDone">What to do with the answer.</param>
            private void Prompt(string title, string starting, string caption, Action<string> onDone)
            {
                Parts.Ask(_anchor, title, starting, caption, text => Ui.Safely(() => onDone(text)));
            }

            // -------------------------------------------------------------------------------
            // Writing, from the board's own controls
            // -------------------------------------------------------------------------------

            /// <summary>Set the status from the chip on a row, over everything picked.</summary>
            /// <param name="row">The row the chip is on.</param>
            /// <param name="status">The new status.</param>
            private void SetStatus(BoardRow row, string status) =>
                Mark(Reaching(row), status, _toast);

            /// <summary>Set the assignee from the chip on a row, over everything picked.</summary>
            /// <param name="row">The row the chip is on.</param>
            /// <param name="who">Who it is with now.</param>
            private void SetAssignee(BoardRow row, string who) =>
                AssignPicked(Reaching(row), who == Nobody ? string.Empty : who, _toast);

            /// <summary>
            /// What a verb pressed on one row actually acts on.
            ///
            /// <b>The selection, when the row is in it.</b> Somebody who has picked forty rows
            /// and then sets a status on one of them means all forty - that is what picking them
            /// was for. Acting on the one row under the pointer and leaving the other
            /// thirty-nine is the behaviour that makes a selection feel decorative, and it is
            /// what every row chip and row verb did.
            ///
            /// A row outside the selection is itself, and only itself. Right-clicking one already
            /// moves the selection to it, so the two rules never disagree.
            /// </summary>
            /// <param name="row">The row the verb was pressed on.</param>
            private List<object> Reaching(BoardRow row) =>
                _picked.Count > 1 && _picked.Contains(row.Id)
                    ? Picked().Cast<object>().ToList()
                    : row.Rows.Cast<object>().ToList();

            /// <summary>Set the status or the assignee over everything picked.</summary>
            /// <param name="picked">The rows.</param>
            /// <param name="value">The new value.</param>
            /// <param name="status">True for a status, false for an assignee.</param>
            private void SetMany(IReadOnlyList<object> picked, string value, bool status)
            {
                if (status) Mark(picked, value, _toast);
                else AssignPicked(picked, value == Nobody ? string.Empty : value, _toast);
            }

            /// <summary>
            /// Group, ungroup, rename or move — every one of which is the same host write.
            ///
            /// A real clash group, made in the test: it is saved with the test, it carries its own
            /// status and comments, and it goes out with every report and BCF file. Nothing on
            /// this board makes a group that only this screen can see.
            /// </summary>
            /// <param name="picked">The results to move.</param>
            /// <param name="name">The group to put them in, or empty to put them back loose.</param>
            /// <param name="did">What to say afterwards, with a slot for the count.</param>
            private void Regroup(IReadOnlyList<object> picked, string name, string did)
            {
                var into = (name ?? string.Empty).Trim();

                Write(picked, _toast,
                      into.Length == 0
                          ? "Put {0} back loose, out of their clash group."
                          : did + " " + Quoted(into) + ".",
                      (testId, ids) => _session.Clash.Regroup(testId, ids, into),
                      row => row.Group = into,
                      (finding, row) => { _ = row; finding.Group = into.Length == 0 ? null : into; },
                      FindingField.Group);

                if (into.Length > 0)
                {
                    _made.Add(GroupId(into));
                    _open.Add(GroupId(into));
                }

                _picked.Clear();
                _pickedFrom = null;
                Redraw();
            }

            /// <summary>Rename the test on the board.</summary>
            /// <param name="entry">The test.</param>
            /// <param name="name">Its new name.</param>
            private void RenameTest(TestEntry? entry, string name)
            {
                var wanted = (name ?? string.Empty).Trim();

                if (entry == null || wanted.Length == 0)
                {
                    Warned(Ui.Problem("Pick a test on the left and type a name for it."));
                    return;
                }

                // The document first, this project when the document will not take it.
                //
                // A published NWD holds its clash data read-only, which is the seat most review
                // actually happens on - and there this verb simply refused. The rule everywhere
                // else on this board is that the database is the record and the document is a
                // projection of it; a test's own name had nowhere to go, so it was the one
                // decision that got lost.
                var landed = _session.Clash.RenameTest(entry.Test.Id, wanted);

                if (!landed)
                {
                    var names = TestNames();

                    names.Rename(entry.Test.Id, wanted);

                    if (!KeepTestNames(names))
                    {
                        Warned(Ui.Problem("That test was not renamed, and the new name could not "
                            + "be kept in this project either. "
                            + (_session.Clash.LastProblem ?? string.Empty)));
                        return;
                    }
                }

                _session.Record(ActivityKind.Regroup,
                                "renamed the test \"" + entry.Test.Name + "\" to \"" + wanted + "\"");

                // One word, in memory. This used to re-harvest every result on the job to change
                // the name on a rail row.
                entry.Rename(wanted);

                foreach (var row in entry.Rows) row.TestName = wanted;

                Redraw();
                Tell("Renamed.", null);
            }

            /// <summary>
            /// Rename results — through the database, like every other edit on this board.
            ///
            /// <b>This one used to go straight to the host.</b> It called <c>RenamePicked</c>, and
            /// if the document refused — a published NWD refuses everything — the rename was
            /// simply gone. Every other edit on this screen survives that: the finding takes it,
            /// the board keeps showing it, and the sync writes it into the NWF later. A name is
            /// one of the five fields the register holds and one of the five the sync pushes, so
            /// there was no reason for it to be the exception except that it was written last.
            ///
            /// The host has no rename for one result: it takes a prefix and numbers what is
            /// picked from it, so renaming one row makes "Corridor1". The board is one test at a
            /// time, so mirroring that numbering locally is exact.
            /// </summary>
            /// <param name="rows">The results to rename.</param>
            /// <param name="prefix">What to number them from.</param>
            private void Renumber(IReadOnlyList<Row> rows, string prefix)
            {
                prefix = (prefix ?? string.Empty).Trim();

                if (rows.Count == 0 || prefix.Length == 0) return;

                // The names, worked out once, so the row and its finding get the same one.
                //
                // They did not. The row took prefix1, prefix2 and the finding took a bare
                // "prefix" on every one of them - so the board read right and the database, which
                // is the record, held every renamed clash under one name. And the counter ran
                // straight through from one test into the next, while the host restarts it per
                // test, so the two disagreed the moment more than one test was picked.
                var named = new Dictionary<Row, string>();

                foreach (var byTest in rows.GroupBy(r => r.TestId, StringComparer.OrdinalIgnoreCase))
                {
                    var n = 0;

                    foreach (var row in byTest)
                        named[row] = prefix + (++n).ToString(CultureInfo.InvariantCulture);
                }

                Write(rows.Cast<object>().ToList(), _toast,
                      rows.Count == 1
                          ? "Renamed it to “" + prefix + "1”."
                          : "Renamed {0} to “" + prefix + "1” onwards.",
                      (testId, ids) => _session.Clash.RenamePicked(testId, ids, prefix),
                      row =>
                      {
                          if (named.TryGetValue(row, out var call)) row.Info.Name = call;
                      },
                      (finding, row) =>
                      {
                          if (named.TryGetValue(row, out var call)) finding.Title = call;
                      },
                      FindingField.Title);
            }

            /// <summary>Copy the rail to the clipboard, one test per line.</summary>
            /// <param name="read">The tests.</param>
            /// <summary>
            /// Every result of the shown tests, as an OBJ.
            ///
            /// The whole of what is read rather than one test, because the reason to write this
            /// file is to send somebody a package - and a package of one test is a phone call.
            /// The tests stay apart inside it: a group each, so the receiving tool's tree reads
            /// the same way this board does.
            /// </summary>
            /// <param name="read">The tests on the board.</param>
            private void ExportTestMarkers(IReadOnlyList<TestEntry> read)
            {
                var rows = read.SelectMany(e => e.Rows).ToList();

                if (rows.Count == 0)
                {
                    Warned(Ui.Problem("There are no results to mark. Read the results first."));
                    return;
                }

                ExportMarkers(rows, read.Count == 1
                    ? "\"" + read[0].Test.Name + "\""
                    : Ui.Count(read.Count, "test"), _log);
            }

            /// <summary>What this project calls its tests, read fresh off the project file.</summary>
            private ClashTestNames TestNames() =>
                ClashTestNames.FromJson(_session.Store.Section(ProjectStore.ClashSection)[ClashTestNames.Key]);

            /// <summary>Keep what this project calls its tests.</summary>
            /// <param name="names">The names to keep.</param>
            /// <returns>True when the project file took them.</returns>
            private bool KeepTestNames(ClashTestNames names)
            {
                _session.Store.Section(ProjectStore.ClashSection)
                        .Set(ClashTestNames.Key, names.ToJson());

                return _session.Store.Save();
            }

            /// <summary>
            /// Put this project's own names and folders over what the host reported.
            ///
            /// Applied on every read, so a test renamed on a published NWD still reads by the
            /// name the coordinator chose - and a test the document has since accepted the name
            /// for stops being overridden, because the entry is dropped the moment the host takes
            /// it.
            /// </summary>
            /// <param name="entries">Everything just read.</param>
            private void Renamed(IReadOnlyList<TestEntry> entries)
            {
                var names = TestNames();

                if (names.Count == 0) return;

                foreach (var entry in entries)
                {
                    var held = names.For(entry.Test.Id);

                    if (held == null) continue;

                    if (!string.IsNullOrWhiteSpace(held.Name)) entry.Rename(held.Name!);

                    if (held.FolderCleared) entry.Refile(null);
                    else if (!string.IsNullOrWhiteSpace(held.Folder)) entry.Refile(held.Folder);

                    foreach (var row in entry.Rows) row.TestName = entry.Test.Name;
                }
            }

            private void CopyTests(IReadOnlyList<TestEntry> read)
            {
                var lines = new List<string> { "Test\tFolder\tResults\tNew\tActive\tReviewed\tClosed" };

                foreach (var entry in read)
                    lines.Add(string.Join("\t", entry.Test.Name, entry.Test.Folder ?? string.Empty,
                                          entry.Rows.Count.ToString(CultureInfo.InvariantCulture),
                                          entry.Of("New").ToString(CultureInfo.InvariantCulture),
                                          entry.Of("Active").ToString(CultureInfo.InvariantCulture),
                                          entry.Of("Reviewed").ToString(CultureInfo.InvariantCulture),
                                          (entry.Of("Approved") + entry.Of("Resolved"))
                                              .ToString(CultureInfo.InvariantCulture)));

                Ui.Copy(lines, _toast, "test");
            }

            /// <summary>Copy results to the clipboard, as the board's own columns.</summary>
            /// <param name="rows">The results.</param>
            private void CopyRows(IReadOnlyList<Row> rows)
            {
                var head = new List<string> { "Result", "Clash group", "Item 1", "Item 2", "Worst" };

                foreach (var column in _columns) head.Add(column.Title);

                head.Add("Status");
                head.Add("Assigned");

                var lines = new List<string> { string.Join("\t", head) };

                foreach (var row in rows)
                {
                    var cells = new List<string>
                    {
                        row.Info.Name ?? string.Empty, row.Group, row.Info.NameA ?? string.Empty,
                        row.Info.NameB ?? string.Empty, Millimetres(row),
                    };

                    foreach (var column in _columns) cells.Add(Custom(row, column));

                    cells.Add(row.Status);
                    cells.Add(row.Assigned);

                    lines.Add(string.Join("\t", cells));
                }

                Ui.Copy(lines, _toast, "result");
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
                        problems.Add("\"" + entry.Test.Name + "\" could not be run - running a "
                                     + "test is the engine's work and there is nothing this "
                                     + "project can keep on its behalf. "
                                     + (_session.Clash.LastProblem ?? string.Empty));
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

                // The run changed what these tests hold, and nothing else on the job.
                RereadTests(tests.Select(t => t.Test.Id).ToList(),
                            Ui.Count(ran, "test") + " ran.", problems);
            }

            /// <summary>Renumber a test's results, prefix1, prefix2, and so on.</summary>
            /// <param name="picked">The test.</param>
            /// <param name="prefix">What to call them.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void RenameResults(IReadOnlyList<object> picked, string prefix, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                prefix = (prefix ?? string.Empty).Trim();

                if (prefix.Length == 0)
                {
                    say.Children.Add(Ui.Problem("Renumbering needs a prefix to number from."));
                    return;
                }

                var done = 0;
                var refused = new List<TestEntry>();

                foreach (var entry in tests)
                {
                    if (_session.Clash.RenameResults(entry.Test.Id, prefix)) done++;
                    else refused.Add(entry);
                }

                // What the document would not renumber is renumbered here instead, through the
                // same door every other decision on this board goes through: the row takes the
                // new name, the finding keeps it, and the sync carries it into an NWF from a
                // Manage seat. A published NWD refuses all of them, and that is not a reason for
                // the renumber to have not happened.
                if (refused.Count > 0)
                    Renumber(refused.SelectMany(e => e.Rows).ToList(), prefix);

                _session.Record(ActivityKind.Regroup, "renamed the results of "
                    + Ui.Count(tests.Count, "test") + " to \"" + prefix + "...\"");

                if (done == 0)
                {
                    Redraw();
                    return;
                }

                RereadTests(tests.Where(t => !refused.Contains(t)).Select(t => t.Test.Id).ToList(),
                            "Renamed the results of " + Ui.Count(done, "test") + ".", null);
            }

            /// <summary>Move tests into a clash folder, or out to the root.</summary>
            /// <param name="picked">The tests.</param>
            /// <param name="folder">The folder, or empty for the root.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void MoveTests(IReadOnlyList<object> picked, string folder, Panel say)
            {
                say.Children.Clear();

                var tests = Entries(picked);
                folder = (folder ?? string.Empty).Trim();

                if (tests.Count == 0)
                {
                    Warned(Ui.Problem("No test is picked, so there was nothing to move."));
                    return;
                }

                // The database, and only the database.
                //
                // A folder here is this project's, not the host's. Writing it into the clash
                // detective meant a published NWD refused the move outright, and on the seats
                // that did take it the folder then belonged to a file rather than to the job -
                // so two people reading the same model disagreed about how it was filed. Kept
                // here it works on every seat, survives a re-read, and the native clash detective
                // is left exactly as its own users expect to find it.
                var names = TestNames();

                foreach (var test in tests) names.Refile(test.Test.Id, folder);

                if (!KeepTestNames(names))
                {
                    Warned(Ui.Problem("Where those tests were moved to could not be kept in "
                        + "this project."));
                    return;
                }

                _session.Record(ActivityKind.Regroup, "moved " + Ui.Count(tests.Count, "clash test")
                    + (folder.Length == 0 ? " to the root" : " into \"" + folder + "\""));

                // In memory rather than a re-read. Re-reading threw the board away and put the
                // rail back on whichever test came first, which is what "all it does is change
                // the selected test" was.
                foreach (var test in tests) test.Refile(folder.Length == 0 ? null : folder);

                Moved();
                Redraw();
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
                        // One of the three verbs here that genuinely cannot fall back to the
                        // project. A status, a group or a name is a decision about a clash and
                        // belongs in the database whatever the document says; deleting a test is
                        // not a decision that can be kept and synced later, because there would
                        // be nothing left to sync it onto.
                        say.Children.Add(Ui.Problem("Deleting a test is a change to the document "
                            + "itself, so it cannot be kept in this project and synced later. "
                            + (_session.Clash.LastProblem ?? "The host refused.")));
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
                    say.Children.Add(Ui.Problem("Compacting drops results out of the document "
                        + "itself, so it cannot be kept in this project and synced later. "
                        + (_session.Clash.LastProblem ?? "The host refused.")));
                    return;
                }

                _session.Record(ActivityKind.Regroup,
                                "compacted " + Ui.Count(tests.Count, "clash test")
                                + ", dropping their Resolved results from storage");
                RereadTests(tests.Select(t => t.Test.Id).ToList(),
                       "Compacted " + Ui.Count(tests.Count, "test") + " - the Resolved results "
                       + "are out of storage. The next run recreates whatever still clashes.", null);
            }

            // -------------------------------------------------------------------------------
            // Result verbs
            // -------------------------------------------------------------------------------

            private static List<Row> Rows(IReadOnlyList<object> picked) =>
                picked.OfType<Row>().ToList();

            /// <summary>The element keys behind the picked rows, minus the ones a file could not name.</summary>
            private static List<ElementKey> Keys(IReadOnlyList<Row> rows) =>
                rows.SelectMany(r => new[] { r.Info.A, r.Info.B })
                    .Where(k => !k.IsEmpty)
                    .Distinct()
                    .ToList();

            /// <summary>
            /// Go and look at the picked clashes. Instantly, whatever the board holds.
            ///
            /// <b>Nothing here is allowed to search the model.</b> A row carries an element key
            /// per side, and turning a key that is not an instance GUID back into an element means
            /// an index of every geometry leaf in the federation - which, inside a press, is a
            /// Navisworks that stops answering. It was reported as Focus freezing, and it was.
            ///
            /// Neither half of what Focus does needs a search:
            ///
            /// <list type="bullet">
            /// <item><description>The camera. A clash result records the point where the two
            /// elements met, and for a single result the engine also kept the view it composed
            /// when the test ran. Both are on the row already.</description></item>
            /// <item><description>The selection. These results came out of this document, and the
            /// engine is still holding the two items each one is between. Asking it for them is a
            /// copy, not a lookup.</description></item>
            /// </list>
            ///
            /// A board read from somebody else's file is the one case with no engine behind it.
            /// There the keys are all there is - so they are resolved, but only against an index
            /// that already exists. Focus never builds one; the read does that, where there is a
            /// progress bar.
            /// </summary>
            /// <param name="picked">The picked rows.</param>
            /// <param name="say">Where to report what happened.</param>
            private void Focus(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                // The camera, from the point already on the row.
                //
                // Nothing is asked of the clash engine on the way. Focus used to open with
                // ApplyResultView, which has the host compose a viewpoint for the result - and
                // composing a view is work the host does from the two elements, every press, for
                // a camera position the row already carries. That call is the second somebody was
                // waiting through. It is still here, as its own verb, for the times the engine's
                // own framing is what you want.
                var point = Middle(rows);

                if (point != null)
                    _session.View.LookAt(point.Value.X, point.Value.Y, point.Value.Z,
                                         _focusBack / 1000.0);

                // The ball, if it is wanted. Every picked point rather than the average the camera
                // used, because on a multi-row focus the average is a place where nothing clashed.
                // Free: it is a set swap and a frame, not geometry.
                if (_markOnFocus) MarkFocused(rows);

                var selected = OnFile ? FromKeys(rows) : FromEngine(rows);

                // Nothing is said when it worked. A confirmation that the camera moved, over a
                // view somebody is already looking at, is a strip that appears on every row they
                // click and tells them what they can see.
                if (point == null && selected == 0)
                    say.Children.Add(Ui.Problem("Those results record no point and name no "
                        + "elements this federation has, so there is nothing to look at."));
            }


            /// <summary>
            /// Select what the engine is holding for these rows. Costs a copy, never a search.
            /// </summary>
            /// <param name="rows">The picked rows, from the model.</param>
            /// <returns>How many elements were selected.</returns>
            private int FromEngine(IReadOnlyList<Row> rows)
            {
                var selected = 0;
                var replacing = true;

                foreach (var byTest in rows.GroupBy(r => r.TestId, StringComparer.Ordinal))
                {
                    var count = _session.Clash.SelectResults(
                        byTest.Key,
                        byTest.Select(r => r.Info.Id).ToList(),
                        !replacing);

                    if (count < 0) continue;

                    selected += count;
                    replacing = false;
                }

                return selected;
            }

            /// <summary>
            /// How many of the last selection's elements needed an index nobody has built yet.
            ///
            /// Zero after a selection that reached everything it was asked for. Read by the verbs
            /// so they can say what they could not reach instead of quietly doing less.
            /// </summary>
            private int _outOfReach;

            /// <summary>
            /// Select what these rows name, for a board that came out of a file rather than the
            /// engine.
            ///
            /// <b>Why this is not simply "select the two elements the result names".</b> From the
            /// model, it is: the clash engine is holding the two items and hands them over, which
            /// is what <see cref="FromEngine"/> asks it for. From a file there is no engine and no
            /// result - the file leaves us the two element KEYS, and a key is a hash, not a
            /// pointer. Turning one back into an element means finding the element it names.
            ///
            /// For a key made from an instance GUID that is a dictionary lookup the document
            /// answers straight away. For a key made from a tree path or a geometry signature it
            /// means walking the federation to find what matches - the indexing pass, which on a
            /// large model is the freeze this verb exists on the other side of.
            ///
            /// So this reaches for what is free and leaves the rest, rather than giving up on all
            /// of it because one key is expensive. Before, a single tree-path key among forty
            /// GUIDs meant nothing at all was selected, and nothing said so - which is exactly
            /// "why can't we just feed it the two elements". We can, for the ones the document can
            /// find without being searched; the others need the index, and now the verb says so
            /// rather than doing nothing quietly.
            /// </summary>
            /// <param name="rows">The picked rows, from a file.</param>
            /// <returns>How many elements were selected.</returns>
            private int FromKeys(IReadOnlyList<Row> rows)
            {
                _outOfReach = 0;

                var keys = Keys(rows);

                if (keys.Count == 0) return 0;

                // Indexed already: every rung resolves, so ask for the lot.
                if (_session.Model.IsIndexed)
                {
                    _session.Model.Select(keys);
                    return keys.Count;
                }

                var free = keys.Where(k => k.Rung == KeyRung.InstanceGuid).ToList();

                _outOfReach = keys.Count - free.Count;

                if (free.Count == 0) return 0;

                _session.Model.Select(free);
                return free.Count;
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
            /// <summary>
            /// Hang a ball on each of these, quietly.
            ///
            /// Quiet because it is a side effect of Focus rather than something somebody asked
            /// for: a host with no view to draw into, or a size that has been typed down to
            /// nothing, leaves the camera move alone rather than putting an error where the
            /// "Selected and framed" line goes.
            /// </summary>
            /// <param name="rows">The rows being focused.</param>
            private void MarkFocused(IReadOnlyList<Row> rows)
            {
                if (!_session.View.CanMark) return;

                var metres = _session.MetresPerUnit;
                var perUnit = metres > 0 ? metres : 1.0;
                var radius = _markerAcross / 2000.0 / perUnit;

                if (radius <= 0) return;

                _session.View.ShowMarkers(Marks(rows, OnFile ? 1.0 / perUnit : 1.0, radius));
            }

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
            /// Put the picked results onto an issue that already exists.
            ///
            /// <b>The verb the board was missing.</b> "Create an issue" makes a new one every
            /// time, so a coordinator working through a test that is all one problem ends up with
            /// forty issues about the same duct run. This is the other half: pick the results,
            /// pick the issue, and they join it.
            ///
            /// Findings rather than clash keys, so the same issue can carry a clearance breach
            /// and a health failure beside its clashes once those move across.
            /// </summary>
            /// <param name="picked">The picked results.</param>
            /// <param name="say">Where to answer.</param>
            private void AttachToIssue(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var findings = rows.Select(Found).Where(f => f != null).Select(f => f!).ToList();

                if (findings.Count == 0)
                {
                    say.Children.Add(Ui.Problem(OnFile
                        ? "A file's results are not kept as findings, so there is nothing to "
                          + "attach. Open the model and read its results first."
                        : "This read kept no findings, so there is nothing to attach. Press "
                          + "\"Read the results\" and try again."));

                    return;
                }

                var register = IssueViews.Read(_session);

                var open = register.Issues
                    .OrderBy(i => string.Equals(i.Status, Issue.ClosedStatus,
                                                StringComparison.OrdinalIgnoreCase))
                    .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (open.Count == 0)
                {
                    say.Children.Add(Ui.Problem("There are no issues yet. \"Create an issue\" "
                        + "makes one from what is picked."));

                    return;
                }

                _log.Children.Clear();
                _log.Children.Add(Ui.Eyebrow("Attach " + Ui.Count(findings.Count, "result")
                                             + " to an issue"));

                var table = Ui.Table(
                    open.Select(i => new TableRow(i, i.Id, i.Title, i.Status,
                                                  i.AssignedTo ?? string.Empty,
                                                  Ui.Count(i.Findings.Count, "finding"))),
                    ("Issue", 70), ("Title", 240), ("Status", 90), ("Assigned", 110),
                    ("Already on it", 0));

                _log.Children.Add(table);

                _log.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Attach to {n}", (chosen, told) =>
                    {
                        told.Children.Clear();

                        var issues = chosen.OfType<Issue>().ToList();

                        if (issues.Count == 0) return;

                        var added = 0;
                        var now = DateTime.UtcNow.Ticks;

                        foreach (var issue in issues)
                        {
                            var joined = IssueFindings.Attach(issue, findings);

                            if (joined == 0) continue;

                            added += joined;
                            issue.ModifiedTicks = now;
                        }

                        var problem = IssueViews.Write(_session, register);

                        if (problem != null)
                        {
                            told.Children.Add(Ui.Problem(problem));
                            return;
                        }

                        _session.Record(ActivityKind.Regroup,
                            "attached " + Ui.Count(added, "finding") + " to "
                            + Ui.Count(issues.Count, "issue"));

                        told.Children.Add(added == 0
                            ? Ui.Done("Every one of them was already on "
                                      + (issues.Count == 1 ? "that issue." : "those issues."))
                            : Ui.Done(Ui.Count(added, "result") + " attached to "
                                      + (issues.Count == 1
                                          ? issues[0].Id + "."
                                          : Ui.Count(issues.Count, "issue") + ".")));
                    })));

                say.Children.Add(Ui.Info("Pick the issue below the results.",
                    "Open the register", () => IssueViews.Open(null)));
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
                        // The finding, when this read kept one - which is what the issue is
                        // really about, and what lets it hold a clearance breach or a health
                        // failure alongside. The clash key stays for a read that kept none, so
                        // an issue raised off a file still says which conflict it is about.
                        var finding = Found(row);

                        if (finding != null) IssueFindings.Attach(issue, new[] { finding });
                        else issue.Clashes.Add(row.Info.Key());

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

                if (problem != null) Warned(Ui.Problem(problem));
            }

            /// <summary>
            /// Hide everything but the picked clashes.
            ///
            /// Unlike Focus this genuinely needs the elements - there is no way to hide the rest
            /// of a model without knowing which ones to keep. It still does not search for them:
            /// the selection comes from the engine's own handles, and the keys are then read back
            /// off what is selected, which is a handful of items rather than a federation.
            /// </summary>
            /// <param name="picked">The picked rows.</param>
            /// <param name="say">Where to report what happened.</param>
            private void Isolate(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                var rows = Rows(picked);
                if (rows.Count == 0) return;

                var keys = Reach(rows);

                if (keys.Count == 0)
                {
                    // Two different failures, and telling them apart is the difference between
                    // "this model does not have them" and "ask for them and they will come".
                    Warned(_outOfReach > 0
                        ? Ui.Problem("Those results name elements this document can only find by "
                            + "searching it, and it has not been searched yet. Read the results "
                            + "from the model once - that builds the index - and Isolate reaches "
                            + "them from then on. Focus works either way: it moves the camera to "
                            + "the recorded clash point and needs no elements at all.")
                        : Ui.Problem("Those results name no elements this federation has, so "
                            + "there is nothing to isolate."));
                    return;
                }

                // Some reached, some not. Isolating what was reached and saying nothing would show
                // one side of a clash and look like the isolate had simply gone wrong.
                if (_outOfReach > 0)
                    Warned(Ui.Note(Ui.Count(_outOfReach, "element") + " could not be reached "
                        + "without searching the document, so " + (_outOfReach == 1 ? "it is" : "they are")
                        + " not in this isolate. Read the results from the model once to index it."));

                // On the selection the engine just made, not on its keys. Handing keys over
                // meant resolving each one back to the element it names, which for a key that is
                // not an instance GUID indexes the whole federation - so isolating what was
                // already selected started a walk of the model to find what it was holding.
                var kept = _session.View.IsolateSelection();

                // The one case that used to fail in silence. Isolating works off the host's
                // current selection, so a selection the host did not take leaves this press
                // having done nothing at all - and "isolate does not work" is then a report
                // nobody can act on, because it does not say whether the host refused, hid the
                // wrong thing, or had nothing to hide around.
                if (kept == 0)
                {
                    say.Children.Add(Ui.Problem("The host is not holding a selection for those "
                        + "results, so there was nothing to isolate around. Press Focus first - "
                        + "if that does not select them either, this document no longer has the "
                        + "elements the results were found between."));
                    return;
                }

                _session.View.ZoomToSelection(1.0);

                // Nothing said when it worked. What isolating did is the thing on the screen.
                _ = keys;
            }

            /// <summary>
            /// Select the picked rows' elements and hand back the keys of what actually got there.
            ///
            /// For the verbs that need the elements themselves rather than only a camera position.
            /// Keying the selection is per-item work over a handful of items; it is nothing like
            /// the walk of the whole federation that resolving a key from cold can be.
            /// </summary>
            /// <param name="rows">The picked rows.</param>
            private IReadOnlyList<ElementKey> Reach(IReadOnlyList<Row> rows)
            {
                var selected = OnFile ? FromKeys(rows) : FromEngine(rows);

                return selected == 0
                    ? (IReadOnlyList<ElementKey>)Array.Empty<ElementKey>()
                    : _session.Model.SelectedKeys();
            }

            /// <summary>Set the status on everything picked.</summary>
            /// <param name="picked">The results.</param>
            /// <param name="status">The status to write.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void Mark(IReadOnlyList<object> picked, string status, Panel say) =>
                Write(picked, say, "Marked {0} as " + status + ".",
                      (testId, ids) => _session.Clash.SetStatus(testId, ids, status),
                      row => row.Status = status,
                      (finding, row) =>
                      {
                          _ = row;

                          if (FindingStatusLattice.TryParse(status, out var marked))
                              finding.Status = marked;
                      },
                      FindingField.Status);

            /// <summary>Assign everything picked, or clear the assignment.</summary>
            /// <param name="picked">The results.</param>
            /// <param name="who">Who it is with, or empty to clear it.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void AssignPicked(IReadOnlyList<object> picked, string who, Panel say)
            {
                who = (who ?? string.Empty).Trim();

                Write(picked, say,
                      who.Length == 0 ? "Cleared the assignment on {0}." : "Assigned {0} to " + who + ".",
                      (testId, ids) => _session.Clash.Assign(testId, ids, who),
                      row => row.Assigned = who,
                      (finding, row) => { _ = row; finding.AssignedTo = who.Length == 0 ? null : who; },
                      FindingField.AssignedTo);
            }

            /// <summary>
            /// The conversation about one clash: what has been said, and the say in it.
            ///
            /// <b>Reading was the half that did not exist.</b> The board showed how many comments
            /// a result had and the newest one, and the only verb was "Comment", which added
            /// another - so a trail could be written and never read back. Everything a coordinator
            /// actually does with a comment thread, which is read it before adding to it, had to
            /// happen in Navisworks' own clash window.
            ///
            /// <b>Edit is a rebuild, and says so.</b> Every field of a host comment is read-only
            /// and the only write is the whole trail at once, so changing a remark means putting a
            /// new one in its place - which takes today's date. The button says that rather than
            /// implying the original was amended.
            /// </summary>
            /// <param name="row">The clash whose conversation this is.</param>
            private void Conversation(Row row)
            {
                var body = Ui.Stack();

                var window = new Window
                {
                    Title = "CamelWorks - " + (row.Info.Name ?? "this clash"),
                    Width = 520,
                    Height = 520,
                    MinWidth = 380,
                    MinHeight = 320,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false,
                };

                var trail = Ui.Stack();

                void Redraw()
                {
                    trail.Children.Clear();

                    var said = Trail(row);

                    if (said.Count == 0)
                    {
                        trail.Children.Add(Ui.Empty("Nothing has been said about this clash yet.",
                            "What is written here goes into the model's own clash data, and "
                            + "travels out with every report and BCF file."));
                        return;
                    }

                    foreach (var comment in said) trail.Children.Add(Remark(row, comment, Redraw));
                }

                Redraw();

                var box = Ui.Text(string.Empty, null, 0);
                box.AcceptsReturn = true;
                box.TextWrapping = TextWrapping.Wrap;
                box.Height = 64;
                box.Margin = new Thickness(0, 10, 0, 0);

                var add = Ui.Primary("Add", () =>
                {
                    var wrote = box.Text.Trim();

                    if (wrote.Length == 0) return;

                    CommentPicked(new List<object> { row }, wrote, _toast);
                    box.Text = string.Empty;
                    Redraw();
                });

                add.MinWidth = 92;
                add.HorizontalAlignment = HorizontalAlignment.Right;
                add.Margin = new Thickness(0, 8, 0, 0);

                body.Children.Add(Ui.Line(row.Info.Name ?? "this clash", 1, true));
                body.Children.Add(Ui.Line(Both(row), 0.75));

                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = trail,
                    Margin = new Thickness(0, 10, 0, 0),
                };

                var root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                root.Children.Add(body);
                Grid.SetRow(scroll, 1);
                root.Children.Add(scroll);

                var foot = Ui.Stack(box, add);
                Grid.SetRow(foot, 2);
                root.Children.Add(foot);

                var ground = new Border { Child = root, Padding = new Thickness(18, 16, 18, 16) };
                ground.Tint(Border.BackgroundProperty, "Bg");

                window.Content = ground;

                Skin.Apply(window);
                Shell.HostWindow.Own(window);

                window.Closed += (s, e) => Ui.Safely(RedrawRows);
                window.ShowDialog();
            }

            /// <summary>
            /// One remark, with the two things that can be done to it.
            /// </summary>
            /// <param name="row">The clash it is about.</param>
            /// <param name="comment">The remark.</param>
            /// <param name="redraw">What to call once it has changed.</param>
            private UIElement Remark(Row row, ClashComment comment, Action redraw)
            {
                var head = new DockPanel { LastChildFill = true };

                var verbs = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                // Only what came out of the host can be changed there. A remark added on this
                // board a moment ago and not yet written has no id to rebuild the trail around,
                // and offering an Edit that quietly does nothing is worse than not offering one.
                if (comment.HostId != 0)
                {
                    verbs.Children.Add(Ui.Icon(Glyphs.Pencil,
                        "Change what this says. The host has no edit, so it is replaced - and "
                        + "takes today's date.",
                        () => Prompt("Change this comment", comment.Body, "Save",
                                     said => Amend(row, comment, said, redraw))));

                    verbs.Children.Add(Ui.Icon(Glyphs.Bin, "Take this comment off the clash",
                        () => Erase(row, comment, redraw)));
                }

                DockPanel.SetDock(verbs, Dock.Right);
                head.Children.Add(verbs);

                var who = comment.Author
                          + (comment.WhenTicks > 0
                              ? " - " + new DateTime(comment.WhenTicks, DateTimeKind.Utc)
                                        .ToLocalTime().ToString("d MMM yyyy HH:mm",
                                                                CultureInfo.CurrentCulture)
                              : string.Empty);

                head.Children.Add(Ui.Line(who, 0.7));

                var said = new TextBlock
                {
                    Text = comment.Body,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                };

                var card = new Border
                {
                    Padding = new Thickness(10, 8, 10, 9),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Child = Ui.Stack(head, said),
                };

                card.Tint(Border.BackgroundProperty, "Card");
                card.Tint(Border.BorderBrushProperty, "Bd");

                return card;
            }

            /// <summary>
            /// The trail to show for one clash: the host's, and anything not written into it yet.
            /// </summary>
            /// <param name="row">The clash.</param>
            private List<ClashComment> Trail(Row row)
            {
                if (OnFile) return row.Trail.ToList();

                var said = _session.Clash.CommentsOn(row.TestId, row.Info.Id).ToList();

                // What the document would not take is held on the finding, and belongs in the
                // conversation as much as what it did take - it is the same remark, waiting for a
                // seat that can write it.
                var held = Found(row);

                if (held != null)
                    foreach (var comment in held.Comments)
                        if (!said.Any(c => string.Equals(c.Body, comment.Body, StringComparison.Ordinal)
                                           && string.Equals(c.Author, comment.Author,
                                                            StringComparison.OrdinalIgnoreCase)))
                            said.Add(new ClashComment(comment.Author, comment.WhenTicks,
                                                      comment.Body));

                return said.OrderBy(c => c.WhenTicks).ToList();
            }

            /// <summary>Change what one comment says, in the document and in the record.</summary>
            /// <param name="row">The clash.</param>
            /// <param name="comment">The remark being changed.</param>
            /// <param name="said">What it should say now.</param>
            /// <param name="redraw">What to call once it has changed.</param>
            private void Amend(Row row, ClashComment comment, string said, Action redraw)
            {
                said = (said ?? string.Empty).Trim();

                if (said.Length == 0 || said == comment.Body) return;

                if (!_session.Clash.EditComment(row.TestId, row.Info.Id, comment.HostId, said))
                {
                    Warned(Ui.Problem("That comment was not changed. "
                        + (_session.Clash.LastProblem ?? string.Empty)));
                    return;
                }

                Swap(row, comment.Body, said);
                Kept(row);
                redraw();
            }

            /// <summary>Take one comment off, in the document and in the record.</summary>
            /// <param name="row">The clash.</param>
            /// <param name="comment">The remark being taken off.</param>
            /// <param name="redraw">What to call once it has gone.</param>
            private void Erase(Row row, ClashComment comment, Action redraw)
            {
                if (!_session.Clash.DeleteComment(row.TestId, row.Info.Id, comment.HostId))
                {
                    Warned(Ui.Problem("That comment was not taken off. "
                        + (_session.Clash.LastProblem ?? string.Empty)));
                    return;
                }

                Swap(row, comment.Body, null);
                Kept(row);
                redraw();
            }

            /// <summary>
            /// Put the same change through the record the document just took.
            /// </summary>
            /// <param name="row">The clash.</param>
            /// <param name="was">What the comment said.</param>
            /// <param name="now">What it says now, or null when it has gone.</param>
            private void Swap(Row row, string was, string? now)
            {
                var finding = Found(row);

                if (finding != null)
                {
                    var at = finding.Comments.ToList()
                                    .FindIndex(c => string.Equals(c.Body, was, StringComparison.Ordinal));

                    if (at >= 0)
                    {
                        var old = finding.Comments[at];

                        finding.Comments.RemoveAt(at);

                        if (now != null)
                            finding.Comments.Insert(at, new FindingComment(old.Author,
                                                                          DateTime.UtcNow.Ticks, now));
                    }
                }

                var mine = row.Trail.FindIndex(c => string.Equals(c.Body, was, StringComparison.Ordinal));

                if (mine >= 0)
                {
                    var old = row.Trail[mine];

                    row.Trail.RemoveAt(mine);

                    if (now != null)
                        row.Trail.Insert(mine, new ClashComment(old.Author, old.WhenTicks, now));
                }

                row.Comments = row.Trail.Count > 0 ? row.Trail.Count : Math.Max(0, row.Comments - 1);
                row.LastComment = now ?? (row.Trail.Count > 0 ? row.Trail[row.Trail.Count - 1].Body : string.Empty);
            }

            /// <summary>Keep what just changed, and put the board back in step with it.</summary>
            /// <param name="row">The clash.</param>
            private void Kept(Row row)
            {
                _ = row;

                Moved();
                KeepFindings();
                ArmSync();
                RedrawRows();
            }

            /// <summary>Add one comment to everything picked.</summary>
            /// <param name="picked">The results.</param>
            /// <param name="body">What the comment says.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void CommentPicked(IReadOnlyList<object> picked, string body, Panel say)
            {
                body = (body ?? string.Empty).Trim();

                if (body.Length == 0)
                {
                    say.Children.Clear();
                    say.Children.Add(Ui.Problem("A comment with nothing in it is not a comment."));
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
                      (finding, row) =>
                      {
                          _ = row;
                          finding.Comments.Add(new FindingComment(author, when, body));
                      },
                      FindingField.Comments);
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
                               Action<Finding, Row>? record = null, string? field = null)
            {
                say.Children.Clear();

                var rows = Rows(picked);

                // Said rather than returned. A press that changes nothing and reports nothing is
                // indistinguishable from a press that did not arrive, which is exactly how "the
                // dropdown opens but does not change the status" gets reported.
                if (rows.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Nothing is picked, so there was nothing to "
                        + "change. Tick the rows first, or use the row's own menu."));
                    return;
                }

                var done = 0;
                var held = 0;
                var problems = new List<string>();

                // Whatever this writes changes what the filters would answer, and no signature
                // over the filter state can see inside a row.
                Moved();

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

                        var took = write(byTest.Key, ids);

                        if (!took && record == null)
                        {
                            problems.Add(_session.Clash.LastProblem ?? "The host refused.");
                            continue;
                        }

                        var refused = took ? null : _session.Clash.LastProblem ?? "The host refused.";

                        foreach (var row in byTest)
                        {
                            // The finding takes the edit either way: the database is the record
                            // and the document is a projection of it. What changes when the
                            // document refuses - a published NWD holds its results read-only - is
                            // only that the field says it is still waiting, so the sync knows
                            // what to offer and never writes the same comment twice.
                            // A row with no finding yet gets one now.
                            //
                            // The register is written by the read, and a row the read had not
                            // reached - or a result the engine renumbered under us - had no
                            // finding to hold the decision. On a document that takes the write
                            // that was invisible; on one that does not, the decision was simply
                            // lost. It is the database that is the record, so the record is
                            // made rather than the edit dropped.
                            var finding = Found(row) ?? Raised(row);

                            if (finding != null && record != null)
                            {
                                record(finding, row);

                                if (took)
                                {
                                    finding.Landed(field);
                                }
                                else
                                {
                                    finding.Hold(field ?? FindingField.Status);
                                    finding.HostProblem = refused;
                                }
                            }

                            apply(row);
                        }

                        if (took) done += ids.Count;
                        else held += ids.Count;
                    }
                }

                if (done == 0 && held == 0)
                {
                    if (problems.Count == 0)
                        problems.Add("That change could not be kept. "
                            + (_session.Findings.LastSaveProblem
                               ?? "There was nowhere to write it."));

                    foreach (var problem in problems) say.Children.Add(Ui.Problem(problem));
                    return;
                }

                if (!OnFile) { KeepFindings(); ArmSync(); }

                // Nothing is said when the document will not take it.
                //
                // A decision on this board belongs to the project, and the project is where it
                // was just kept. Whether the document could also take it is a fact about the
                // document, said once by the strip at the top of the window on a published NWD -
                // not a note on every status somebody sets all afternoon, on a seat where the
                // answer is the same every time. The changes sync into an NWF from a Manage seat
                // when there is one.

                var what = string.Format(CultureInfo.InvariantCulture, didFormat,
                                         Ui.Count(done + held, "result"));

                if (!OnFile) _session.Record(ActivityKind.Regroup, what);

                // Redrawing replaces the table whose bar the message panel lived in, so the
                // outcome goes to the screen's own log, which survives.
                Redraw();
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
                       + "instead - " + Ui.Count(WaitingCount, "result") + " waiting in all. "
                       + "Write the baseline to carry them into the NWF.";
            }


            /// <summary>
            /// The markers as an OBJ somebody else opens.
            ///
            /// <b>The other half of a marker.</b> On screen a marker is temporary graphics, which
            /// is right for the person driving Navisworks and useless for everybody else. This is
            /// the file: the same status-coloured balls, at the same points, written as geometry
            /// that Revit, Blender, Rhino, a viewer or a phone will open without this plug-in and
            /// without Navisworks. It is how a clash reaches a subcontractor.
            ///
            /// <b>The tree has to be useful at the other end.</b> A group per test and an object
            /// per clash, each named what the engine named it and each carrying its status
            /// colour - so the receiving tool shows a line per test that expands to the clashes,
            /// and somebody can say "Clash117" back down the phone.
            ///
            /// <b>Metres.</b> OBJ carries no units and every reader assumes metres, so this is
            /// where the document's own units are converted - once, here.
            /// </summary>
            /// <param name="rows">The results to write.</param>
            /// <param name="what">What they are called, for the file name and the report.</param>
            /// <param name="say">Where to report what happened.</param>
            private void ExportMarkers(IReadOnlyList<Row> rows, string what, Panel say)
            {
                say.Children.Clear();

                if (rows.Count == 0) return;

                var mesh = ClashMarkers.Write(
                    rows.Select(r => new ClashItem(r.Info.Key(), r.TestName)
                    {
                        Name = r.Info.Name,
                        X = r.Info.PointX,
                        Y = r.Info.PointY,
                        Z = r.Info.PointZ,
                        Status = Enum.TryParse<FindingStatus>(r.Status, true, out var s)
                            ? s
                            : FindingStatus.New,
                    }).ToList(),
                    new MarkerRecipe
                    {
                        // Real metres: the file is in metres, so the size somebody typed in
                        // millimetres converts straight and the document's units do not come into
                        // it. They come into the coordinates instead, below.
                        RadiusMetres = _markerAcross / 2000.0,

                        // A file board's points are already metres; a model board's are in the
                        // document's units and this is the one place that is put right.
                        MetresPerUnit = OnFile ? 1.0 : _session.MetresPerUnit,
                    });

                if (mesh.Refusal != null)
                {
                    say.Children.Add(Ui.Problem(mesh.Refusal));
                    return;
                }

                foreach (var warning in mesh.Warnings) say.Children.Add(Ui.Note(warning));

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the marker spheres",
                    Filter = "Wavefront OBJ|*.obj",
                    FileName = "clash-markers.obj",
                    AddExtension = true,
                    DefaultExt = ".obj",
                };

                if (!Ui.Asked(dialog)) return;

                var obj = dialog.FileName;
                var mtl = System.IO.Path.ChangeExtension(obj, ".mtl");

                try
                {
                    // The .obj names the .mtl beside it, so the two travel together or the
                    // colours are lost at the other end without anything saying why.
                    System.IO.File.WriteAllText(obj, mesh.Text.Replace(
                        "mtllib " + mesh.MaterialFile,
                        "mtllib " + System.IO.Path.GetFileName(mtl)));

                    if (mesh.Materials.Length > 0) System.IO.File.WriteAllText(mtl, mesh.Materials);
                }
                catch (Exception e)
                {
                    say.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "exported clash markers", mesh.Explain());

                var tests = rows.Select(r => r.TestName).Distinct(StringComparer.Ordinal).Count();

                say.Children.Add(Ui.Done("Wrote " + mesh.Explain() + " for " + what + " to "
                    + obj + " - " + (tests == 1
                        ? "one group, named after the test"
                        : Ui.Count(tests, "group") + ", one per test")
                    + ", each clash a named object coloured by status. Send the .mtl beside it or "
                    + "the colours are lost."));
            }

            /// <summary>
            /// Hide the markers, or put back the ones that were hidden.
            ///
            /// <b>One press, both ways.</b> Hiding used to be a one-way door: the markers went
            /// down and the only way back was to pick the same rows again and drop them afresh,
            /// which on a row somebody had since scrolled past is no way back at all.
            /// </summary>
            /// <param name="say">Where to report what happened.</param>
            private void Unmark(Panel say)
            {
                say.Children.Clear();

                if (_session.View.HideMarkers() == 0 && _session.View.CanUnhideMarkers)
                    _session.View.UnhideMarkers();
            }

            /// <summary>What the marker toggle should say right now.</summary>
            private string MarkerToggle() =>
                _session.View.CanUnhideMarkers && !MarkerOverlay.Any
                    ? "Show the markers again"
                    : "Hide the markers";

            /// <summary>Put the switch in the state the markers are actually in.</summary>
            private void RedrawMarkerToggle() =>
                _markerToggle.Content = Ui.Check("Show the markers", MarkerOverlay.Any, on =>
                {
                    if (on == MarkerOverlay.Any) return;

                    Unmark(_toast);
                    RedrawMarkerToggle();
                });

            /// <summary>
            /// The balls for these rows, in document units.
            /// </summary>
            /// <param name="rows">The rows to mark.</param>
            /// <param name="scale">What a row's stored coordinate is multiplied by.</param>
            /// <param name="radius">How big each ball is, in document units.</param>
            /// <returns>One marker per row.</returns>
            private static List<SceneMarker> Marks(IReadOnlyList<Row> rows, double scale,
                                                   double radius) =>
                rows.Select(r => new SceneMarker(
                        r.Info.PointX * scale, r.Info.PointY * scale, r.Info.PointZ * scale, radius,
                        ClashMarkers.Colour(Enum.TryParse<FindingStatus>(r.Status, true, out var s)
                            ? s
                            : FindingStatus.New)))
                    .ToList();

            // -------------------------------------------------------------------------------
            // Property columns and the grouping chain
            // -------------------------------------------------------------------------------

            private void AddColumn()
            {
                var name = _newProp.Trim();

                if (name.Length == 0)
                {
                    Warned(Ui.Problem("Pick or type the property first."));
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
                Indexed();


                RedrawColumnList();
                RedrawBoard();
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
                            Indexed();
                            RedrawColumnList();
                            RedrawBoard();
                        })));
                }
            }

            /// <summary>
            /// Read every custom column's value off the clashing elements themselves.
            ///
            /// <b>Through the engine, not the index.</b> This used to take each row's element
            /// keys and resolve them back through the document, and a key that is not an instance
            /// GUID makes the first such resolve index every geometry leaf in the federation. The
            /// clash engine is holding both elements of every result already, so there is nothing
            /// to look up - the properties are read straight off them.
            ///
            /// Every test, deliberately: a column belongs to the board, not to whichever test
            /// happens to be open, so adding one adds it everywhere and this fills it everywhere.
            /// </summary>
            /// <param name="job">The runner's job, for progress and for stopping.</param>
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

                if (OnFile)
                {
                    _fill.Children.Add(Ui.Problem("A board read from a file has no elements "
                        + "behind it in this model, so there is nothing to read the values off. "
                        + "Read from the model to fill property columns."));
                    return;
                }

                var wanted = _columns
                    .Select(c => new ClashPropertyWanted(c.Id, c.Tab, c.Name, Sided(c.Side)))
                    .ToList();

                job.Total = read.Sum(e => e.Rows.Count);
                job.Say("Reading the values off the clashing elements...");

                var byId = new Dictionary<string, Row>(StringComparer.Ordinal);
                var filled = 0;
                var blank = 0;
                var done = 0;

                foreach (var entry in read)
                {
                    byId.Clear();

                    foreach (var row in entry.Rows) byId[row.Info.Id] = row;

                    foreach (var answer in _session.Clash.ValuesFor(
                                 entry.Test.Id, wanted, _ => job.Step(++done, "results read")))
                    {
                        if (!byId.TryGetValue(answer.ResultId, out var row)) continue;

                        foreach (var column in _columns)
                        {
                            var value = answer.Values.TryGetValue(column.Id, out var got)
                                ? got
                                : string.Empty;

                            row.SetCustom(column.Id, value);

                            if (value.Length == 0) blank++; else filled++;
                        }
                    }

                    if (job.IsCancelled) break;
                }

                RedrawBoard();

                _fill.Children.Add(job.IsCancelled
                    ? Ui.Note("Stopped. " + Ui.Count(filled, "value") + " were read before you "
                              + "asked it to stop; the rest of the cells are still empty.")
                    : (UIElement)Ui.Done(Ui.Count(filled, "value") + " read into "
                        + Ui.Count(_columns.Count, "column") + " across "
                        + Ui.Count(read.Count, "test") + "."
                        + (blank > 0
                            ? " " + Ui.Count(blank, "cell") + " came back empty - elements that "
                              + "do not carry the property."
                            : string.Empty)));
            }

            /// <summary>The side a column reads from, as the seam names it.</summary>
            /// <param name="side">The side as the screen words it.</param>
            private static ClashElementSide Sided(string side) =>
                side == FirstSide ? ClashElementSide.First
                : side == SecondSide ? ClashElementSide.Second
                : ClashElementSide.Both;

            /// <summary>
            /// Export a snapshot of each picked result, wherever the person says.
            ///
            /// It used to write into a "clash-snapshots" folder beside the project file and say
            /// so on a message that faded, which on a seat with no project file meant the pictures
            /// landed in the temp folder and nothing told anybody where. "The snapshot button
            /// doesn't do anything at all" was that: it did the work somewhere nobody looked. A
            /// save dialog is asked first, so the files land where they were sent and the person
            /// is standing in the folder when they do.
            /// </summary>
            /// <param name="picked">The results to picture.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void Snapshots(IReadOnlyList<object> picked, Panel say)
            {
                say.Children.Clear();

                if (OnFile)
                {
                    Warned(Ui.Problem("Snapshots are rendered by the clash engine, so "
                        + "they need the model source."));
                    return;
                }

                var rows = Rows(picked);

                if (rows.Count == 0)
                {
                    Warned(Ui.Problem("Nothing is picked, so there was nothing to picture."));
                    return;
                }

                var first = Safe(rows[0].TestName) + "_" + Safe(rows[0].Info.Name ?? rows[0].Info.Id);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = rows.Count == 1
                        ? "Export a snapshot"
                        : "Export " + Ui.Count(rows.Count, "snapshot")
                          + " — pick the first one's name, the rest land beside it",
                    Filter = "PNG image|*.png",
                    FileName = first + ".png",
                    AddExtension = true,
                    DefaultExt = ".png",
                };

                if (!Ui.Asked(dialog)) return;

                var chosen = dialog.FileName;
                var folder = System.IO.Path.GetDirectoryName(chosen) ?? System.IO.Path.GetTempPath();

                var written = 0;
                var failed = string.Empty;

                Ui.Working("Rendering " + Ui.Count(rows.Count, "snapshot"), job =>
                {
                    for (var i = 0; i < rows.Count; i++)
                    {
                        if (job.IsCancelled) break;

                        var row = rows[i];
                        job.Step(i + 1, rows.Count, row.Info.Name ?? row.Info.Id);

                        var bytes = _session.Clash.ResultSnapshot(row.TestId, row.Info.Id, 1280, 720);
                        if (bytes == null) continue;

                        // The one the dialog named keeps the name it was given; the rest are
                        // named for the clash, because a folder of "snapshot (3).png" tells
                        // nobody which clash they are looking at.
                        var path = rows.Count == 1 || i == 0
                            ? chosen
                            : System.IO.Path.Combine(
                                folder,
                                Safe(row.TestName) + "_" + Safe(row.Info.Name ?? row.Info.Id) + ".png");

                        try
                        {
                            System.IO.File.WriteAllBytes(path, bytes);
                            written++;
                        }
                        catch (Exception e)
                        {
                            if (failed.Length == 0) failed = Ui.Describe(e);
                        }
                    }
                });

                Warned(written == 0
                    ? (UIElement)Ui.Problem(failed.Length > 0
                        ? "Nothing could be written: " + failed
                        : _session.Clash.LastProblem ?? "The host rendered none of them.")
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
                    Warned(Ui.Problem("Read the results first."));
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
                    _log.Children.Clear();
                    _log.Children.Add(Ui.Line("No two results across these tests are the same "
                                              + "clash.", 0.8));
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

                    Redraw();
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
                    Warned(Ui.Problem("Read the results first."));
                    return;
                }

                // The whole read, not the test on the board: a baseline that carried one test
                // is a review round the other seventeen tests are missing from.
                var from = read;

                var items = new List<ClashItem>();
                var scale = OnFile ? 1.0 : _session.MetresPerUnit;

                // Which item each result became, so the trails and the ids can find it again.
                var carrying = new Dictionary<string, ClashItem>(StringComparer.Ordinal);

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

                        // Back out as it came in, so a reviewer's priority is not reset by the
                        // act of sending them the file again.
                        Review = row.Review,
                    };

                    foreach (var comment in row.Trail) item.Comments.Add(comment);

                    items.Add(item);
                }

                if (items.Count == 0)
                {
                    Warned(Ui.Problem("Nothing here can be written: no result names elements by key."));
                    return;
                }

                // The GlobalIds are what let the receiving tool select the two clashing
                // elements. Read before writing, under the wait cursor: on a first export of a
                // large board this is the slow part, and it is not a press that looks slow.
                var named = 0;

                // Watched, with a Stop. This is a key resolution per element of the whole board -
                // on a board of a few hundred results, the first export after a model change pays
                // for the federation index and then a lookup each. It used to run under nothing
                // but a wait cursor with the job passed as null, which is a press that looks like
                // a host that has hung.
                if (!OnFile)
                    Ui.Working("Reading IFC ids for the export...",
                               job => named = ClashIdentity.Stamp(_session, items, job));

                // Which test and result each item came from, so the clash engine can draw it
                // rather than the camera having to be moved.
                var drawnFrom = new Dictionary<ClashKey, (string Test, string Result)>();

                foreach (var row in from.SelectMany(e => e.Rows))
                    if (!row.Info.A.IsEmpty && !row.Info.B.IsEmpty)
                        drawnFrom[row.Info.Key()] = (row.TestId, row.Info.Id);

                var written = ClashBcf.Write(items, null, new ClashBcfOptions
                {
                    Snapshot = _pictures && !OnFile
                        ? ClashIdentity.Pictures(_session, 1280, 720,
                              item => drawnFrom.TryGetValue(item.Key, out var at)
                                  ? at
                                  : ((string Test, string Result)?)null)
                        : null,
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

                if (!Ui.Asked(dialog)) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName, written.Bytes);
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote a clash baseline", written.Explain());

                // Into the footer, where the read's own counts go - not the strip over the board.
                // A press that produces a file has to say where the file went, which is the one
                // thing about it nobody can see.
                _log.Children.Clear();
                _log.Children.Add(Ui.Done("Wrote " + dialog.FileName + " - " + written.Explain()));

                // What the file itself says about what it can and cannot do in somebody else's
                // tool - chiefly how many topics name an element that tool can select. This was
                // written and never read here, so the one fact a coordinator needs before sending
                // the file was only ever visible on the other export screen.
                foreach (var warning in written.Warnings) Said(Ui.Note(warning));

                if (!OnFile)
                {
                    // Only the good news. The bad news is the warning above, which counts the
                    // topics actually written rather than the ids read, and saying both is the
                    // same fact twice in two different numbers.
                    var about = ClashIdentity.Explain(items.Count, named);

                    if (about != null && named >= items.Count * 2) Said(Ui.Line(about, 0.8));

                    if (_pictures)
                        Said(written.Snapshots == 0
                            ? Ui.Note("No picture could be drawn - this document cannot render "
                                      + "off-screen. Everything else in the file is unaffected.")
                            : Ui.Line(Ui.Count(written.Snapshots, "clash")
                                      + " carries a picture.", 0.8));
                }
            }

            /// <summary>The results grid, exactly as filtered and shown, as a spreadsheet.</summary>
            private void Sheet()
            {
                if (_shown.Count == 0)
                {
                    Warned(Ui.Problem("Nothing is on the grid to write."));
                    return;
                }

                var headers = new List<string>
                {
                    "Test", "Result", "Group", "Status", "Worst",
                };

                foreach (var column in _columns) headers.Add(column.Title);

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
                        Millimetres(row),
                    };

                    foreach (var column in _columns)
                        cells.Add(row.Custom.TryGetValue(column.Id, out var value)
                            ? value
                            : string.Empty);


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

                if (!Ui.Asked(dialog)) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName,
                                                 XlsxWriter.Write("Clash results", block));
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the clash results grid",
                                Ui.Count(_shown.Count, "row") + " to " + dialog.FileName);

                _log.Children.Clear();
                _log.Children.Add(Ui.Done("Wrote " + Ui.Count(_shown.Count, "row") + " to "
                                          + dialog.FileName));
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
                    Warned(Ui.Problem("Read a file first — the markup is its edited state."));
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
                    Warned(Ui.Problem("Nothing in that file carries a CamelWorks key, so a markup "
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
                    Warned(Ui.Problem("Could not write the markup: " + Ui.Describe(e)));
                    return;
                }

                var note = "Saved your markup: " + path + " — " + written.Explain()
                           + (skipped > 0
                               ? " " + Ui.Count(skipped, "unkeyed result") + " could not be "
                                 + "included."
                               : string.Empty);

                _log.Children.Clear();
                _log.Children.Add(Ui.Done(note));
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

                var board = Current();

                if (board == null)
                {
                    _sync.Children.Add(Ui.Problem("Read the results first - the sync matches the "
                        + "reviewers' markups against the board it is looking at."));
                    return;
                }

                job.Say("Matching the markups against the board...");

                // The model's results, indexed two ways, both pointing at the same record so a
                // second file sees what the first one asked for.
                //
                // By topic GUID first, because that is the index that survives somebody else's
                // tool: it is recomputed here from the model rather than read out of the file, so
                // a markup returned from Solibri or Revizto - stripped of our sidecar and of the
                // element keys we wrote - still lands on the right results. By clash key second,
                // for a file that still carries ours.
                var byTopic = new Dictionary<string, Live>(StringComparer.OrdinalIgnoreCase);
                var byKey = new Dictionary<string, Live>(StringComparer.Ordinal);

                // Built lazily, because it is twenty-seven times the size and an ordinary sync
                // never needs it.
                Dictionary<string, Live>? near = null;

                // From the board, not from the host.
                //
                // This used to walk every test and every result of the document again to build
                // these two indexes - a second full harvest of exactly what the board read a
                // moment ago and is still holding. On a job of any size that was the longest part
                // of a sync, and it was spent re-learning what was already in memory.
                var read = 0;

                foreach (var entry in board)
                {
                    foreach (var row in entry.Rows)
                    {
                        if (!job.Step(++read, "results matched")) return;

                        var key = row.Info.Key();

                        var live = new Live
                        {
                            Key = key,
                            TestId = entry.Test.Id,
                            ResultId = row.Info.Id,
                            Status = row.Status,
                            Assigned = row.Assigned,
                            Group = row.Group,
                        };

                        byKey[key.ToString()] = live;

                        if (!key.IsEmpty) byTopic[ClashBcf.TopicGuidFor(key)] = live;
                    }
                }

                // Which tests the markups actually reached, so the refresh afterwards reads
                // those and leaves the rest of the job alone.
                var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var statuses = 0;
                var assigns = 0;
                var comments = 0;
                var groups = 0;
                var missing = 0;
                var notes = 0;
                var kept = 0;
                var problems = new List<string>();

                // The topics no clash in this model answers to. They are somebody's own findings,
                // raised while they had the file, and the issue register is where a problem with
                // no clash behind it belongs.
                var strangers = new List<CamelWorks.Core.OpenBim.BcfTopic>();

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
                        Live? found = null;

                        // The topic's own GUID first. A file that has been through another tool
                        // has nothing else left that names our model.
                        if (result.FileGuid != null) byTopic.TryGetValue(result.FileGuid, out found);

                        if (found == null && result.Item != null)
                            byKey.TryGetValue(result.Item.Key.ToString(), out found);

                        // A clash whose point drifted across a cell boundary between runs is
                        // still that clash. The near index is built once, and only if something
                        // failed to match exactly - on an ordinary sync it is never built at all.
                        if (found == null && result.FileGuid != null)
                        {
                            near ??= Near(byKey.Values);

                            near.TryGetValue(result.FileGuid, out found);
                        }

                        if (found == null)
                        {
                            missing++;

                            var raised = markup.Topics
                                .FirstOrDefault(t => t != null && t.Guid == result.FileGuid);

                            if (raised != null) strangers.Add(raised);

                            continue;
                        }

                        var status = result.Status.ToString();

                        if (!string.Equals(status, found.Status, StringComparison.OrdinalIgnoreCase))
                            Bucket(setStatus, (found.TestId, status), found.ResultId);

                        var who = result.AssignedTo ?? string.Empty;

                        if (who.Length > 0
                            && !string.Equals(who, found.Assigned, StringComparison.Ordinal))
                            Bucket(setAssign, (found.TestId, who), found.ResultId);

                        var band = result.Item?.CarriedGroup ?? result.Group ?? string.Empty;

                        if (!string.Equals(band, found.Group, StringComparison.Ordinal))
                            Bucket(setGroup, (found.TestId, band), found.ResultId);

                        foreach (var comment in result.Comments)
                            say.Add((found.TestId, found.ResultId, comment));

                        // Kept whatever the tick says. The tick decides whether it is ALSO
                        // written as a comment - which is about making it visible to the native
                        // clash window and to the next tool down the line, not about storing it.
                        // Losing what somebody set because a checkbox was off would be the same
                        // failure this round trip exists to prevent.
                        var review = ClashBcf.Review(result, result.Name,
                                                     System.IO.Path.GetFileName(file),
                                                     DateTime.UtcNow.Ticks);

                        if (review != null)
                        {
                            review.TestId = found.TestId;
                            review.ResultId = found.ResultId;

                            // Onto the finding, which is the record. The board reads it from
                            // there, the next export writes it back out, and it survives the
                            // document being one the host will not write into.
                            review.ApplyTo(Found(found.TestId, found.ResultId));

                            kept++;

                            if (_noteAsides)
                            {
                                say.Add((found.TestId, found.ResultId,
                                         new ClashComment("CamelWorks", DateTime.UtcNow.Ticks,
                                             "Set in the reviewer's tool, which this model has no "
                                             + "field for: " + review.Explain() + ".")));

                                notes++;
                            }
                        }

                        // Later files see the state this one just asked for. The record is shared
                        // by both indexes, so it does not matter which one found it.
                        found.Status = status;
                        found.Assigned = who.Length > 0 ? who : found.Assigned;
                        found.Group = band;
                    }

                    foreach (var batch in setStatus)
                    {
                        touched.Add(batch.Key.Test);

                        if (_session.Clash.SetStatus(batch.Key.Test, batch.Value, batch.Key.Status))
                            statuses += batch.Value.Count;
                        else
                            problems.Add(_session.Clash.LastProblem ?? "A status write was refused.");
                    }

                    foreach (var batch in setAssign)
                    {
                        touched.Add(batch.Key.Test);

                        if (_session.Clash.Assign(batch.Key.Test, batch.Value, batch.Key.Who))
                            assigns += batch.Value.Count;
                        else
                            problems.Add(_session.Clash.LastProblem ?? "An assignment was refused.");
                    }

                    foreach (var batch in setGroup)
                    {
                        touched.Add(batch.Key.Test);

                        if (_session.Clash.Regroup(batch.Key.Test, batch.Value, batch.Key.Group))
                            groups += batch.Value.Count;
                        else
                            problems.Add(_session.Clash.LastProblem ?? "A regroup was refused.");
                    }

                    // Batched the way status and assignment already were. One host edit per
                    // comment meant a sync of two hundred notes was two hundred edits, each one
                    // walking the test it lands in to find its result.
                    foreach (var batch in say
                                 .GroupBy(c => (c.Test, c.Comment.Author, c.Comment.Body)))
                    {
                        var ids = batch.Select(c => c.Id).Distinct(StringComparer.Ordinal)
                                       .ToList();

                        // The adapter skips a comment the result already carries word for word,
                        // so re-running a sync never doubles the conversation.
                        touched.Add(batch.Key.Test);

                        if (_session.Clash.Comment(batch.Key.Test, ids,
                                                   batch.Key.Body, batch.Key.Author))
                            comments += ids.Count;
                    }
                }

                if (kept > 0) KeepFindings();

                var raisedNote = Raise(strangers, named);

                _session.Record(ActivityKind.Regroup, "synced " + Ui.Count(files.Count, "markup")
                    + " into the model", statuses + " statuses, " + comments + " comments, "
                    + assigns + " assignments");

                // Only the tests the markups landed in, and without a second progress window on
                // top of the one this job is already showing.
                RereadTests(touched.ToList(),
                    "Synced " + Ui.Count(files.Count, "markup") + ": "
                    + Ui.Count(statuses, "status change") + ", "
                    + Ui.Count(comments, "comment") + ", "
                    + Ui.Count(assigns, "assignment") + ", "
                    + Ui.Count(groups, "regrouped result") + "."
                    + (kept > 0
                        ? " " + Ui.Count(kept, "clash") + " carries a priority, a due date or a "
                          + "tag this model has no field for; the board shows "
                          + (kept == 1 ? "it" : "them") + " and the next export sends "
                          + (kept == 1 ? "it" : "them") + " back out"
                          + (notes > 0 ? ", and each is noted on the clash too." : ".")
                        : string.Empty)
                    + (raisedNote ?? (missing > 0
                        ? " " + Ui.Count(missing, "topic") + " could not be placed on this model: "
                          + "either raised in the reviewer's own tool, or resolved and gone from a "
                          + "newer run."
                        : string.Empty)), problems);
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
            /// <summary>
            /// Read the whole document again, for an edit that changed which tests there are.
            ///
            /// <b>Only when the test list moved.</b> Deleting or moving tests changes the rail
            /// itself, so there is nothing to refresh in place. Everything else names the tests it
            /// touched and goes through <see cref="RereadTests"/>, which reads those and leaves
            /// the rest of the document alone — a rename used to re-harvest a hundred and sixty
            /// tests to change one word.
            ///
            /// Shown while it runs. This is a walk of every result in the document from inside a
            /// button press, and behind a wait cursor it is indistinguishable from a plug-in that
            /// has stopped.
            /// </summary>
            /// <param name="did">What to say afterwards.</param>
            /// <param name="problems">Anything the host refused, or null.</param>
            private void Reread(string did, List<string>? problems)
            {
                var carried = Harvest(_read);
                var entries = new List<TestEntry>();

                Ui.Working("Reading the results again", (onProgress, keepGoing) =>
                {
                    foreach (var read in _session.Clash.ReadEverything(
                                 seen => { onProgress(seen); return keepGoing(); }))
                    {
                        var entry = new TestEntry(read.Test) { Shape = read.Shape };

                        foreach (var result in read.Results)
                            entry.Rows.Add(Restored(new Row(read.Test.Id, read.Test.Name, result),
                                                    carried));

                        entries.Add(entry);
                    }
                });

                if (entries.Count > 0) _read = entries;

                Moved();
                Redraw();
                Tell(did, problems);
            }

            /// <summary>
            /// Read back only the tests an edit actually touched.
            ///
            /// A run, a renumber or a compact changes the results of the tests it names and
            /// nothing else. Re-reading the whole document for that is a walk of every result on
            /// the job to refresh a handful, and it is the shape the board was already frozen by
            /// once.
            /// </summary>
            /// <param name="testIds">The tests whose results changed.</param>
            /// <param name="did">What to say afterwards.</param>
            /// <param name="problems">Anything the host refused, or null.</param>
            private void RereadTests(IReadOnlyList<string> testIds, string did,
                                     List<string>? problems)
            {
                var board = _read;

                if (board == null || testIds == null || testIds.Count == 0)
                {
                    Reread(did, problems);
                    return;
                }

                var carried = Harvest(board);
                var wanted = new HashSet<string>(testIds, StringComparer.OrdinalIgnoreCase);

                foreach (var entry in board)
                {
                    if (!wanted.Contains(entry.Test.Id)) continue;

                    entry.Rows.Clear();

                    foreach (var result in _session.Clash.Results(entry.Test.Id))
                        entry.Rows.Add(Restored(new Row(entry.Test.Id, entry.Test.Name, result),
                                                carried));
                }

                Moved();
                Redraw();
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
            /// The custom column values travel across a re-read because they cost a property read
            /// each and would otherwise be re-fetched for no reason. Everything a coordinator
            /// decided travels differently — it is on the finding, and laid over the row once the
            /// read has been taken into the register, which is <see cref="Apply"/>.
            /// </summary>
            private Row Restored(Row row,
                                 Dictionary<string, Dictionary<string, string>> carried)
            {
                if (carried.TryGetValue(row.TestId + "\u001f" + row.Info.Id, out var values))
                    foreach (var pair in values) row.SetCustom(pair.Key, pair.Value);

                return row;
            }

            /// <summary>Write the findings out, so a decision survives closing Navisworks.</summary>
            private void KeepFindings()
            {
                // Failing to save is an ordinary outcome — a document nobody has saved has no
                // project file — and never a reason to refuse the edit: it still applies on the
                // board and still travels out in the baseline.
                _session.Findings.Save();
            }

            /// <summary>
            /// Lay a finding over the row it belongs to, so the board reads the way the
            /// coordinator left it rather than the way a read-only document insists it is.
            /// </summary>
            private static void Apply(Finding? finding, Row row)
            {
                if (finding == null) return;

                row.Status = FindingStatusLattice.ToName(finding.Status);

                if (finding.AssignedTo != null) row.Assigned = finding.AssignedTo;
                if (finding.Group != null) row.Group = finding.Group;

                // Only the remarks the host has not got: the rest are already on the row, read
                // out of the document a moment ago.
                foreach (var comment in finding.Comments)
                {
                    if (comment.InHost) continue;

                    row.Trail.Add(new ClashComment(comment.Author, comment.WhenTicks, comment.Body));
                    row.Comments++;
                    row.LastComment = comment.Body;
                }

                // What the host has no field for at all — priority, due date, stage, a reviewer's
                // own tags — so the board can show a column the document cannot.
                row.Review = ClashAside.Of(finding);
            }

            /// <summary>
            /// Offer the held edits to the document again, and keep only what it still refuses.
            ///
            /// The case this is for: the coordinator marked up the NWD on site, then opened the
            /// NWF the results actually live in. One press moves the week's decisions across.
            /// </summary>
            private void PushHeld()
            {
                var waiting = Waiting.ToList();

                if (waiting.Count == 0) return;

                var written = 0;
                var refused = 0;
                var problems = new List<string>();

                foreach (var finding in waiting)
                {
                    if (!finding.Facts.TryGetValue(ClashFindings.HostTest, out var test)
                        || !finding.Facts.TryGetValue(ClashFindings.HostId, out var result)
                        || test == null || result == null)
                    {
                        // Nothing to write into: this finding's result is not in the document
                        // that is open. It stays waiting, which is the honest answer.
                        refused++;
                        continue;
                    }

                    var ids = new[] { result };
                    var landed = new List<string>();

                    foreach (var field in finding.Pending.ToList())
                    {
                        var ok = field switch
                        {
                            FindingField.Status =>
                                Kept(_session.Clash.SetStatus(
                                    test, ids, FindingStatusLattice.ToName(finding.Status)), problems),

                            FindingField.AssignedTo =>
                                Kept(_session.Clash.Assign(
                                    test, ids, finding.AssignedTo ?? string.Empty), problems),

                            FindingField.Group =>
                                Kept(_session.Clash.Regroup(
                                    test, ids, finding.Group ?? string.Empty), problems),

                            FindingField.Title =>
                                Kept(_session.Clash.RenamePicked(test, ids, finding.Title), problems),

                            FindingField.Comments => Say(test, ids, finding, problems),

                            _ => true,
                        };

                        if (ok) landed.Add(field);
                    }

                    foreach (var field in landed) finding.Landed(field);

                    if (finding.IsHeld) refused++;
                    else written++;
                }

                KeepFindings();
                ArmSync();
                Redraw();

                Tell(written == 0
                        ? "This document still will not take them. They are still kept here."
                        : Ui.Count(written, "held edit") + " written into the document"
                          + (refused > 0
                              ? "; " + Ui.Count(refused, "other") + " still refused and still kept."
                              : ". Nothing is waiting now."),
                     problems.Distinct().Take(3).ToList());
            }

            /// <summary>
            /// Offer the remarks the document has not got, and say whether it took all of them.
            ///
            /// The adapter skips a comment the result already carries word for word, so a partial
            /// failure here is a real refusal rather than a double.
            /// </summary>
            private bool Say(string test, IReadOnlyList<string> ids, Finding finding,
                             List<string> problems)
            {
                var all = true;

                foreach (var comment in finding.Comments)
                {
                    if (comment.InHost) continue;

                    all &= Kept(_session.Clash.Comment(test, ids, comment.Body, comment.Author),
                                problems);
                }

                return all;
            }

            /// <summary>One host answer, with its complaint collected the first time it is heard.</summary>
            private bool Kept(bool ok, List<string> problems)
            {
                if (!ok) problems.Add(_session.Clash.LastProblem ?? "The host refused.");

                return ok;
            }

            /// <summary>
            /// The Save button, kept because what it writes depends on which source is open.
            ///
            /// "Save the changes" is true and says nothing: from the model it writes the baseline
            /// the whole review round is built on, and from a file it writes the markup a Manage
            /// seat reads back. Both are the file somebody came here to produce, and a button
            /// that will not name it is how the most useful thing on a screen goes unnoticed.
            /// </summary>
            private System.Windows.Controls.Button? _saveButton;

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

                // These say why the DOCUMENT cannot take the changes - never why they cannot be
                // saved. They are saved either way; this is the second half that is unavailable.
                if (!_session.Clash.IsAvailable)
                    return "This Navisworks edition has no clash engine, so there are no clash "
                           + "results in the file to write into. "
                           + (_session.ClashProblem ?? string.Empty).Trim();

                if (_session.IsPublished)
                    return "This is a published NWD, and a published file holds its clash results "
                           + "read-only. Open the NWF the models were federated in and the same "
                           + "changes go into it.";

                if (!_session.Clash.CanEdit)
                    return "This document's clash results will not take an edit. Open the NWF the "
                           + "models were federated in and the same changes go into it.";

                return null;
            }

            /// <summary>
            /// Say what the footer's one primary will write, and whether it can.
            ///
            /// The design calls this button "Save the changes", and every edit on this board has
            /// already been written — the chip on the row asks the host the moment it is picked.
            /// What is left to save is what the host refused, which a published NWD always does,
            /// so the button names that: it is the sync, and its caption counts what is waiting
            /// for it rather than promising a save nobody needs.
            ///
            /// Kept visible when it cannot run, with the reason on it. A button that disappears
            /// teaches nobody that the sync exists, and it is the thing somebody came for.
            /// </summary>
            private void ArmSync()
            {
                if (_saveButton == null) return;

                var why = WhySync();
                var waiting = OnFile ? 0 : WaitingCount;

                // Never off. Saving always has somewhere to go: the project database is where the
                // decisions live, and writing them into the document is the extra step that a
                // published NWD or a Simulate seat cannot take. A greyed-out Save on those said
                // the work could not be saved at all, which was never true.
                _saveButton.IsEnabled = true;

                _saveButton.Content = OnFile
                    ? "Save the markup"
                    : waiting == 0 || why != null ? "Save the changes"
                    : "Save, and write " + Ui.Count(waiting, "change") + " into the document";

                _saveButton.ToolTip = OnFile
                    ? "Writes what is on this board into a markup beside the file it came from, "
                      + "which a Manage seat reads back."
                    : why == null
                        ? "Saves the decisions to this project, and writes them into the "
                          + "document's own clash results as well, where the native clash window "
                          + "and every export can see them."
                        : "Saves the decisions to this project, which is where they live. They "
                          + "cannot also be written into the document itself: " + Lowered(why);
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

            /// <summary>
            /// Save the work. Always to this project; into the document as well where it can go.
            ///
            /// <b>What "save" means here.</b> The decisions on this board - a status, an
            /// assignee, a group, a comment, a due date the host has no field for - belong to the
            /// project, and the project database is where they are kept. Writing them into the
            /// document's own clash results is a second thing, worth doing because the native
            /// clash window and every export read from there, but it is not what makes the work
            /// safe and it is not always possible.
            ///
            /// A published NWD holds its clash results read-only. A Simulate seat has no clash
            /// engine at all. Both of those used to grey this button out, which told somebody
            /// their afternoon's decisions could not be saved - when in fact they were already
            /// kept and would travel to whoever opened the NWF next. Saving to the database is
            /// the default and the whole of it; the document write is the part that is offered
            /// when the document will take it.
            /// </summary>
            private void SaveWork()
            {
                var stored = _session.Findings.Save();
                var why = WhySync();

                if (why == null && WaitingCount > 0)
                {
                    // The document will take them, and some are waiting. PushHeld writes them and
                    // says what landed.
                    PushHeld();
                    return;
                }

                if (!stored)
                {
                    Tell("The decisions on this board could not be saved.",
                         new List<string>
                         {
                             _session.Findings.LastSaveProblem
                             ?? "There was nowhere to write the project file.",
                         });

                    return;
                }

                if (why == null)
                {
                    Tell("Saved. Every change on this board is in the project and in the "
                         + "document's own clash results.", null);
                    return;
                }

                Tell("Saved to this project. " + Ui.Count(_shown.Count, "result")
                     + " on this board carries what you decided, and it travels with the project "
                     + "rather than with the file that is open.",
                     new List<string>
                     {
                         "The document itself was not written into: " + Lowered(why),
                     });
            }

            /// <summary>The same sentence, joined onto one that has already started.</summary>
            /// <param name="said">What to lower the first letter of.</param>
            private static string Lowered(string said) =>
                said.Length == 0 ? said : char.ToLowerInvariant(said[0]) + said.Substring(1);

            /// <summary>List what is waiting, so somebody can see it before deciding anything.</summary>
            private void ShowHeld()
            {
                var waiting = Waiting
                    .OrderBy(f => f.Rule, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (waiting.Count == 0)
                {
                    Said(Ui.Line("Nothing is waiting.", 0.75));
                    return;
                }

                // Over the board, and held there. A list somebody asked to see is not a
                // confirmation, and three seconds is not long enough to read one.
                Said(Ui.Eyebrow("Waiting to be written"));
                Hold();

                var table = Ui.Table(
                    waiting.Select(f => new TableRow(f, f.Rule, f.Title, f.Describe())),
                    ("Test", 200), ("Result", 140), ("What is held", 0));

                _toast.Children.Add(table);

                _toast.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(table, picked), say, "held edit")),
                    new Ui.OnPicked("Forget {n}", (picked, say) =>
                    {
                        // Forgetting is only about the wait, never about the record: the finding
                        // keeps what somebody decided, and stops offering it to a document that
                        // will not have it.
                        foreach (var finding in picked.OfType<Finding>()) finding.Released();

                        KeepFindings();
                        ArmSync();
                        say.Children.Add(Ui.Done("Forgotten. Press \"Read the results\" to see "
                            + "the board the way the document holds it."));
                    })));
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

            /// <summary>
            /// Put the topics no clash answers to into the issue register.
            ///
            /// <b>Why they are not simply dropped.</b> A reviewer with the file open raises what
            /// they find, and what they find is not always a clash we sent them - a missing
            /// handrail, a door that opens the wrong way, a coordination question. Those came back
            /// counted and discarded, which is the same as not having asked.
            ///
            /// The register is where a problem with no clash behind it belongs, and importing is
            /// a merge rather than a load, so the same reviewer sending the same file three times
            /// updates their topics instead of tripling them.
            /// </summary>
            /// <param name="strangers">Topics this model could not place.</param>
            /// <param name="source">Where they came from, for the register's Source column.</param>
            /// <returns>What to say about it, or null when there was nothing to raise.</returns>
            private string? Raise(IReadOnlyList<CamelWorks.Core.OpenBim.BcfTopic> strangers,
                                  string? source)
            {
                if (!_raiseStrangers || strangers.Count == 0) return null;

                try
                {
                    var register = IssueViews.Read(_session);
                    var merged = IssueBcf.Merge(register, strangers, source);

                    var problem = IssueViews.Write(_session, register);

                    if (problem != null)
                        return " " + Ui.Count(strangers.Count, "topic") + " could not be placed on "
                               + "this model and could not be raised as issues either: " + problem;

                    return " " + Ui.Count(merged.Added, "topic") + " had no clash behind "
                           + (merged.Added == 1 ? "it" : "them") + " and went to the Issue Manager"
                           + (merged.Updated > 0
                               ? ", and " + Ui.Count(merged.Updated, "issue") + " already there was "
                                 + "updated."
                               : ".");
                }
                catch (Exception e)
                {
                    return " " + Ui.Count(strangers.Count, "topic")
                           + " could not be raised as issues: " + Ui.Describe(e);
                }
            }

            /// <summary>
            /// Every result indexed under the topic ids it would have had anywhere in its own cell
            /// or the twenty-six around it.
            ///
            /// The last writer wins on a collision, which is right: two results close enough to
            /// share a neighbouring cell are close enough that either is a defensible answer, and
            /// the exact index has already had its say.
            /// </summary>
            /// <param name="live">The model's results.</param>
            private static Dictionary<string, Live> Near(IEnumerable<Live> live)
            {
                var near = new Dictionary<string, Live>(StringComparer.OrdinalIgnoreCase);

                foreach (var one in live)
                {
                    if (one.Key.IsEmpty) continue;

                    foreach (var guid in ClashBcf.NearbyTopicGuidsFor(one.Key)) near[guid] = one;
                }

                return near;
            }

            /// <summary>One of the model's results, as the sync tracks it while it works.</summary>
            private sealed class Live
            {
                internal ClashKey Key;
                internal string TestId = string.Empty;
                internal string ResultId = string.Empty;
                internal string Status = string.Empty;
                internal string Assigned = string.Empty;
                internal string Group = string.Empty;
            }

            /// <summary>What a write did, and anything the host would not do.</summary>
            /// <param name="did">What happened.</param>
            /// <param name="problems">What was refused, or null when nothing was.</param>
            private void Tell(string did, List<string>? problems)
            {
                _ = did;

                var problem = problems ?? new List<string>();

                // Nothing is said when it worked.
                //
                // Every verb on this board used to put a strip over the table saying what it had
                // just done - to a table that was showing the result of doing it. Forty of those
                // in an afternoon is not feedback, it is a thing to dismiss, and it was reported
                // as exactly that.
                //
                // A refusal is the opposite: it is the only account of why a press did nothing,
                // so it stays until it is read.
                if (problem.Count == 0) return;

                _toast.Children.Clear();

                foreach (var line in problem) _toast.Children.Add(Ui.Problem(line));

                Hold();
            }

            /// <summary>
            /// Say one thing, over the board it is about.
            ///
            /// Over the board and not down in the footer, which is where this went when the
            /// footer was the only place a message could land: the outcome of a press belongs
            /// next to the rows the press changed, where the eye already is.
            /// </summary>
            /// <param name="note">What to say.</param>
            private void Said(UIElement note) => Said(note, hold: false);

            /// <summary>
            /// Say one thing over the board, and decide whether it goes away on its own.
            ///
            /// <b>A problem never fades.</b> Confirmations do - they are the press being
            /// acknowledged, and a strip of them stacking over the table pushes the rows they are
            /// about off the screen. A refusal is the opposite: it is the only account of why the
            /// press did nothing, and three seconds of it while somebody is looking at the rail
            /// rather than the board is a verb that silently does not work. Every one of "rename
            /// does not work", "move only changes the selected test" and "the status dropdown
            /// does not change it" was the host refusing and saying so where nobody was looking.
            /// </summary>
            /// <param name="note">What to say.</param>
            /// <param name="hold">True to leave it up until it is dismissed.</param>
            private void Said(UIElement note, bool hold)
            {
                _toast.Children.Clear();

                var row = new DockPanel { LastChildFill = true };

                var dismiss = Ui.Link("Dismiss", Clear);
                dismiss.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(dismiss, Dock.Right);
                row.Children.Add(dismiss);

                if (note is FrameworkElement placed) placed.Margin = new Thickness(0, 0, 8, 0);

                row.Children.Add(note);
                _toast.Children.Add(row);

                if (hold) Hold();
                else Fades();
            }

            /// <summary>Say something that stays up until it is dismissed.</summary>
            /// <param name="note">What went wrong.</param>
            private void Warned(UIElement note) => Said(note, hold: true);

            /// <summary>Take the message away.</summary>
            private void Clear()
            {
                _fade?.Stop();
                _toast.Children.Clear();
            }

            /// <summary>Leave this one up until somebody dismisses it.</summary>
            private void Hold() => _fade?.Stop();

            /// <summary>
            /// Start the message's three seconds.
            ///
            /// Timed rather than left up, because most of what this says is a confirmation nobody
            /// needs twice — and a strip of them stacking over the table would push the rows the
            /// confirmations are about off the screen.
            /// </summary>
            private void Fades()
            {
                _fade?.Stop();

                _fade = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3.2),
                };

                _fade.Tick += (s, e) =>
                {
                    _fade?.Stop();
                    _toast.Children.Clear();
                };

                _fade.Start();
            }

            private System.Windows.Threading.DispatcherTimer? _fade;

            /// <summary>
            /// Who a result can be assigned to: the project's parties, and whoever the board
            /// already names.
            ///
            /// The board's own names as well as the profile's, because a federation inherited
            /// mid-job carries assignments to parties nobody has written down yet, and a list
            /// that cannot offer the name already on the row is a list that makes somebody type
            /// it again slightly differently.
            /// </summary>
            /// <param name="entry">The test on the board, or null for the whole read.</param>
            private List<string> Assignees(TestEntry? entry)
            {
                var names = new List<string>();

                foreach (var party in _session.Profile.Parties)
                    if (!names.Contains(party, StringComparer.OrdinalIgnoreCase)) names.Add(party);

                var from = entry != null
                    ? new List<TestEntry> { entry }
                    : Current() ?? new List<TestEntry>();

                foreach (var one in from)
                    foreach (var row in one.Rows)
                        if (row.Assigned.Length > 0
                            && !names.Contains(row.Assigned, StringComparer.OrdinalIgnoreCase))
                            names.Add(row.Assigned);

                // Last, and always there: clearing an assignment is as ordinary as making one,
                // and a list with no way back leaves the only route through a verb somewhere else.
                names.Add(Nobody);

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
