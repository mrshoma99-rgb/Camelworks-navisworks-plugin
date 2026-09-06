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
            Drawn(session.Store, session.Profile.ProjectName);

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
        public static UIElement Drawn(ProjectStore store, string? projectName = null,
                                      IReadOnlyList<ClashGroup>? board = null) =>
            new Desk(store, projectName ?? "Untitled project", board).View;

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
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _packs = Ui.Stack();
            private readonly StackPanel _sending = Ui.Stack();
            private readonly TextBox _find = Ui.Text(string.Empty, "a test or a folder", null, 220);
            private readonly TextBox _named = Ui.Text("Everything", null, 180);

            private string _search = string.Empty;
            private System.Windows.Threading.DispatcherTimer? _typing;

            // -------------------------------------------------------------------------------
            // The take-back half
            // -------------------------------------------------------------------------------

            private readonly List<TextBox> _returns = new List<TextBox>();
            private readonly StackPanel _files = Ui.Stack();
            private readonly StackPanel _back = Ui.Stack();
            private readonly StackPanel _rows = Ui.Stack();
            private readonly StackPanel _applied = Ui.Stack();

            private ExchangeReading? _reading;

            private UIElement? _out;
            private UIElement? _in;

            private readonly IReadOnlyList<ClashGroup>? _board;

            internal Desk(ProjectStore store, string projectName,
                          IReadOnlyList<ClashGroup>? board = null)
            {
                _store = store;
                _projectName = projectName;
                _board = board;

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -------------------------------------------------------------------------------
            // The board
            // -------------------------------------------------------------------------------

            private IReadOnlyList<ClashGroup> Board() =>
                _board ?? CoordinateViews.LastBoard?.Groups ?? Array.Empty<ClashGroup>();

            private IReadOnlyList<ClashItem> Results() =>
                Board().SelectMany(one => one.Items).Where(one => one != null).ToList();

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

                if (_out != null) _half.Content = Ui.Loose(_out);

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
            // Exporting
            // -------------------------------------------------------------------------------

            /// <summary>
            /// Three columns, left to right, in the order the data nests.
            ///
            /// A clash belongs to a test, and the two are filtered by different things: a test by
            /// its folder, its kind and when it last went out; a result by its status, who it is
            /// assigned to, the storey it stands on. A screen that lists four hundred clashes and
            /// one search box has flattened that into a list nobody sets an issue up from - nobody
            /// picks clashes one at a time to send.
            ///
            /// So: which tests, then which of their results, then what falls out - the packs. Each
            /// column says how much it let through, so it is clear which of the three narrowed the
            /// run when the last number is smaller than expected.
            /// </summary>
            private UIElement SendingOut()
            {
                var columns = new Grid { Margin = new Thickness(0, 8, 0, 0) };

                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                columns.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });

                var tests = Column("Tests", _tests);
                var results = Column("Results", _results);
                var packs = Column("Packs", _packs);

                Grid.SetColumn(tests, 0);
                Grid.SetColumn(results, 2);
                Grid.SetColumn(packs, 4);

                columns.Children.Add(tests);
                columns.Children.Add(results);
                columns.Children.Add(packs);

                var stage = Ui.Stack(
                    Switch(SendOut),
                    columns,
                    Ui.Commit("Preview the export", "Write", "pack", Rehearse, Publish),
                    _sending);

                var foot = new WrapPanel();

                foot.Children.Add(Ui.Icon(Glyphs.Filter, "Export scope", () => Configure("in")));
                foot.Children.Add(Ui.Icon(Glyphs.Save, "Export profile", () => Configure("profile")));

                Redraw();

                return Bench.Surface(null, foot, stage);
            }

            /// <summary>Redraw the columns once the typing in the find box has stopped.</summary>
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

            /// <summary>One of the three columns, headed and boxed so the three read as steps.</summary>
            /// <param name="title">What the column is about.</param>
            /// <param name="body">The column's own contents, redrawn in place.</param>
            private static UIElement Column(string title, Panel body)
            {
                var held = Ui.Stack(Ui.Eyebrow(title), body);

                var box = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 10),
                    Child = held,
                };

                box.Tint(Border.BorderBrushProperty, "Bd");
                box.Tint(Border.BackgroundProperty, "Card");

                return box;
            }

            /// <summary>Draw all three columns from what the profile says.</summary>
            private void Redraw()
            {
                DrawTests();
                DrawResults();
                DrawPacks();
            }

            // -------------------------------------------------------------------------------
            // Column one: the tests
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

            /// <summary>Whether this test is ticked. A profile that knows nothing sends everything.</summary>
            /// <param name="name">The test.</param>
            private bool Sending(string name)
            {
                var found = _profile.Tests.FirstOrDefault(
                    one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

                return found?.Chosen ?? true;
            }

            /// <summary>Tick or untick a test, adding it to the profile if it had never seen it.</summary>
            /// <param name="name">The test.</param>
            /// <param name="on">Whether it goes.</param>
            private void Send(string name, bool on)
            {
                var found = _profile.Tests.FirstOrDefault(
                    one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

                if (found == null)
                {
                    // The profile has to hold every test before one can be left out, or unticking
                    // one would read as "send only that one" - an empty list means everything.
                    foreach (var test in Tests())
                        if (!_profile.Tests.Any(held => string.Equals(held.Name, test.Name,
                                                                      StringComparison.OrdinalIgnoreCase)))
                            _profile.Add(test.Name);

                    found = _profile.Tests.FirstOrDefault(
                        one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));
                }

                if (found != null) found.Chosen = on;

                Redraw();
            }

            private void DrawTests()
            {
                _tests.Children.Clear();

                var all = Tests();

                if (all.Count == 0)
                {
                    _tests.Children.Add(Ui.Small("No tests on the board. Run them on Manage first."));
                    return;
                }

                // Live, but not per keystroke. Every letter redraws all three columns, which on a
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

                _find.Margin = new Thickness(0, 2, 0, 6);
                _tests.Children.Add(Ui.Loose(_find));

                var showing = _search.Length == 0
                    ? all
                    : all.Where(one => Holds(one.Name) || Holds(one.Folder)).ToList();

                var list = Ui.Stack();

                foreach (var folder in showing.GroupBy(one => one.Folder ?? string.Empty))
                {
                    if (folder.Key.Length > 0)
                    {
                        var here = folder.ToList();

                        var band = Ui.Check(folder.Key + "  (" + here.Count + ")",
                                            here.All(one => Sending(one.Name)),
                                            on =>
                                            {
                                                foreach (var test in here) Send(test.Name, on);
                                            });

                        band.FontWeight = FontWeights.SemiBold;
                        band.Margin = new Thickness(0, 4, 0, 2);

                        list.Children.Add(band);
                    }

                    foreach (var test in folder)
                    {
                        var here = test;

                        var row = Ui.Check(here.Name + "   " + here.Count.ToString("N0", CultureInfo.InvariantCulture),
                                           Sending(here.Name), on => Send(here.Name, on));

                        row.Margin = new Thickness(folder.Key.Length > 0 ? 14 : 0, 1, 0, 1);
                        row.ToolTip = ExportProfile.When(_profile.Tests.FirstOrDefault(
                            one => string.Equals(one.Name, here.Name,
                                                 StringComparison.OrdinalIgnoreCase)));

                        list.Children.Add(row);
                    }
                }

                _tests.Children.Add(new ScrollViewer
                {
                    Content = Ui.Loose(list),
                    MaxHeight = 340,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });

                var chosen = all.Count(one => Sending(one.Name));

                _tests.Children.Add(Ui.Across(
                    Ui.Link("All", () => { foreach (var one in all) Send(one.Name, true); }),
                    Ui.Link("None", () => { foreach (var one in all) Send(one.Name, false); })));

                _tests.Children.Add(Ui.Small(
                    chosen + " of " + Ui.Count(all.Count, "test") + " · "
                    + Ui.Count(all.Where(one => Sending(one.Name)).Sum(one => one.Count), "result")));
            }

            // -------------------------------------------------------------------------------
            // Column two: the results
            // -------------------------------------------------------------------------------

            private void DrawResults()
            {
                _results.Children.Clear();

                _results.Children.Add(Ui.Small(
                    "Which of the chosen tests' results go in, by any parameter a result carries."));

                foreach (var clause in _profile.Pick.Clauses.ToList())
                {
                    var here = clause;
                    var facet = ClashFacets.Of(here.Facet, Results(), Storeys());

                    var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };

                    row.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star),
                    });

                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var said = Ui.Line(here.Describe(facet));

                    said.TextWrapping = TextWrapping.Wrap;
                    said.VerticalAlignment = VerticalAlignment.Center;

                    var drop = Ui.Icon(Glyphs.Close, "Remove this filter", () => Ui.Safely(() =>
                    {
                        _profile.Pick.Clauses.Remove(here);
                        Redraw();
                    }));

                    Grid.SetColumn(drop, 1);

                    row.Children.Add(said);
                    row.Children.Add(drop);

                    _results.Children.Add(row);
                }

                if (_profile.Pick.Clauses.Count == 0)
                    _results.Children.Add(Ui.Small("No filter. Every result of a chosen test goes."));

                var add = Ui.Link("Add a filter", () => { });

                add.Click += (s, e) => Ui.Safely(() => Parts.PopMenu(add, Facets()));
                add.Margin = new Thickness(0, 8, 0, 0);

                _results.Children.Add(Ui.Across(add, Sorting()));

                var scope = Scope();

                _results.Children.Add(Ui.Small(
                    Ui.Count(scope.Count, "result") + " in scope, of "
                    + Ui.Count(Results().Count, "result") + " on the board."));
            }

            /// <summary>What the tests and the filter between them let through.</summary>
            private IReadOnlyList<ClashItem> Scope() =>
                _profile.Choose(Results(), Board(), Storeys()).Items;

            private bool Holds(string? said) =>
                said != null && said.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

            // -------------------------------------------------------------------------------
            // Column three: the packs
            // -------------------------------------------------------------------------------

            private void DrawPacks()
            {
                _packs.Children.Clear();

                _packs.Children.Add(Splitting());

                var rounds = Rounds(Scope());

                if (rounds.Count == 0 || rounds.All(one => one.Selection.Items.Count == 0))
                {
                    _packs.Children.Add(Ui.Small("Nothing to write yet."));
                    return;
                }

                foreach (var round in rounds.Take(30))
                {
                    var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };

                    row.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star),
                    });

                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var who = Ui.Line(round.Recipient);

                    who.TextTrimming = TextTrimming.CharacterEllipsis;
                    who.ToolTip = round.FileStem() + ClashFile.Extension;

                    var count = Ui.Small(round.Selection.Items.Count
                        .ToString("N0", CultureInfo.InvariantCulture));

                    count.VerticalAlignment = VerticalAlignment.Center;

                    Grid.SetColumn(count, 1);

                    row.Children.Add(who);
                    row.Children.Add(count);

                    _packs.Children.Add(row);
                }

                _packs.Children.Add(Ui.Small(
                    Ui.Count(rounds.Count, "pack") + " · "
                    + Ui.Count(rounds.Sum(one => one.Selection.Items.Count), "clash")));
            }

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
            }

            /// <summary>Every parameter a result filter can be about, as a menu.</summary>
            private IReadOnlyList<Parts.Entry> Facets()
            {
                var items = new List<Parts.Entry> { Parts.Entry.Heading("Filter by") };

                foreach (var facet in ClashFacets.Every(Results(), Storeys()))
                {
                    var here = facet;

                    items.Add(Parts.Entry.Act(here.Title, null, () => Values(here)));
                }

                return items;
            }

            /// <summary>Ask which values of one parameter go in, from the ones the board holds.</summary>
            /// <param name="facet">The parameter chosen.</param>
            private void Values(ClashFacet facet)
            {
                var held = ClashFacets.Values(facet, Results());

                if (held.Count == 0)
                {
                    Ui.Told("No clash on the board carries a value for "
                            + facet.Title.ToLowerInvariant() + ".");

                    return;
                }

                var box = Ui.Stack();
                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var value in held.Take(60))
                {
                    var here = value;

                    box.Children.Add(Ui.Check(here, false, on =>
                    {
                        if (on) taken.Add(here);
                        else taken.Remove(here);
                    }));
                }

                var page = new Ui.Page(facet.Title,
                    () => Bench.Group(facet.Title + " values to include", box));

                Ui.Configure(facet.Title, () =>
                {
                    if (taken.Count == 0) return;

                    var clause = _profile.Pick.Add(facet.Key);

                    foreach (var one in taken) clause.Values.Add(one);

                    Redraw();
                }, page);
            }

            /// <summary>Which parameter the results are ordered by inside each pack.</summary>
            private UIElement Sorting()
            {
                var by = Ui.Link(_profile.Pick.Sort == null
                    ? "Sort…"
                    : "Sorted by " + Titled(_profile.Pick.Sort), () => { });

                by.Click += (s, e) => Ui.Safely(() =>
                {
                    var items = new List<Parts.Entry> { Parts.Entry.Heading("Sort by") };

                    items.Add(Parts.Entry.Act("Unsorted", null, () =>
                    {
                        _profile.Pick.Sort = null;
                        Rebuild();
                    }));

                    foreach (var facet in ClashFacets.Every(Results(), Storeys()))
                    {
                        var here = facet;

                        items.Add(Parts.Entry.Act(here.Title, null, () =>
                        {
                            _profile.Pick.Sort = here.Key;
                            Rebuild();
                        }));
                    }

                    if (_profile.Pick.Sort != null)
                    {
                        items.Add(Parts.Entry.Rule());
                        items.Add(Parts.Entry.Act(
                            _profile.Pick.Descending ? "Ascending" : "Descending", null,
                            () =>
                            {
                                _profile.Pick.Descending = !_profile.Pick.Descending;
                                Rebuild();
                            }));
                    }

                    Parts.PopMenu(by, items);
                });

                return by;
            }

            /// <summary>Which parameter the run is split by, so one press writes a pack each.</summary>
            private UIElement Splitting()
            {
                var by = Ui.Link(_profile.SplitBy == null
                    ? "Single file"
                    : "Split by " + Titled(_profile.SplitBy).ToLowerInvariant(), () => { });

                by.ToolTip = "Writes one pack per value, each holding only that party's clashes.";
                by.Margin = new Thickness(0, 2, 0, 4);

                by.Click += (s, e) => Ui.Safely(() =>
                {
                    var items = new List<Parts.Entry> { Parts.Entry.Heading("Split by") };

                    items.Add(Parts.Entry.Act("Do not split", null, () =>
                    {
                        _profile.SplitBy = null;
                        Rebuild();
                    }));

                    foreach (var facet in ClashFacets.Every(Results(), Storeys()))
                    {
                        var here = facet;

                        items.Add(Parts.Entry.Act(here.Title, null, () =>
                        {
                            _profile.SplitBy = here.Key;
                            Rebuild();
                        }));
                    }

                    Parts.PopMenu(by, items);
                });

                return by;
            }

            /// <summary>Draw the whole half again, for the controls captioned by their own value.</summary>
            private void Rebuild()
            {
                _out = SendingOut();

                if (ReferenceEquals(_half.Content, _in)) return;

                _half.Content = null;

                if (_out != null) _half.Content = Ui.Loose(_out);
            }

            private string Titled(string? key) =>
                ClashFacets.Of(key, Results(), Storeys())?.Title ?? key ?? "nothing";

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
                _profile.Rounds(going, Board(), Storeys());

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

                    _sending.Children.Add(Ui.Note(
                        Path.GetFileName(file) + " - " + round.Selection.Explain()));
                }

                // Which tests went out, and when, so next Friday's list says which have never
                // gone at all.
                _profile.Stamp(going.Select(one => one.TestName).Distinct(StringComparer.OrdinalIgnoreCase),
                               when);

                Keep(quiet: true);

                _sending.Children.Add(Ui.Line(
                    Ui.Count(wrote, "pack") + " written to " + folder + ".", 1, true));
            }

            // -------------------------------------------------------------------------------
            // Taking back
            // -------------------------------------------------------------------------------

            private UIElement TakingBack()
            {
                if (_returns.Count == 0) AddReturn(null);

                var strip = Ui.Stack(
                    Switch(TakeBack),
                    Ui.Small("One file per party. The party is taken from the file name, so "
                             + "Mechanical.bcfzip is read as Mechanical's response."),
                    _files,
                    Ui.Across(
                        Ui.Quiet("Another file", () => Ui.Safely(() => AddReturn(null))),
                        Ui.Quiet("Browse…", Chosen)));

                _back.Children.Clear();
                _back.Children.Add(Bench.Ready(
                    "Reads the returned packs and reports what they change.",
                    "Where two parties returned different answers for one clash, the row is "
                    + "marked as a conflict and applied only once a side is chosen."));

                var stage = Ui.Stack(strip, _back, _rows, _applied);

                return Bench.Surface(null, Ui.Runner("Read the packs", _back, Receive), stage);
            }

            /// <summary>Add a row for one returned pack.</summary>
            /// <param name="path">The file, or null for an empty row.</param>
            private void AddReturn(string? path)
            {
                var box = Ui.Text(path ?? string.Empty, "a returned .bcfzip", null, 300);

                _returns.Add(box);

                _files.Children.Add(Ui.PathField(
                    "Pack " + _returns.Count.ToString(CultureInfo.InvariantCulture),
                    box, PathKind.Open, ClashFile.Filter));
            }

            /// <summary>Take several packs at once, which is how they arrive.</summary>
            private void Chosen()
            {
                var picked = Ui.AskToOpenMany("Returned packs", ClashFile.Filter);

                if (picked.Count == 0) return;

                // The empty rows first: choosing four files when one row is blank should fill it
                // rather than leave it above four new ones.
                foreach (var file in picked)
                {
                    var empty = _returns.FirstOrDefault(one => one.Text.Trim().Length == 0);

                    if (empty != null) empty.Text = file;
                    else AddReturn(file);
                }
            }

            /// <summary>Read every pack named, and say what would happen.</summary>
            /// <param name="job">The run.</param>
            private void Receive(Job job)
            {
                _rows.Children.Clear();
                _applied.Children.Clear();

                var packs = new List<ExchangePack>();

                foreach (var box in _returns)
                {
                    var file = box.Text.Trim();

                    if (file.Length == 0) continue;

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

                    // The party is the file's own name. Asking somebody to type "Mechanical"
                    // beside a file called Mechanical.bcfzip is asking them to do the tool's job.
                    packs.Add(new ExchangePack(Path.GetFileNameWithoutExtension(file),
                                               ClashFile.Read(bytes), Path.GetFileName(file)));
                }

                if (packs.Count == 0)
                {
                    _back.Children.Add(Ui.Empty("No packs named.",
                        "Name the files the parties returned."));

                    return;
                }

                _reading = ClashExchange.Read(packs, Results());

                _back.Children.Add(Ui.Line(_reading.Explain(), 1, true));

                _back.Children.Add(Ui.Across(
                    Ui.Pill(Ui.Count(_reading.Packs.Count, "pack")),
                    Ui.Pill(Ui.Count(_reading.Changes, "change"),
                            _reading.Changes > 0 ? Tone.Good : Tone.Plain),
                    Ui.Pill(Ui.Count(_reading.Comments, "comment")),
                    Ui.Pill(Ui.Count(_reading.Disputed, "disputed"),
                            _reading.Disputed > 0 ? Tone.Warn : Tone.Plain)));

                DrawReading();
            }

            /// <summary>The rows that came back, and what each would do.</summary>
            private void DrawReading()
            {
                _rows.Children.Clear();

                if (_reading == null || _reading.Rows.Count == 0) return;

                var rows = _reading.Rows.Take(500).Select(one => new TableRow(one,
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
                    ("Clash", 0), ("Test", 150), ("On the board", 100),
                    ("Returned", 100), ("Parties", 160), ("Accepted", 90)).Multi();

                _rows.Children.Add(Ui.Eyebrow("Returned clashes"));
                _rows.Children.Add(table);

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

                if (_reading.Undecided > 0)
                    _rows.Children.Add(Ui.Note(
                        Ui.Count(_reading.Undecided, "clash")
                        + " returned conflicting answers. They are applied only once a side "
                        + "is chosen."));

                if (_reading.Strangers.Count > 0)
                    _rows.Children.Add(Ui.Note(
                        Ui.Count(_reading.Strangers.Count, "result")
                        + " reference elements this federation does not hold, which usually "
                        + "means a party worked against an earlier model issue."));

                _rows.Children.Add(Ui.Runner("Apply to the board", _applied, Apply));
            }

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

                var applied = ClashExchange.Apply(_reading);

                _applied.Children.Add(Ui.Line(applied.Explain(), 1, true));

                if (applied.Moved == 0 && applied.Assigned == 0 && applied.Comments == 0) return;

                // The board's own save is what carries this into the project database and, on a
                // Manage seat with an .nwf open, back into Navisworks. Written here would be a
                // second path to the same place that could disagree with the first.
                _applied.Children.Add(Ui.Note(
                    "Save on the Manage tab to write this to the project database. It reaches "
                    + "Navisworks when the open model is an .nwf and the edition is Manage."));

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
                    : new Ui.Page("Export scope", () => Bench.Group("Export scope", Filters()));

                Ui.Configure(page.Name, Rebuild, page);
            }

            /// <summary>The two filters asked often enough to have their own controls.</summary>
            private UIElement Filters()
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
