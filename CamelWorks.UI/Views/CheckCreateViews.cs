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

                var left = Bench.Pane("Tests", null, Ui.Stack(_log, _list), null);
                var right = Bench.Pane("Settings", null, _panel, null);

                return Bench.Split(Strip(), left, right, _foot, 470);
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
                    RedrawFoot();
                    return;
                }

                if (shown.Count == 0)
                {
                    _table = null;

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
                string? band = null;

                foreach (var one in shown)
                {
                    var folder = one.Run.Folder ?? string.Empty;

                    // The folder is printed once per group and blank on the rows under it, which
                    // is the grouping. One table rather than a table per folder, because a
                    // selection has to be able to span folders and a picking bar can only serve
                    // one table.
                    var head = string.Equals(folder, band, StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : folder.Length == 0 ? "Not filed" : folder;

                    band = folder;

                    rows.Add(new TableRow(one, head, one.Run.Name, one.Check.Name,
                                          one.Run.Between, Found(one))
                    {
                        Tone = one.Is(PickedCheck, PickedRun) ? Tone.Good : Tone.Plain,
                    });
                }

                var table = Ui.Table(rows,
                                     ("Folder", 120), ("Test", 0), ("Kind", 120),
                                     ("What it covers", 210), ("Last run", 150)).Multi();

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
                    new Ui.OnPicked("Rename this one...", (picked, said) => Rename(picked, said)),
                    new Ui.OnPicked("Move these {n}...", (picked, said) => Refile(picked, said)),
                    new Ui.OnPicked("Delete these {n}", (picked, said) => Delete(picked, said)),
                };

                // The same verbs on right-click, writing into the screen's own log: OnMenu carries
                // no panel of its own, and a verb that says nothing is a verb reported as broken.
                table.Menu(verbs.Select(one =>
                    new Ui.OnMenu(one.Caption, chosen => one.Act(chosen, _log))).ToArray());

                _list.Content = Ui.Stack(table, Ui.Picking(table, verbs.ToArray()));
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
            /// The kinds of test that can be made, and where the ones that cannot are made instead.
            ///
            /// A switch on the check's own type, which is where the asymmetry between the kinds
            /// actually lives: <see cref="ICheck"/> lists and runs tests and has no member that
            /// makes one, because only the two project-file checks can be handed a new
            /// <see cref="CheckRunSpec"/>. A clash test needs two sides resolved against the
            /// document, which is the Clash matrix.
            /// </summary>
            private IReadOnlyList<Parts.Entry> Kinds()
            {
                var made = new List<Parts.Entry>();

                foreach (var check in Checks())
                {
                    switch (check)
                    {
                        case ClearanceCheck clearance:
                            made.Add(Parts.Entry.Saying(check.Name, Glyphs.Ruler,
                                "The clear height over the surfaces you name. A duct 1.9 m over a "
                                + "corridor clashes with nothing.",
                                () => NewClearance(clearance)));
                            break;

                        case FallCheck fall:
                            made.Add(Parts.Entry.Saying(check.Name, Glyphs.Layers,
                                "Openings wide enough to fall through, and floor edges with no "
                                + "handrail, at one elevation.",
                                () => NewFall(fall)));
                            break;

                        default:
                            made.Add(Parts.Entry.Saying(check.Name, Glyphs.Clash,
                                "Built on the Clash matrix, which resolves the two sides against "
                                + "the model.",
                                () => Ui.Told(Shell.AppWindow.Show("clash", "matrix"))));
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
                var picked = Picked();

                if (picked == null)
                {
                    _panel.Content = _all.Count == 0
                        ? Ui.Empty("Nothing to set up yet.",
                                   "Make a test on the left and its settings appear here.",
                                   "New test", () => Parts.PopMenu(_make, Kinds()))
                        : Ui.Empty("No test picked.",
                                   "Pick one on the left to change what it covers and what it "
                                   + "reports.");

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

                var open = Ui.Button("Open the Clash matrix",
                                     () => Ui.Told(Shell.AppWindow.Show("clash", "matrix")));

                return Ui.Stack(
                    Bench.Group("Test",
                        Ui.Across(Sized(Ui.Field("Name", name), 210),
                                  Sized(Ui.Field("Folder", folder), 170)),
                        Sized(Ui.Field("Tolerance (" + Units.LengthUnit + ")", tolerance), 170)),
                    Bench.Group("Sides",
                        Ui.Line(run.Between, 0.85),
                        Ui.Info("A clash test is held in the Navisworks document, so its two sides "
                                + "are set on the Clash matrix and not here."),
                        open),
                    blocked == null ? null : Ui.Note(blocked));
            }

            /// <summary>Write a clash test's tolerance back to the host, keeping the kind it has.</summary>
            /// <param name="testId">The host's id for the test.</param>
            /// <param name="metres">The tolerance it should now have.</param>
            private void Retune(string testId, double metres)
            {
                var kind = MatrixDiff.Kinded(_seat.Clash.Sides(testId)?.Kind);

                if (_seat.Clash.Retune(testId, metres, kind)) return;

                _log.Children.Add(Ui.Problem(_seat.Clash.LastProblem
                                             ?? "The document refused the tolerance."));
            }

            /// <summary>A field capped to a readable width, so short fields sit two to a row.</summary>
            /// <param name="field">The field.</param>
            /// <param name="width">How wide it may be.</param>
            private static UIElement Sized(Panel field, double width)
            {
                field.Width = width;

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

                foreach (var one in targets)
                {
                    if (one.Check.Runner?.Run(one.Run.Id, onProgress) != true) break;

                    ran++;
                }

                Reload();

                var after = _all.Where(one => targets.Any(was => was.Is(one.Check.Key, one.Run.Id)))
                                .ToList();

                var found = after.Sum(one => one.Run.ResultCount);

                _outcome = Ui.Count(ran, "test") + " run, " + Ui.Count(found, "finding") + ".";

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
                    var into = string.IsNullOrWhiteSpace(folder) ? null : folder;

                    foreach (var one in chosen) one.Check.Naming?.Refile(one.Run.Id, into);

                    Reload();

                    said.Children.Add(Ui.Done(Ui.Count(chosen.Count, "test") + " moved."));
                });
            }

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
