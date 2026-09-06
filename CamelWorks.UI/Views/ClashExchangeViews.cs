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
        private const string SendOut = "Send out";

        /// <summary>The two halves of a round.</summary>
        private const string TakeBack = "Take back";

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

            private readonly StackPanel _clauses = Ui.Stack();
            private readonly StackPanel _grid = Ui.Stack();
            private readonly StackPanel _sending = Ui.Stack();
            private readonly TextBox _find = Ui.Text(string.Empty, "a clash, a test, a trade", null, 220);
            private readonly TextBox _named = Ui.Text("Everything", null, 180);

            private ListView? _table;
            private string _search = string.Empty;
            private System.Windows.Threading.DispatcherTimer? _typing;

            /// <summary>The rows picked by hand, or null when the whole filtered list goes.</summary>
            private HashSet<Core.Identity.ClashKey>? _only;

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
            // Sending out
            // -------------------------------------------------------------------------------

            private UIElement SendingOut()
            {
                // Live, but not per keystroke. Every letter re-picks the board and rebuilds the
                // table, which on a job of any size is a tenth of a second between one letter and
                // the next.
                _find.TextChanged += (s, e) =>
                {
                    _typing ??= new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(250),
                    };

                    _typing.Stop();
                    _typing.Tick -= Settled;
                    _typing.Tick += Settled;
                    _typing.Start();
                };

                var stage = Ui.Stack(
                    Switch(SendOut),
                    Ui.Across(Ui.Field("Find", _find), Sorting(), Splitting()),
                    _clauses,
                    _grid,
                    Ui.Commit("Show me what would go out", "Write", "pack", Rehearse, Publish),
                    _sending);

                var foot = new WrapPanel();

                foot.Children.Add(Ui.Icon(Glyphs.Filter, "What goes in", () => Configure("in")));
                foot.Children.Add(Ui.Icon(Glyphs.Save, "This profile", () => Configure("profile")));

                Redraw();

                return Bench.Surface(null, foot, stage);
            }

            private void Settled(object? sender, EventArgs e)
            {
                _typing?.Stop();

                var said = _find.Text.Trim();

                if (string.Equals(said, _search, StringComparison.Ordinal)) return;

                _search = said;
                Redraw();
            }

            /// <summary>What the send-out half is showing: the picked rows, searched.</summary>
            private IReadOnlyList<ClashItem> Showing()
            {
                var chosen = _profile.Choose(Results(), Board(), Storeys()).Items;

                if (_search.Length == 0) return chosen;

                // Across the columns a person can see. A search box that matched a field the
                // table does not draw returns rows nobody can tell apart.
                return chosen.Where(one =>
                    Holds(one.Name) || Holds(one.TestName) || Holds(one.AssignedTo)
                    || Holds(one.Level) || Holds(one.ModelPair) || Holds(one.Status.ToString()))
                    .ToList();
            }

            private bool Holds(string? said) =>
                said != null && said.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;

            /// <summary>The table, the clause chips and the counts, from what the profile picks.</summary>
            private void Redraw()
            {
                DrawClauses();

                _grid.Children.Clear();

                var showing = Showing();

                if (showing.Count == 0)
                {
                    _grid.Children.Add(Ui.Empty(
                        Results().Count == 0
                            ? "No clashes on the board."
                            : "Nothing gets through the filter.",
                        Results().Count == 0
                            ? "Run the tests on the Manage tab first."
                            : _profile.Pick.Describe(Storeys())));

                    return;
                }

                var rows = showing.Take(500).Select(one => new TableRow(one,
                    one.Name ?? one.Key.ToString(),
                    one.TestName,
                    one.Status.ToString(),
                    one.AssignedTo ?? string.Empty,
                    one.Level ?? string.Empty,
                    one.ModelPair)).ToList();

                _table = Ui.Table(rows,
                    ("Clash", 0), ("Test", 150), ("Status", 80),
                    ("Assigned to", 110), ("Level", 70), ("Models", 150)).Multi();

                _grid.Children.Add(Ui.Line(
                    Ui.Count(showing.Count, "clash") + " to go out"
                    + (showing.Count > rows.Count
                        ? ", " + Ui.Count(rows.Count, "row") + " drawn"
                        : string.Empty), 1, true));

                _grid.Children.Add(Ui.Loose(_table));

                _grid.Children.Add(Ui.Picking(_table,
                    new Ui.OnPicked("Send only these {n}", (picked, said) =>
                    {
                        _only = new HashSet<Core.Identity.ClashKey>(
                            picked.OfType<ClashItem>().Select(one => one.Key));

                        said.Children.Add(Ui.Note(
                            Ui.Count(_only.Count, "clash") + " will go, not the whole list. "
                            + "Press “Send everything shown” to undo it."));
                    }, primary: true),
                    new Ui.OnPicked("Send everything shown", (picked, said) =>
                    {
                        _only = null;

                        said.Children.Add(Ui.Note("The whole filtered list will go."));
                    })));

                if (_only != null)
                    _grid.Children.Add(Ui.Note(
                        Ui.Count(Going().Count, "clash") + " picked by hand, not the whole list."));
            }

            /// <summary>The filter, as a chip per clause and a way to add one.</summary>
            private void DrawClauses()
            {
                _clauses.Children.Clear();

                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };

                foreach (var clause in _profile.Pick.Clauses.ToList())
                {
                    var here = clause;

                    var chip = Ui.Quiet(
                        here.Describe(ClashFacets.Of(here.Facet, Results(), Storeys())),
                        () => Ui.Safely(() =>
                        {
                            _profile.Pick.Clauses.Remove(here);
                            Redraw();
                        }));

                    chip.ToolTip = "Press to take this filter off.";
                    chip.Margin = new Thickness(0, 0, 6, 4);

                    row.Children.Add(chip);
                }

                var add = Ui.Link("Add a filter", () => { });

                add.Click += (s, e) => Ui.Safely(() => Parts.PopMenu(add, Facets()));
                add.Margin = new Thickness(0, 0, 6, 4);

                row.Children.Add(add);

                _clauses.Children.Add(row);
            }

            /// <summary>Every parameter a filter can be about, as a menu.</summary>
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
                    Ui.Told("Nothing on the board holds a " + facet.Title.ToLowerInvariant() + ".");
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
                    () => Bench.Group("Which " + facet.Title.ToLowerInvariant() + " goes in", box));

                Ui.Configure(facet.Title, () =>
                {
                    if (taken.Count == 0) return;

                    var clause = _profile.Pick.Add(facet.Key);

                    foreach (var one in taken) clause.Values.Add(one);

                    Redraw();
                }, page);
            }

            /// <summary>Which parameter the rows are ordered by.</summary>
            private UIElement Sorting()
            {
                var by = Ui.Link(_profile.Pick.Sort == null
                    ? "Sort by…"
                    : "Sorted by " + Titled(_profile.Pick.Sort), () => { });

                by.Click += (s, e) => Ui.Safely(() =>
                {
                    var items = new List<Parts.Entry> { Parts.Entry.Heading("Sort by") };

                    items.Add(Parts.Entry.Act("As they came", null, () =>
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
                            _profile.Pick.Descending ? "Smallest first" : "Largest first", null,
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
                    ? "One file"
                    : "A file per " + Titled(_profile.SplitBy).ToLowerInvariant(), () => { });

                by.ToolTip = "Six trades, six packs, each holding only that trade's clashes.";

                by.Click += (s, e) => Ui.Safely(() =>
                {
                    var items = new List<Parts.Entry> { Parts.Entry.Heading("A file per") };

                    items.Add(Parts.Entry.Act("One file for everything", null, () =>
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

            /// <summary>Draw the whole half again, for the controls that are captioned by their value.</summary>
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

            /// <summary>The rows that actually go, once the table's own pick is honoured.</summary>
            private IReadOnlyList<ClashItem> Going()
            {
                var showing = Showing();

                if (_only == null) return showing;

                // Held as keys rather than as the table's own selection, because the table is
                // rebuilt whenever the filter or the search changes and a selection does not
                // survive that - which is how somebody presses Write and sends the whole board.
                var picked = showing.Where(one => _only.Contains(one.Key)).ToList();

                return picked.Count == 0 ? showing : picked;
            }

            /// <summary>Work out the packs without writing any of them.</summary>
            /// <param name="job">The run, for its progress line.</param>
            /// <param name="into">Where the detail is drawn.</param>
            private int Rehearse(Job job, Panel into)
            {
                var going = Going();

                if (going.Count == 0)
                {
                    into.Children.Add(Ui.Problem("Nothing gets through the filter."));
                    return 0;
                }

                var rounds = Rounds(going);

                into.Children.Add(Ui.Line(
                    Ui.Count(rounds.Count, "pack") + " holding "
                    + Ui.Count(going.Count, "clash") + ".", 1, true));

                var rows = rounds.Select(one => new TableRow(one,
                    one.Recipient,
                    one.FileStem() + ClashFile.Extension,
                    Ui.Count(one.Selection.Items.Count, "clash"))).ToList();

                into.Children.Add(Ui.Table(rows, ("For", 0), ("File", 220), ("Holding", 120)));

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
                    rounds.Count == 1 ? "Write the pack" : "Write the first pack, and the rest beside it",
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
                    Ui.Count(wrote, "pack") + " written into " + folder + ".", 1, true));
            }

            // -------------------------------------------------------------------------------
            // Taking back
            // -------------------------------------------------------------------------------

            private UIElement TakingBack()
            {
                if (_returns.Count == 0) AddReturn(null);

                var strip = Ui.Stack(
                    Switch(TakeBack),
                    Ui.Small("One file per party. The party is the file's own name, so a pack "
                             + "called Mechanical.bcfzip is Mechanical's answer."),
                    _files,
                    Ui.Across(
                        Ui.Quiet("Add a pack", () => Ui.Safely(() => AddReturn(null))),
                        Ui.Quiet("Choose several…", Chosen)));

                _back.Children.Clear();
                _back.Children.Add(Bench.Ready(
                    "Reads the returned packs and says what came back.",
                    "Two parties who answered the same clash differently are marked and applied "
                    + "to nothing until one side is chosen."));

                var stage = Ui.Stack(strip, _back, _rows, _applied);

                return Bench.Surface(null, Ui.Runner("Read them", _back, Receive), stage);
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
                var picked = Ui.AskToOpenMany("Choose the returned packs", ClashFile.Filter);

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
                        "Choose the files the parties sent back."));

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
                    one.IsDisputed && one.Chosen == null ? "disputed" : one.Would.ToString(),
                    string.Join(", ", one.Answers.Select(say => say.Party)),
                    one.Take ? "yes" : "no")
                {
                    Tone = one.IsDisputed && one.Chosen == null ? Tone.Warn
                         : one.Changes ? Tone.Good : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                    ("Clash", 0), ("Test", 140), ("Board has", 90),
                    ("Coming back", 100), ("Who answered", 140), ("Taking", 70)).Multi();

                _rows.Children.Add(Ui.Eyebrow("What came back"));
                _rows.Children.Add(table);

                var settling = Ui.Link("Settle a dispute…", () => { });

                settling.Click += (s, e) => Ui.Safely(() =>
                {
                    var picked = table.PickedMany().OfType<ExchangeRow>()
                                      .Where(one => one.IsDisputed).ToList();

                    if (picked.Count == 0)
                    {
                        Ui.Told("Pick a disputed row first.");
                        return;
                    }

                    Parts.PopMenu(settling, Sides(picked));
                });

                _rows.Children.Add(Ui.Picking(table, settling,
                    new Ui.OnPicked("Take these {n}", (picked, said) =>
                        Decide(picked, said, row =>
                        {
                            if (row.Settled != null) row.Take = true;
                        }), primary: true),
                    new Ui.OnPicked("Leave these {n}", (picked, said) =>
                        Decide(picked, said, row => row.Take = false))));

                if (_reading.Undecided > 0)
                    _rows.Children.Add(Ui.Note(
                        Ui.Count(_reading.Undecided, "clash")
                        + " two parties answered differently. They apply to nothing until "
                        + "one side is chosen."));

                if (_reading.Strangers.Count > 0)
                    _rows.Children.Add(Ui.Note(
                        Ui.Count(_reading.Strangers.Count, "result")
                        + " name elements this federation does not have - a party working "
                        + "against older models."));

                _rows.Children.Add(Ui.Runner("Apply them to the board", _applied, Apply));
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
                    said.Children.Add(Ui.Note("Pick some rows first."));
                    return;
                }

                foreach (var row in rows) what(row);

                said.Children.Add(Ui.Note(Ui.Count(rows.Count, "row") + " decided."));

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
                    Parts.Entry.Heading("Take the answer from"),
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

                items.Add(Parts.Entry.Saying("Leave them where they are", null,
                    "No party's answer is taken", () =>
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
                    "Save on the Manage tab to carry this into the project database. "
                    + "It reaches Navisworks when the open model is an .nwf on a Manage seat."));

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
                    ? new Ui.Page("This profile", () => Bench.Group("This profile", Profiles()))
                    : new Ui.Page("What goes in", () => Bench.Group("What goes in", Filters()));

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

                box.Children.Add(Ui.Small("None ticked means every status goes."));

                box.Children.Add(Ui.Check("Keep the folders", _profile.KeepGroups,
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
                    Ui.Small("A profile holds the tests, the statuses, the filter, the order and "
                             + "the split, so a weekly issue is set up once."));
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

                if (!quiet) Ui.Told("Kept “" + _profile.Name + "”.");
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
