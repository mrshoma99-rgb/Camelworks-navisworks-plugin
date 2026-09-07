using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Create: every test this project holds, of every kind, with the picked test's settings
    /// beside the list.
    ///
    /// Three words are used on this tab and no synonyms: a test is the configured thing, a run is
    /// one execution of it, and a kind is what sort of test it is.
    ///
    /// The list is the union of every registered check's <see cref="ICheck.Runs"/>, plus the
    /// document's clash tests. The kinds are not symmetric and the screen says so rather than
    /// pretending otherwise: a clearance or fall-hazard test is a <see cref="CheckRunSpec"/> in
    /// the project file and every field of it is edited here, while a clash test lives in the
    /// Navisworks document, so its panel offers only what the host takes — name, folder and
    /// tolerance — and its two sides are set on the Clash matrix.
    ///
    /// Results are not here. Run hands off to the Manage board, which is the one viewer for every
    /// check's findings.
    ///
    /// This replaces the Clearance and Fall hazard tabs. One tab per kind put the same list, the
    /// same verbs and the same footer on two screens and gave neither a way to run a mixed set.
    /// </summary>
    public static class CheckCreateViews
    {
        /// <summary>The check the picked test belongs to, held between visits, or null.</summary>
        internal static string? PickedCheck { get; set; }

        /// <summary>The picked test, by its check's own id for it, or null.</summary>
        internal static string? PickedRun { get; set; }

        /// <summary>
        /// The surface, for a desk to bench.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        public static UIElement Create(ISeat seat) => new Screen(seat).Build();

        /// <summary>
        /// The tab with one test already picked, for the render harness.
        ///
        /// The picked test is tab state rather than an argument, so without this the settings
        /// panel — the half of the tab that differs per kind — could only be looked at on a
        /// licensed seat.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="checkKey">The check that owns the test, or null for nothing picked.</param>
        /// <param name="runId">The test, by its check's own id for it, or null.</param>
        public static UIElement Drawn(ISeat seat, string? checkKey, string? runId)
        {
            PickedCheck = checkKey;
            PickedRun = runId;

            return Create(seat);
        }

        static CheckCreateViews() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Forget which test was picked. Tests saved in the project file survive; a clash test's
        /// id belongs to the document that has just been closed.
        /// </summary>
        internal static void Forget()
        {
            PickedCheck = null;
            PickedRun = null;
        }

        // -----------------------------------------------------------------------------------

        /// <summary>One test in the list: which check owns it, and the check's own description.</summary>
        private sealed class Listed
        {
            internal Listed(ICheck check, CheckRun run)
            {
                Check = check;
                Run = run;
            }

            internal ICheck Check { get; }

            internal CheckRun Run { get; }

            /// <summary>Whether this row is the one the settings panel is drawing.</summary>
            internal bool Is(string? checkKey, string? runId) =>
                string.Equals(Check.Key, checkKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Run.Id, runId, StringComparison.Ordinal);
        }

        private sealed class Screen
        {
            private readonly ISeat _seat;

            private readonly StackPanel _log = Ui.Stack();
            private readonly ContentControl _list = new ContentControl();

            /// <summary>The verbs under the list, pinned to the bottom of the pane.</summary>
            private readonly ContentControl _picking = new ContentControl();
            private readonly ContentControl _panel = new ContentControl();
            private readonly ContentControl _foot = new ContentControl();
            private readonly TextBox _search;
            private readonly System.Windows.Controls.Button _make;

            /// <summary>Every test, read once per reload. Searching filters this rather than asking again.</summary>
            private IReadOnlyList<Listed> _all = Array.Empty<Listed>();

            /// <summary>The document's clash tests as last read, so an edit elsewhere does not re-walk them.</summary>
            private IReadOnlyList<Listed> _hosted = Array.Empty<Listed>();

            /// <summary>What the search box holds.</summary>
            private string _query = string.Empty;

            /// <summary>The rows the last draw put in the table, for the footer's counts.</summary>
            private IReadOnlyList<Listed> _shown = Array.Empty<Listed>();

            /// <summary>The table, so the footer can read what is selected.</summary>
            private ListView? _table;

            /// <summary>True while the table is being filled, so its own selection does not redraw it.</summary>
            private bool _filling;

            /// <summary>What the last run found, or null before one.</summary>
            private string? _outcome;

            /// <summary>What the last run covered, for the hand-off to the board.</summary>
            private ClashManageViews.Request? _went;

            /// <summary>The document's clash tests as a check, built once because reading is host calls.</summary>
            private ClashCheck? _clash;

            /// <summary>The form for a clash test being made, or null when none is.</summary>
            private UIElement? _making;

            internal Screen(ISeat seat)
            {
                _seat = seat;

                _search = Ui.Text(string.Empty, "Search tests", text =>
                {
                    _query = text ?? string.Empty;

                    RedrawList();
                }, 220);

                // The menu is hung off the button it is opened from, so it needs the button. A
                // handler that names the control it is attached to cannot be written in the
                // constructor call that makes it.
                System.Windows.Controls.Button? made = null;

                made = Ui.Button("New test", () => Parts.PopMenu(made!, Kinds()));

                _make = made;
            }

            private Core.Measurement.Measure Units => _seat.Profile.Units;

            /// <summary>
            /// The document's clash tests, as a check.
            ///
            /// Built here rather than taken from the catalogue, which registers the two
            /// project-file checks only: the Manage board builds its own for the same reason.
            /// Held for the life of the screen because it caches what each test is between, which
            /// is two host calls per test.
            /// </summary>
            private ClashCheck Clash =>
                _clash ??= new ClashCheck(_seat.Clash, _seat.MetresPerUnit, _seat.ClashProblem);

            /// <summary>Every check whose tests belong in this list, clash first.</summary>
            private IReadOnlyList<ICheck> Checks()
            {
                var found = new List<ICheck>();

                if (_seat.Checks.Find(FindingSource.Clash) == null) found.Add(Clash);

                found.AddRange(_seat.Checks.All);

                return found;
            }

            internal UIElement Build()
            {
                Reload();
                RedrawPanel();
                RedrawFoot();

                var left = Bench.Pane("Tests", null, Listing(), null, scrolls: false);
                var right = Bench.Pane("Settings", null, _panel, null);

                // The list pane holds four columns of names; the settings pane holds fields
                // capped at 380. Splitting the window evenly starved the columns and left the
                // fields with white space they had no use for.
                return Bench.Split(Strip(), left, right, _foot, 620);
            }

            /// <summary>
            /// The list pane's body: the rows scroll, the verbs under them do not.
            ///
            /// The verbs were inside the scroller with the rows, so on a project with more tests
            /// than fit the pane they were below the last row and had to be scrolled to. They act
            /// on what is ticked, which is on screen, so they stay on screen.
            /// </summary>
            private UIElement Listing()
            {
                // A dock, not a grid with a scroller in its star row. The table is a ListView and
                // scrolls itself; putting it inside a second scroller sized to the pane left it
                // measured against a height it did not use, and the rows drew at the foot of an
                // otherwise empty pane. Docked, the verbs take their line at the bottom and the
                // table fills what is left, which is what every other two-pane bench here does.
                var body = new DockPanel { LastChildFill = true };

                var pinned = Ui.Loose(_picking);

                DockPanel.SetDock(pinned, Dock.Bottom);
                body.Children.Add(pinned);

                DockPanel.SetDock(_log, Dock.Top);
                body.Children.Add(Ui.Loose(_log));

                Ui.SetFills(body, true);

                body.Children.Add(Ui.Loose(_list));

                return body;
            }

            /// <summary>
            /// The strip over both panes: what makes a test on the left, the search on the right.
            /// </summary>
            private UIElement Strip()
            {
                var row = new DockPanel { LastChildFill = false };

                var matrix = Ui.Button("From the matrix", () =>
                    Ui.Told(Shell.AppWindow.Show("clash", "matrix")));

                matrix.Margin = new Thickness(8, 0, 0, 0);

                DockPanel.SetDock(_make, Dock.Left);
                row.Children.Add(Ui.Loose(_make));

                DockPanel.SetDock(matrix, Dock.Left);
                row.Children.Add(matrix);

                DockPanel.SetDock(_search, Dock.Right);
                row.Children.Add(Ui.Loose(_search));

                return row;
            }

            // ---- The list ------------------------------------------------------------------

            /// <summary>
            /// Ask every check what tests it holds, and draw them.
            /// </summary>
            /// <param name="rereadHost">
            /// False to reuse the document's clash tests as last read. Reading them is two host
            /// calls per test, and an edit to a project-file test cannot have changed any of them.
            /// </param>
            private void Reload(bool rereadHost = true)
            {
                var found = new List<Listed>();

                if (_seat.Checks.Find(FindingSource.Clash) == null)
                {
                    if (rereadHost) _hosted = Held(Clash);

                    found.AddRange(_hosted);
                }

                foreach (var check in _seat.Checks.All) found.AddRange(Held(check));

                _all = found;

                RedrawList();
            }

            /// <summary>One check's tests, or nothing and a line saying why it would not answer.</summary>
            /// <param name="check">The check.</param>
            private IReadOnlyList<Listed> Held(ICheck check)
            {
                try
                {
                    return check.Runs().Select(run => new Listed(check, run)).ToList();
                }
                catch (Exception error)
                {
                    _log.Children.Add(Ui.Problem(check.Name + " could not be listed: "
                                                 + Ui.Describe(error)));

                    return Array.Empty<Listed>();
                }
            }

            /// <summary>The tests the search leaves, filed by folder and named within it.</summary>
            private IReadOnlyList<Listed> Filtered()
            {
                var query = _query.Trim();

                var kept = string.IsNullOrEmpty(query)
                    ? _all
                    : _all.Where(one => Matches(one, query)).ToList();

                return kept.OrderBy(one => one.Run.Folder ?? string.Empty,
                                    StringComparer.OrdinalIgnoreCase)
                           .ThenBy(one => one.Run.Name, StringComparer.OrdinalIgnoreCase)
                           .ToList();
            }

            /// <summary>Whether a test answers the search, on any of the four things the row shows.</summary>
            /// <param name="one">The test.</param>
            /// <param name="query">What was typed.</param>
            private static bool Matches(Listed one, string query) =>
                Has(one.Run.Name, query) || Has(one.Run.Folder, query)
                || Has(one.Check.Name, query) || Has(one.Run.Between, query);

            /// <summary>Whether one field holds the search text.</summary>
            /// <param name="value">The field.</param>
            /// <param name="query">What was typed.</param>
            private static bool Has(string? value, string query) =>
                value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

            private void RedrawList()
            {
                var shown = Filtered();

                _shown = shown;

                if (_all.Count == 0)
                {
                    _table = null;
                    _list.Content = Empty();
                    _picking.Content = null;
                    RedrawFoot();
                    return;
                }

                if (shown.Count == 0)
                {
                    _table = null;
                    _picking.Content = null;

                    _list.Content = Ui.Empty("Nothing matches \"" + _query.Trim() + "\".",
                        Ui.Count(_all.Count, "test") + " in this project.", "Clear the search",
                        () =>
                        {
                            _search.Text = string.Empty;
                            _query = string.Empty;

                            RedrawList();
                        });

                    RedrawFoot();
                    return;
                }

                var rows = new List<TableRow>();

                foreach (var one in shown)
                {
                    var folder = one.Run.Folder ?? string.Empty;

                    // The folder is printed on every row. Printing it once per group and leaving
                    // the rows under it blank read as "this test is in no folder", which is a
                    // state the same column has to be able to show.
                    rows.Add(new TableRow(one, one.Run.Name, one.Check.Name,
                                          folder.Length == 0 ? "Not filed" : folder, Found(one))
                    {
                        Tone = one.Is(PickedCheck, PickedRun) ? Tone.Good : Tone.Plain,

                        // The check's own symbol, beside its own name. The list mixes kinds and
                        // sorts by any column, so the kind has to be readable without reading.
                        Symbol = Ui.CheckSymbol(one.Check.Symbol),
                    });
                }

                // The name takes what is left and the other three ask for what their longest
                // ordinary value needs, so nothing in this table is drawn with an ellipsis at the
                // pane's own width.
                //
                // What a test covers is not a column: it is a whole sentence - "Level 03 Floors
                // under everything except the surfaces, in Level 03" - and no width this pane can
                // give it is enough. It is on the settings panel beside the list, and the search
                // still reads it.
                // Kind is column 1, and it is the one column whose value is a kind rather than a
                // measurement, so it is the one that carries the symbol.
                var table = Ui.Table(rows,
                                     ("Test", 0), ("Kind", 118), ("Folder", 120),
                                     ("Last run", 160)).Multi().Symbolled(1);

                _filling = true;

                foreach (var row in rows)
                    if (row.Source is Listed one && one.Is(PickedCheck, PickedRun))
                        table.SelectedItem = row;

                _filling = false;

                table.SelectionChanged += (s, e) =>
                {
                    if (_filling) return;

                    if (!(table.SelectedItem is TableRow row) || !(row.Source is Listed one)) return;

                    PickedCheck = one.Check.Key;
                    PickedRun = one.Run.Id;

                    RedrawPanel();
                    RedrawFoot();
                };

                _table = table;

                var verbs = new List<Ui.OnPicked>
                {
                    new Ui.OnPicked("Run these {n}", (picked, said) => RunPicked(picked, said)),
                    new Ui.OnPicked("Duplicate this one", (picked, said) => Duplicate(picked, said)),
                    new Ui.OnPicked("Rename this one...", (picked, said) => Rename(picked, said)),
                    new Ui.OnPicked("Move these {n}...", (picked, said) => Refile(picked, said)),
                    new Ui.OnPicked("Delete these {n}", (picked, said) => Delete(picked, said)),
                };

                // The same verbs on right-click, writing into the screen's own log: OnMenu carries
                // no panel of its own, and a verb that says nothing is a verb reported as broken.
                table.Menu(verbs.Select(one =>
                    new Ui.OnMenu(one.Caption, chosen => one.Act(chosen, _log))).ToArray());

                _list.Content = table;
                _picking.Content = Ui.Picking(table, verbs.ToArray());
            }

            /// <summary>What the last run of a test found: never run, clean, or a count.</summary>
            /// <param name="one">The test.</param>
            private static string Found(Listed one)
            {
                if (one.Run.LastRunTicks == 0) return "Never run";

                var when = new DateTime(one.Run.LastRunTicks, DateTimeKind.Utc).ToLocalTime()
                    .ToString("d MMM HH:mm", CultureInfo.CurrentCulture);

                return (one.Run.ResultCount == 0
                            ? "Clean"
                            : Ui.Count(one.Run.ResultCount, one.Check.ResultWord))
                       + ", " + when;
            }

            /// <summary>What the list says when this project holds no test of any kind.</summary>
            private UIElement Empty()
            {
                var checks = Checks();

                if (checks.Count == 0)
                    return Ui.Empty("No checks are registered in this session.",
                                    "This is a fault in this build rather than anything you did.");

                return Ui.Empty("No tests yet.",
                                "A test is a named set of settings, saved in this project and run "
                                + "as often as you like.", "New test",
                                () => Parts.PopMenu(_make, Kinds()));
            }

            // ---- Making one ----------------------------------------------------------------

            /// <summary>
            /// The kinds of test that can be made.
            ///
            /// A switch on the check's own type, which is where the difference between the kinds
            /// lives: <see cref="ICheck"/> lists and runs tests and has no member that makes one,
            /// because the two project-file checks are handed a <see cref="CheckRunSpec"/> and a
            /// clash test is made in the Navisworks document. Every kind is made from here.
            /// </summary>
            private IReadOnlyList<Parts.Entry> Kinds()
            {
                var made = new List<Parts.Entry>();

                foreach (var check in Checks())
                {
                    switch (check)
                    {
                        case ClearanceCheck clearance:
                            made.Add(Parts.Entry.Saying(check.Name, Ui.CheckSymbol(check.Symbol),
                                "The clear height over the surfaces you name. A duct 1.9 m over a "
                                + "corridor clashes with nothing.",
                                () => NewClearance(clearance)));
                            break;

                        case FallCheck fall:
                            made.Add(Parts.Entry.Saying(check.Name, Ui.CheckSymbol(check.Symbol),
                                "Openings wide enough to fall through, and floor edges with no "
                                + "handrail, at one elevation.",
                                () => NewFall(fall)));
                            break;

                        case ClashCheck clash:
                            made.Add(Parts.Entry.Saying(check.Name, Ui.CheckSymbol(check.Symbol),
                                "Two things against each other, in the Navisworks document. A "
                                + "round of them at once is the Clash matrix.",
                                () => NewClash(clash)));
                            break;

                        default:
                            made.Add(Parts.Entry.Barred(check.Name, Ui.CheckSymbol(check.Symbol),
                                check.Name + " tests are made where they are defined."));
                            break;
                    }
                }

                return made;
            }

            /// <summary>Save a new clearance test and pick it.</summary>
            /// <param name="check">The clearance check.</param>
            private void NewClearance(ClearanceCheck check)
            {
                var made = new CheckRunSpec(check.Key, Guid.NewGuid().ToString("N"),
                                            Next(check, "Clearance"));

                new ClearanceSettings().Write(made);

                Made(check, made, check.Put(made));
            }

            /// <summary>
            /// Make a new clash test in the document and pick it.
            ///
            /// A form rather than a saved default the panel then edits, because the host will not
            /// re-point a test that already exists: its two sides are decided once. Everything
            /// else about it — the name, the folder, the kind, the tolerance — is changed on the
            /// settings panel afterwards.
            /// </summary>
            /// <param name="check">The clash check.</param>
            private void NewClash(ClashCheck check)
            {
                // In the panel, where every other kind of test is set up, rather than in a window
                // over the tab. Nothing is made until the form's own press.
                _making = ClashTestViews.Panel(_seat, check, Next(check, "Clash"), id =>
                {
                    _making = null;

                    Reload();

                    PickedCheck = check.Key;
                    PickedRun = id;

                    RedrawList();
                    RedrawPanel();
                    RedrawFoot();
                }, () =>
                {
                    _making = null;

                    RedrawPanel();
                    RedrawFoot();
                });

                RedrawPanel();
                RedrawFoot();
            }

            /// <summary>Save a new fall-hazard test and pick it.</summary>
            /// <param name="check">The fall-hazard check.</param>
            private void NewFall(FallCheck check)
            {
                var made = new CheckRunSpec(check.Key, Guid.NewGuid().ToString("N"),
                                            Next(check, "Fall hazard"));

                new FallSettings().Write(made);

                Made(check, made, check.Put(made));
            }

            /// <summary>The next unused name of this kind, so two new tests are not both "Clearance 1".</summary>
            /// <param name="check">The check.</param>
            /// <param name="stem">What the kind's tests are called.</param>
            private static string Next(ICheck check, string stem)
            {
                var held = check.Runs();

                for (var at = held.Count + 1; ; at++)
                {
                    var wanted = stem + " " + at.ToString(CultureInfo.InvariantCulture);

                    if (!held.Any(one => string.Equals(one.Name, wanted,
                                                       StringComparison.OrdinalIgnoreCase)))
                        return wanted;
                }
            }

            /// <summary>Take up a test that was just saved, and say so when the save was refused.</summary>
            /// <param name="check">The check that owns it.</param>
            /// <param name="made">The test.</param>
            /// <param name="problem">What the save refused, or null.</param>
            private void Made(ICheck check, CheckRunSpec made, string? problem)
            {
                PickedCheck = check.Key;
                PickedRun = made.Id;

                if (problem != null) _log.Children.Add(Ui.Problem(problem));

                Reload();
                RedrawPanel();
                RedrawFoot();
            }

            // ---- The settings panel --------------------------------------------------------

            /// <summary>The picked test, or null when none is.</summary>
            private Listed? Picked() =>
                _all.FirstOrDefault(one => one.Is(PickedCheck, PickedRun));

            private void RedrawPanel()
            {
                // A test being made owns the panel until it is made or abandoned: it is the only
                // thing on this tab that is not yet in the list beside it.
                if (_making != null)
                {
                    _panel.Content = Ui.Loose(_making);
                    return;
                }

                var picked = Picked();

                if (picked == null)
                {
                    // One line rather than a card with a button in it. The list pane already
                    // carries the statement that there are no tests and the verb that fixes it;
                    // a second card here said the same thing again and offered the same action.
                    _panel.Content = Waiting(_all.Count == 0
                        ? "A test's settings appear here."
                        : "Pick a test on the left to change what it covers and what it reports.");

                    return;
                }

                switch (picked.Check)
                {
                    case ClearanceCheck clearance:
                        var measured = clearance.Spec(PickedRun);

                        _panel.Content = measured == null ? Gone()
                            : ClearanceViews.Settings(_seat, clearance, measured, Changed);
                        break;

                    case FallCheck fall:
                        var swept = fall.Spec(PickedRun);

                        _panel.Content = swept == null ? Gone()
                            : FallHazardViews.Settings(_seat, fall, swept, Changed);
                        break;

                    case ClashCheck clash:
                        _panel.Content = ClashFields(picked, clash);
                        break;

                    default:
                        _panel.Content = Ui.Empty(
                            picked.Check.Name + " tests cannot be edited here.",
                            "This kind is listed and run on this tab; its settings belong to "
                            + "wherever it was defined.");
                        break;
                }
            }

            /// <summary>What the settings pane says while it has no test to draw.</summary>
            /// <param name="text">The line.</param>
            private static UIElement Waiting(string text)
            {
                var line = Ui.Line(text, 0.85);

                line.Margin = new Thickness(0, 4, 0, 0);

                return line;
            }

            /// <summary>What the panel says when the list holds a test the project file does not.</summary>
            private static UIElement Gone() =>
                Ui.Empty("This test is no longer in the project file.",
                         "It was deleted, or the project file was replaced under this session. "
                         + "Pick another.");

            /// <summary>Redraw the list after a settings edit, without re-walking the document.</summary>
            private void Changed()
            {
                Reload(rereadHost: false);
                RedrawFoot();
            }

            /// <summary>
            /// A clash test's panel: what the host lets this screen change, and one line saying
            /// where the rest is changed.
            ///
            /// The two sides are not editable here and the screen says so rather than offering
            /// boxes whose edits would be dropped. Name, folder and tolerance are host writes and
            /// are greyed with the host's own reason when the document refuses them.
            /// </summary>
            /// <param name="picked">The picked test.</param>
            /// <param name="check">The same check, typed, for the tolerance the host records.</param>
            private UIElement ClashFields(Listed picked, ClashCheck check)
            {
                var run = picked.Run;
                var naming = check.Naming;
                var blocked = _seat.Clash.CanEdit ? null
                    : _seat.ClashProblem ?? "This document will not take a clash write.";

                var name = Ui.Settled(run.Name, string.Empty, text =>
                {
                    if (naming == null || string.IsNullOrWhiteSpace(text)) return;

                    naming.Rename(run.Id, text);
                    Changed();
                }, 190);

                var folder = Ui.Settled(run.Folder ?? string.Empty, string.Empty, text =>
                {
                    naming?.Refile(run.Id, string.IsNullOrWhiteSpace(text) ? null : text);
                    Changed();
                }, 160);

                var tolerance = Ui.Settled(Units.Bare(check.ThresholdMetres(run.Id) ?? 0),
                                           string.Empty, text =>
                {
                    if (Units.TryRead(text, out var metres) && metres >= 0) Retune(run.Id, metres);
                }, 90);

                name.IsEnabled = blocked == null && naming != null;
                folder.IsEnabled = blocked == null && naming != null;
                tolerance.IsEnabled = blocked == null;

                if (blocked != null)
                {
                    name.ToolTip = blocked;
                    folder.ToolTip = blocked;
                    tolerance.ToolTip = blocked;
                }

                var shape = check.Sides(run.Id);
                var here = MatrixDiff.Kinded(shape?.Kind);

                var kind = Ui.Choice(ClashTestViews.Kinds.Select(MatrixNaming.Worded).ToList(),
                    MatrixNaming.Worded(here), pick =>
                    {
                        var wanted = ClashTestViews.Kinds.FirstOrDefault(
                            one => string.Equals(MatrixNaming.Worded(one), pick,
                                                 StringComparison.Ordinal));

                        if (wanted == here) return;

                        Retune(run.Id, check.ThresholdMetres(run.Id) ?? 0, wanted);
                    }, 230);

                kind.IsEnabled = blocked == null;

                if (blocked != null) kind.ToolTip = blocked;

                return Ui.Stack(
                    // No heading over the name and the folder. The tab is called Create, the list
                    // beside it has a Test column, and "TEST" over the first two boxes named
                    // nothing the reader did not already have.
                    Bench.Group(string.Empty,
                        Ui.Across(Sized(Ui.Field("Name", name), 210),
                                  Sized(Ui.Field("Folder", folder), 170))),
                    // The sides first: they are what the test is, and they are the one thing on
                    // this panel that cannot be changed.
                    Bench.Group("What is tested against what",
                        Ui.Line(run.Between, 0.85),
                        Ui.Info("A test's two sides are decided when it is made. Navisworks does "
                                + "not re-point one that already has results, so changing them "
                                + "means a new test.")),
                    Bench.Group("How it is judged",
                        Sized(Ui.Field("Kind of test", kind,
                                       ClashTestViews.Explains(here)), 380),
                        here == MatrixTest.Duplicate
                            ? null
                            : Sized(Ui.Field("Tolerance (" + Units.LengthUnit + ")", tolerance),
                                    190),
                        Ui.Small("Both are changed on the test itself, and the results it already "
                                 + "holds are kept until it is run again.")),
                    blocked == null ? null : Ui.Note(blocked));
            }

            /// <summary>Write a clash test's tolerance back to the host, keeping the kind it has.</summary>
            /// <param name="testId">The host's id for the test.</param>
            /// <param name="metres">The tolerance it should now have.</param>
            private void Retune(string testId, double metres) =>
                Retune(testId, metres, MatrixDiff.Kinded(_seat.Clash.Sides(testId)?.Kind));

            /// <summary>Change what a test is judged at, and say so when the document refuses.</summary>
            /// <param name="testId">Which test.</param>
            /// <param name="metres">The tolerance, in metres.</param>
            /// <param name="kind">Which kind of test it is.</param>
            private void Retune(string testId, double metres, MatrixTest kind)
            {
                if (_seat.Clash.Retune(testId, metres, kind))
                {
                    // The rail prints the kind and the threshold, and the panel's own tolerance
                    // field appears or goes with the kind.
                    Changed();
                    RedrawPanel();

                    return;
                }

                _log.Children.Add(Ui.Problem(_seat.Clash.LastProblem
                                             ?? "The document refused the tolerance."));
            }

            /// <summary>A field capped to a readable width, so short fields sit two to a row.</summary>
            /// <param name="field">The field.</param>
            /// <param name="width">How wide it may be.</param>
            private static UIElement Sized(Panel field, double width)
            {
                field.Width = width;

                // Left, not the default stretch. A stretched element given a fixed width is
                // centred in what is left over, so a full-width field under a row of two started
                // half a column in from the margin.
                field.HorizontalAlignment = HorizontalAlignment.Left;

                return field;
            }

            // ---- Running -------------------------------------------------------------------

            /// <summary>What Run would act on: what is selected, or every test when nothing is.</summary>
            private IReadOnlyList<Listed> Targets()
            {
                var picked = _table?.PickedMany() ?? Array.Empty<object>();
                var chosen = picked.OfType<Listed>().ToList();

                return chosen.Count > 0 ? chosen : _shown;
            }

            /// <summary>Why the targets cannot be run, or null when they can.</summary>
            /// <param name="targets">What Run would act on.</param>
            private static string? Blocked(IReadOnlyList<Listed> targets)
            {
                if (targets.Count == 0) return "There is no test to run.";

                foreach (var one in targets)
                {
                    var runner = one.Check.Runner;

                    if (runner == null)
                        return one.Check.Name + " tests cannot be run from here.";

                    if (!runner.CanRun)
                        return runner.CannotRun ?? one.Check.Name + " cannot be run here.";
                }

                return null;
            }

            private void RedrawFoot()
            {
                var targets = Targets();
                var selected = (_table?.PickedMany() ?? Array.Empty<object>()).OfType<Listed>().Count();
                var blocked = Blocked(targets);

                // "Run all" only when the list is showing all of them. A search narrows what Run
                // acts on, so a button saying "all" over a filtered list would run something other
                // than what it says.
                var caption = selected > 0
                    ? "Run " + selected.ToString(CultureInfo.InvariantCulture) + " selected"
                    : _shown.Count < _all.Count
                        ? "Run " + _shown.Count.ToString(CultureInfo.InvariantCulture) + " shown"
                        : "Run all";

                var said = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                said.Children.Add(Ui.Small(_shown.Count == _all.Count
                    ? Ui.Count(_all.Count, "test") + " in this project"
                    : _shown.Count.ToString(CultureInfo.InvariantCulture) + " of "
                      + Ui.Count(_all.Count, "test") + " shown"));

                if (_outcome != null) said.Children.Add(Ui.Line(_outcome, 0.9));

                if (_went != null)
                    said.Children.Add(Ui.Link("View in Manage", () =>
                    {
                        ClashManageViews.Asked = _went;

                        Ui.Told(Shell.AppWindow.Show("clash", "manage"));
                    }));

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(said);

                // Primary bottom-right, which is where this product's one action always sits.
                var press = blocked == null
                    ? Ui.Runner(caption, _log, RunNow)
                    : Ui.Blocked(caption, blocked);

                press.VerticalAlignment = VerticalAlignment.Center;

                Grid.SetColumn(press, 1);
                row.Children.Add(press);

                _foot.Content = row;
            }

            /// <summary>Run whatever the footer's button covers.</summary>
            /// <param name="job">The job, for progress and a Stop.</param>
            private void RunNow(Job job) => Ran(Targets(), read => job.Step(read, "steps"), _log);

            /// <summary>Run whatever the picking bar handed over.</summary>
            /// <param name="picked">What was picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void RunPicked(IReadOnlyList<object> picked, Panel said) =>
                Ran(picked.OfType<Listed>().ToList(), null, said);

            /// <summary>
            /// Run these tests, one after another, and say what they found.
            ///
            /// Mixed kinds are ordinary here: each test is run through its own check's runner, and
            /// a kind that refuses stops the pass with its own reason rather than being skipped
            /// silently.
            /// </summary>
            /// <param name="targets">The tests to run.</param>
            /// <param name="onProgress">Called with how far it has got; returning false stops it.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void Ran(IReadOnlyList<Listed> targets, Func<int, bool>? onProgress, Panel say)
            {
                var blocked = Blocked(targets);

                if (blocked != null)
                {
                    say.Children.Add(Ui.Problem(blocked));
                    return;
                }

                var ran = 0;
                var stopped = (Listed?)null;
                var notes = new List<(string Test, IReadOnlyList<string> Said)>();

                foreach (var one in targets)
                {
                    var went = one.Check.Runner?.Run(one.Run.Id, onProgress) == true;

                    // Off the check straight after its run and before the next one starts: every
                    // check clears its notes at the top of a run, so a pass over five tests keeps
                    // only the fifth one's.
                    var said = one.Check.Notes;

                    if (said != null && said.Count > 0) notes.Add((one.Run.Name, said.ToList()));

                    if (!went)
                    {
                        stopped = one;
                        break;
                    }

                    ran++;
                }

                Reload();

                var after = _all.Where(one => targets.Any(was => was.Is(one.Check.Key, one.Run.Id)))
                                .ToList();

                var found = after.Sum(one => one.Run.ResultCount);

                _outcome = Ui.Count(ran, "test") + " run, " + Ui.Count(found, "finding") + ".";

                // The reason, where the button was pressed.
                //
                // Every check writes a specific sentence when it cannot do what it was asked —
                // "resolved to no surface to measure from", "gave no triangles within 1 m of 0 m" —
                // onto the run, and the only place that reached was the Manage board. What the
                // Create tab showed for all of them was "0 tests run, 0 findings", which is a dead
                // end wherever the answer already exists.
                if (stopped != null)
                    say.Children.Add(Ui.Problem(stopped.Run.Name + " did not run."));

                foreach (var (test, said) in notes)
                    foreach (var line in said)
                        say.Children.Add(Ui.Note(targets.Count > 1 ? test + ": " + line : line));

                var first = targets.Count > 0 ? targets[0] : null;

                _went = first == null
                    ? null
                    : new ClashManageViews.Request(
                        first.Check.Key,
                        targets.Where(one => string.Equals(one.Check.Key, first.Check.Key,
                                                           StringComparison.OrdinalIgnoreCase))
                               .Select(one => one.Run.Id).ToList());

                _seat.Record(ActivityKind.Regroup, "ran " + Ui.Count(ran, "test"));

                RedrawPanel();
                RedrawFoot();
            }

            // ---- Naming and deleting -------------------------------------------------------

            /// <summary>
            /// Copy one test, settings and all, and pick the copy.
            ///
            /// The second corridor and the eleventh storey are the first one with a different name
            /// and one field changed. Without this each of them was the whole form again, including
            /// re-dropping the same detected desk tops, while the matrix one tab over has offered
            /// "Duplicate this row, source and all" since it was written.
            /// </summary>
            /// <param name="picked">The tests picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Duplicate(IReadOnlyList<object> picked, Panel said)
            {
                var chosen = picked.OfType<Listed>().ToList();

                if (chosen.Count != 1)
                {
                    said.Children.Add(Ui.Note("Pick one test to duplicate."));

                    return;
                }

                var one = chosen[0];
                var name = Copied(one.Check, one.Run.Name);

                switch (one.Check)
                {
                    case ClearanceCheck clearance when clearance.Spec(one.Run.Id) is CheckRunSpec was:
                        Made(clearance,
                             was.CopyAs(Guid.NewGuid().ToString("N"), name, ClearanceDerived),
                             clearance, said);
                        break;

                    case FallCheck fall when fall.Spec(one.Run.Id) is CheckRunSpec was:
                        Made(fall, was.CopyAs(Guid.NewGuid().ToString("N"), name, FallDerived),
                             fall, said);
                        break;

                    case ClashCheck clash:
                        var copy = clash.Copy(one.Run.Id, name, one.Run.Folder, out var refused);

                        if (copy == null)
                        {
                            said.Children.Add(Ui.Problem(refused
                                ?? "The host would not copy that test."));
                            break;
                        }

                        PickedCheck = clash.Key;
                        PickedRun = copy.Value.Id;

                        Reload();
                        RedrawPanel();
                        RedrawFoot();

                        said.Children.Add(Ui.Done("Copied. It is between the same two sides at the "
                            + "same tolerance, and has not been run."));
                        break;

                    default:
                        said.Children.Add(Ui.Note(
                            one.Check.Name + " tests are not copied here."));
                        break;
                }
            }

            /// <summary>
            /// Settings a clearance copy does not inherit: what the last run read, and the surfaces
            /// somebody dropped, which are keyed to findings the copy has not produced.
            /// </summary>
            private static readonly string[] ClearanceDerived =
                { ClearanceSettings.LastSurfacesKey, ClearanceSettings.ExcludedKey };

            /// <summary>Settings a fall-hazard copy does not inherit, for the same reason.</summary>
            private static readonly string[] FallDerived = { FallSettings.LastFloorsKey };

            /// <summary>Save a copy and take it up, or say why the save was refused.</summary>
            /// <param name="check">The check that owns it.</param>
            /// <param name="copy">The copy.</param>
            /// <param name="into">The same check, as whatever can save a run.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Made(ICheck check, CheckRunSpec copy, object into, Panel said)
            {
                var problem = into is ClearanceCheck clearance
                    ? clearance.Put(copy)
                    : into is FallCheck fall ? fall.Put(copy) : "This test cannot be copied.";

                if (problem == null)
                    said.Children.Add(Ui.Done("Copied as \"" + copy.Name + "\". It has not been "
                                              + "run yet, and it holds no results."));

                Made(check, copy, problem);
            }

            /// <summary>The next unused name for a copy of this test.</summary>
            /// <param name="check">The check that owns it.</param>
            /// <param name="name">What the original is called.</param>
            private static string Copied(ICheck check, string name)
            {
                var held = check.Runs();

                for (var at = 2; ; at++)
                {
                    var wanted = name + " (" + at.ToString(CultureInfo.InvariantCulture) + ")";

                    if (!held.Any(two => string.Equals(two.Name, wanted,
                                                       StringComparison.OrdinalIgnoreCase)))
                        return wanted;
                }
            }

            /// <summary>Rename one test, through whichever check owns it.</summary>
            /// <param name="picked">The tests picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Rename(IReadOnlyList<object> picked, Panel said)
            {
                var chosen = picked.OfType<Listed>().ToList();

                if (chosen.Count != 1)
                {
                    said.Children.Add(Ui.Note("Pick one test to rename."));
                    return;
                }

                var one = chosen[0];
                var naming = one.Check.Naming;

                if (naming == null)
                {
                    said.Children.Add(Ui.Note(one.Check.Name + " tests cannot be renamed here."));
                    return;
                }

                Parts.Ask(said, "Call it", one.Run.Name, "Rename", name =>
                {
                    if (!naming.Rename(one.Run.Id, name)) return;

                    Reload();

                    said.Children.Add(Ui.Done("Renamed. Its findings keep their decisions: the "
                        + "test's id is what they are keyed on, not its name."));
                });
            }

            /// <summary>File tests in a folder, or take them out of one.</summary>
            /// <param name="picked">The tests picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Refile(IReadOnlyList<object> picked, Panel said)
            {
                var chosen = picked.OfType<Listed>().Where(one => one.Check.Naming != null).ToList();

                if (chosen.Count == 0)
                {
                    said.Children.Add(Ui.Note("These tests cannot be refiled here."));
                    return;
                }

                Parts.Ask(said, "Folder, or empty for none", chosen[0].Run.Folder ?? string.Empty,
                          "Move", folder =>
                {
                    var into = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim();

                    // What the host said, not what was asked of it. Every refusal used to be
                    // dropped on the floor and the bar reported the whole selection moved.
                    var refused = chosen
                        .Where(one => one.Check.Naming?.Refile(one.Run.Id, into) != true)
                        .ToList();

                    Reload();

                    var moved = chosen.Count - refused.Count;

                    if (moved > 0)
                        said.Children.Add(Ui.Done(Ui.Count(moved, "test") + " moved"
                            + (into == null ? " out of their folders." : " into " + into + ".")));

                    if (refused.Count > 0)
                        said.Children.Add(Ui.Problem(Ui.Count(refused.Count, "test")
                            + " could not be moved: "
                            + string.Join(", ", refused.Take(3).Select(one => one.Run.Name))
                            + (refused.Count > 3 ? " and others." : ".")));
                }, Folders());
            }

            /// <summary>The folders the tests are already filed in, for the move prompt.</summary>
            private IReadOnlyList<string> Folders() =>
                Ui.Listed(_all.Select(one => one.Run.Folder));

            /// <summary>Delete tests, and the findings that name them.</summary>
            /// <param name="picked">The tests picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Delete(IReadOnlyList<object> picked, Panel said)
            {
                var chosen = picked.OfType<Listed>().ToList();

                if (chosen.Count == 0) return;

                var found = chosen.Sum(one => one.Run.ResultCount);

                Parts.Sure(said, "Delete " + Ui.Count(chosen.Count, "test") + " and "
                           + Ui.Count(found, "finding") + "?", "Delete", () =>
                {
                    foreach (var byCheck in chosen.GroupBy(one => one.Check))
                        byCheck.Key.Runner?.Delete(byCheck.Select(one => one.Run.Id).ToList());

                    if (chosen.Any(one => one.Is(PickedCheck, PickedRun))) Forget();

                    Reload();
                    RedrawPanel();
                    RedrawFoot();

                    said.Children.Add(Ui.Done(Ui.Count(chosen.Count, "test") + " deleted."));
                });
            }
        }
    }
}
