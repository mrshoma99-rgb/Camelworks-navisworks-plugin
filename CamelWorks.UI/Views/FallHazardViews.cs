using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Checks ▸ Fall hazard: the set-up for a run that looks for holes in a floor and edges with
    /// no handrail.
    ///
    /// The second set-up screen on the Checks tab, beside clearance, and the shape is the same: a
    /// check picker, the runs this project has saved, and the settings of the run being set up in
    /// windows off the action row. Nine settings is why they are in windows: a tab that laid them
    /// beside the run list would be a form somebody scrolls rather than a list of runs.
    ///
    /// The results are not here. An opening to cover and an edge to rail are places to argue
    /// about, assign and close, so they are worked on the Manage board with everything else, and
    /// pressing Run goes there.
    /// </summary>
    public static class FallHazardViews
    {
        /// <summary>
        /// The surface, for a desk to bench.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="frame">
        /// The tab's frame, so picking another check swaps the set-up in place. Null draws the
        /// screen on its own, which is what the render harness wants.
        /// </param>
        public static UIElement Setup(ISeat seat, ContentControl? frame = null) =>
            new Screen(seat, frame).Build();

        /// <summary>
        /// The two set-up windows' contents, one under the other, for the render harness.
        ///
        /// Drawn rather than shown: a window needs a screen to open on and the harness has none,
        /// so without this the nine settings this check is configured by could only be looked at
        /// on a licensed seat.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="run">The saved run to show the settings of. It must already be saved.</param>
        public static UIElement Drawn(ISeat seat, CheckRunSpec run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            ClearanceViews.Picked = run.Id;

            var screen = new Screen(seat, null);

            return Ui.Stack(screen.ReadPage(), screen.JudgePage());
        }

        // -----------------------------------------------------------------------------------

        private sealed class Screen
        {
            private readonly ISeat _seat;
            private readonly ContentControl? _frame;

            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _runs = Ui.Stack();
            private readonly StackPanel _picker = Ui.Stack();

            internal Screen(ISeat seat, ContentControl? frame)
            {
                _seat = seat;
                _frame = frame;
            }

            private Core.Measurement.Measure Units => _seat.Profile.Units;

            /// <summary>The fall-hazard check, or null when this session registered none.</summary>
            private FallCheck? Check => _seat.Checks.All.OfType<FallCheck>().FirstOrDefault();

            /// <summary>The run being set up, or null when none is picked.</summary>
            private CheckRunSpec? Run() => Check?.Spec(ClearanceViews.Picked);

            internal UIElement Build()
            {
                RedrawPicker();
                RedrawRuns();

                return Bench.Surface(null, Foot(Ui.Runner("Run", _log, RunNow)),
                                     Ui.Stack(_picker, _log, _runs));
            }

            /// <summary>The action row: run, then the windows the set-up lives in.</summary>
            /// <param name="run">The Run button.</param>
            private UIElement Foot(UIElement run)
            {
                var row = new WrapPanel();

                row.Children.Add(run);
                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Icon(Glyphs.Layers, "What to read", () => Configure("read")));
                row.Children.Add(Ui.Icon(Glyphs.Ruler, "How to judge it", () => Configure("judge")));

                return row;
            }

            /// <summary>Which check's runs are being set up, above the list of them.</summary>
            private void RedrawPicker()
            {
                _picker.Children.Clear();

                var row = new WrapPanel();

                row.Children.Add(CheckPicker.Draw(_seat.Checks.All, Check, _seat.Findings.Findings,
                                                  Pick));
                row.Children.Add(new Border { Width = 10 });
                row.Children.Add(Ui.Icon(Glyphs.Add, "A new run", NewRun));

                _picker.Children.Add(Ui.Loose(row));
            }

            /// <summary>Set up another check's runs, which is another screen in the same frame.</summary>
            /// <param name="check">The check picked.</param>
            private void Pick(ICheck check) =>
                Ui.Safely(() => ClearanceViews.Pick(_frame, _seat, check));

            /// <summary>
            /// The runs this check has saved, with what each covers and when it last ran.
            /// </summary>
            private void RedrawRuns()
            {
                _runs.Children.Clear();

                var check = Check;

                if (check == null)
                {
                    _runs.Children.Add(Ui.Empty("This session registered no fall-hazard check.",
                        "Nothing here has set-up of its own, which is a fault in this build."));

                    return;
                }

                if (!check.IsAvailable)
                {
                    _runs.Children.Add(Ui.Info(check.Unavailable ?? "This seat cannot offer it."));
                    return;
                }

                var saved = check.Runs();

                if (saved.Count == 0)
                {
                    _runs.Children.Add(Bench.Ready(
                        "Draws a plan of the floor at one level and lists every opening wide "
                        + "enough to fall through and every floor edge with no handrail along it.",
                        "A hole in a slab clashes with nothing and has no surface over it to "
                        + "measure down from, so neither the clash tests nor the clearance check "
                        + "report one. What this finds is worked on the Manage tab beside them."));

                    _runs.Children.Add(Ui.Empty("No " + check.RunWord + "s yet.",
                        "A " + check.RunWord + " is a name, a level, what to read the floor from "
                        + "and the gap that needs a handrail. It is saved in the project, so "
                        + "re-running it later is one press.",
                        "New " + check.RunWord, NewRun));

                    return;
                }

                var rows = saved.Select(one => new TableRow(one,
                    one.Name,
                    one.Folder ?? "—",
                    one.Between,
                    one.Threshold ?? "—",
                    Ui.Count(one.ResultCount, check.ResultWord),
                    When(one))
                {
                    Tone = string.Equals(one.Id, ClearanceViews.Picked, StringComparison.Ordinal)
                        ? Tone.Good
                        : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                                     ("Run", 0), ("Folder", 110), ("What it covers", 300),
                                     ("Threshold", 130), ("Found", 110), ("Last run", 130));

                table.SelectionChanged += (s, e) =>
                {
                    if (!(table.SelectedItem is TableRow row) || !(row.Source is CheckRun one)) return;

                    ClearanceViews.Picked = one.Id;
                };

                _runs.Children.Add(table);

                _runs.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Run these {n}", (picked, said) => RunPicked(picked, said)),
                    new Ui.OnPicked("Rename this one...", (picked, said) => Rename(picked, said)),
                    new Ui.OnPicked("Delete these {n}", (picked, said) => Delete(picked, said))));

                var waiting = saved.Count(one => one.LastRunTicks == 0);

                if (waiting > 0)
                    _runs.Children.Add(Ui.Small(Ui.Count(waiting, check.RunWord)
                        + " saved and not yet run."));
            }

            /// <summary>When a run last ran, or that it never has.</summary>
            /// <param name="run">The run.</param>
            private static string When(CheckRun run) =>
                run.LastRunTicks == 0
                    ? "not yet run"
                    : new DateTime(run.LastRunTicks, DateTimeKind.Utc).ToLocalTime()
                        .ToString("d MMM HH:mm", CultureInfo.CurrentCulture);

            /// <summary>Save a new run and open the set-up on it.</summary>
            private void NewRun()
            {
                var check = Check;

                if (check == null) return;

                var made = new CheckRunSpec(check.Key, Guid.NewGuid().ToString("N"),
                                            "Fall hazard " + (check.Runs().Count + 1)
                                                .ToString(CultureInfo.InvariantCulture));

                new FallSettings().Write(made);

                var problem = check.Put(made);

                ClearanceViews.Picked = made.Id;

                RedrawRuns();

                if (problem != null) _log.Children.Add(Ui.Problem(problem));

                Configure("read");
            }

            /// <summary>The runs a picking bar handed over.</summary>
            /// <param name="picked">What was picked.</param>
            private static List<CheckRun> Chosen(IReadOnlyList<object> picked) =>
                picked.OfType<CheckRun>().ToList();

            /// <summary>
            /// Run these runs, one after another, and go to the board — which is where the results
            /// are, so a run that finished lands on them rather than on a screen saying so.
            /// </summary>
            /// <param name="runIds">Which runs.</param>
            /// <param name="onProgress">Called with how far it has got; returning false stops it.</param>
            /// <param name="say">Where the outcome goes.</param>
            private void Ran(IReadOnlyList<string> runIds, Func<int, bool>? onProgress, Panel say)
            {
                var check = Check;

                if (check?.Runner == null || runIds.Count == 0)
                {
                    say.Children.Add(Ui.Problem("Pick a run first, or make one."));
                    return;
                }

                if (!check.Runner.CanRun)
                {
                    say.Children.Add(Ui.Problem(check.Runner.CannotRun ?? "This run cannot run here."));
                    return;
                }

                foreach (var id in runIds)
                    if (!check.Runner.Run(id, onProgress)) break;

                foreach (var note in check.Notes) say.Children.Add(Ui.Note(note));

                RedrawRuns();

                _seat.Record(ActivityKind.Regroup,
                             "ran " + Ui.Count(runIds.Count, check.RunWord) + " of "
                             + check.Name.ToLowerInvariant());

                Onward(check, runIds);
            }

            /// <summary>Run whatever the run list has picked.</summary>
            /// <param name="picked">The runs picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void RunPicked(IReadOnlyList<object> picked, Panel said)
            {
                var runs = Chosen(picked);

                if (runs.Count > 0) ClearanceViews.Picked = runs[0].Id;

                Ran(runs.Select(one => one.Id).ToList(), null, said);
            }

            /// <summary>Hand off to the board: this check picked, those runs selected.</summary>
            /// <param name="check">The check.</param>
            /// <param name="runIds">The runs to select.</param>
            private static void Onward(ICheck check, IReadOnlyList<string> runIds)
            {
                ClashManageViews.Asked = new ClashManageViews.Request(check.Key, runIds);

                Ui.Told(Shell.AppWindow.Show("clash", "manage"));
            }

            /// <summary>Rename one run.</summary>
            /// <param name="picked">The runs picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Rename(IReadOnlyList<object> picked, Panel said)
            {
                var runs = Chosen(picked);

                if (runs.Count != 1)
                {
                    said.Children.Add(Ui.Note("Pick one run to rename."));
                    return;
                }

                var one = runs[0];

                Parts.Ask(said, "Call it", one.Name, "Rename", name =>
                {
                    if (Check?.Naming?.Rename(one.Id, name) != true) return;

                    RedrawRuns();

                    said.Children.Add(Ui.Done("Renamed. Its findings keep their decisions: the "
                        + "run's id is what they are keyed on, not its name."));
                });
            }

            /// <summary>Delete runs, and the findings that name them.</summary>
            /// <param name="picked">The runs picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void Delete(IReadOnlyList<object> picked, Panel said)
            {
                var runs = Chosen(picked);

                if (runs.Count == 0) return;

                var found = runs.Sum(one => one.ResultCount);

                Parts.Sure(said, "Delete " + Ui.Count(runs.Count, "run") + " and "
                           + Ui.Count(found, "finding") + "?", "Delete", () =>
                {
                    Check?.Runner?.Delete(runs.Select(one => one.Id).ToList());

                    if (runs.Any(one => string.Equals(one.Id, ClearanceViews.Picked,
                                                      StringComparison.Ordinal)))
                        ClearanceViews.Picked = null;

                    RedrawRuns();

                    said.Children.Add(Ui.Done(Ui.Count(runs.Count, "run") + " deleted."));
                });
            }

            /// <summary>Run the picked run from the action row.</summary>
            /// <param name="job">The job, for progress and a Stop.</param>
            private void RunNow(Job job) =>
                Ran(ClearanceViews.Picked == null
                        ? Array.Empty<string>()
                        : new[] { ClearanceViews.Picked! },
                    read => job.Step(read, "elements read"), _log);

            /// <summary>What one symbol on the action row is for, in a window of its own.</summary>
            /// <param name="section">Which section the symbol names.</param>
            private void Configure(string section)
            {
                Ui.Page page;

                switch (section)
                {
                    case "judge": page = new Ui.Page("How to judge it", JudgePage); break;
                    default: page = new Ui.Page("What to read", ReadPage); break;
                }

                Ui.Configure(page.Name, () => Ui.Safely(RedrawRuns), page);
            }

            /// <summary>Write the settings back onto the run and into the project file.</summary>
            /// <param name="change">What to change.</param>
            private void Keep(Action<FallSettings> change)
            {
                var spec = Run();
                var check = Check;

                if (spec == null || check == null) return;

                var settings = FallSettings.From(spec);

                change(settings);
                settings.Write(spec);

                var problem = check.Put(spec);

                if (problem != null) _log.Children.Add(Ui.Problem(problem));
            }

            /// <summary>What a set-up page says when there is no run to set up.</summary>
            /// <param name="title">The page's own heading.</param>
            private UIElement NoRun(string title) =>
                Bench.Group(title, Ui.Empty("No run picked.",
                    "The settings belong to a run, so there is nothing to change until there is "
                    + "one.", "New run", NewRun));

            internal UIElement ReadPage()
            {
                var spec = Run();

                if (spec == null) return NoRun("What to read");

                var settings = FallSettings.From(spec);
                var sides = new List<string> { FallSettings.Loaded, FallSettings.Selected };

                sides.AddRange(Sets());

                var optional = new List<string> { FallSettings.Nothing };

                optional.AddRange(sides);

                return Ui.Stack(
                    Bench.Group("The run",
                        Ui.Field("Name", Ui.Text(spec.Name, name => Keep(_ => spec.Name = name), 220)),
                        Ui.Field("Folder", Ui.Text(spec.Folder ?? string.Empty,
                                                   folder => Keep(_ => spec.Folder =
                                                       string.IsNullOrWhiteSpace(folder) ? null : folder),
                                                   220),
                                 "how the board's rail files it")),
                    Bench.Group("What to read",
                        Ui.Field("Floor from",
                                 Ui.Choice(Offered(sides, settings.Floors), settings.Floors,
                                           pick => Keep(one => one.Floors = pick), 300),
                                 "the slabs the plan is drawn from"),
                        Ui.Field("Handrails from",
                                 Ui.Choice(Offered(optional, settings.Handrails), settings.Handrails,
                                           pick => Keep(one => one.Handrails = pick), 300),
                                 "what protects an edge"),
                        Ui.Field("Obstructions from",
                                 Ui.Choice(Offered(optional, settings.Obstructions),
                                           settings.Obstructions,
                                           pick => Keep(one => one.Obstructions = pick), 300),
                                 "equipment that plugs an opening"),
                        Ui.Small("Every list is this model's own search sets and selection sets, "
                                 + "the current selection, or everything loaded. Name a set of "
                                 + "floors: everything loaded reads the whole federation at this "
                                 + "level, walls and ducts included, and each of those projects "
                                 + "its own outline onto the plan as though it were floor.")),
                    Bench.Group("Where to cut the plan",
                        Ui.Field("Level",
                                 Ui.Text(Units.Bare(settings.LevelMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres))
                                                 one.LevelMetres = metres;
                                         }), 90),
                                 "the elevation the plan is cut at, in " + Units.LengthUnit),
                        Ui.Field("Band",
                                 Ui.Text(Units.Bare(settings.BandMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres > 0)
                                                 one.BandMetres = metres;
                                         }), 90),
                                 "how far above and below the level an element still counts"),
                        Ui.Small("An element whose box reaches the band contributes its whole "
                                 + "outline in plan, so the band does not have to match a slab "
                                 + "thickness. It is also why a column or a stair through the "
                                 + "band reads as solid floor rather than as a hole, which is the "
                                 + "largest thing this check gets wrong and is written down."),
                        Sets().Count == 0
                            ? Ui.Note("This document holds no saved sets yet. Build one in Set "
                                      + "Builder and it appears in all three lists.")
                            : null));
            }

            internal UIElement JudgePage()
            {
                var spec = Run();

                if (spec == null) return NoRun("How to judge it");

                var settings = FallSettings.From(spec);

                return Ui.Stack(
                    Bench.Group("How to judge it",
                        Ui.Field("Gap allowed",
                                 Ui.Text(Units.Bare(settings.LimitMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres > 0)
                                                 one.LimitMetres = metres;
                                         }), 90),
                                 "an edge facing a wider void needs a handrail"),
                        Ui.Field("Cell size",
                                 Ui.Text(Units.Bare(settings.CellMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres > 0)
                                                 one.CellMetres = metres;
                                         }), 90),
                                 "how finely the plan is drawn"),
                        Ui.Field("Handrail reach",
                                 Ui.Text(Units.Bare(settings.HandrailToleranceMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres >= 0)
                                                 one.HandrailToleranceMetres = metres;
                                         }), 90),
                                 "how close a rail must be to protect an edge"),
                        Ui.Field("Smallest break",
                                 Ui.Text(Units.Bare(settings.MinPassageMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres >= 0)
                                                 one.MinPassageMetres = metres;
                                         }), 90),
                                 "a shorter break in the protection is not reported"),
                        Ui.Small("The plan is drawn cell by cell and a cell is judged at its "
                                 + "centre, so anything thinner than one cell is a coin flip. Set "
                                 + "the cell size a few times under the gap being judged: a "
                                 + "250 mm cell cannot tell a 200 mm gap from a solid floor."),
                        Ui.Small("Halving the cell size doubles the plan in each direction, so it "
                                 + "costs four times the work. Over six million cells the run "
                                 + "stops and says so rather than taking the session with it."),
                        Ui.Small("The smallest break is the width of a person, not of the gap: "
                                 + "a 300 mm break between two handrails is too narrow to fall "
                                 + "through. Leave it at nothing and every break is reported.")));
            }

            /// <summary>One side's list, with whatever it is set to kept in it.</summary>
            /// <param name="offered">What this build offers.</param>
            /// <param name="chosen">What the side is set to.</param>
            private static List<string> Offered(IEnumerable<string> offered, string chosen)
            {
                var list = offered.ToList();

                if (!list.Contains(chosen, StringComparer.OrdinalIgnoreCase)) list.Add(chosen);

                return list;
            }

            /// <summary>
            /// Every saved set this document holds, named once, or nothing when it will not
            /// answer: a picker that cannot be filled is a picker with nothing in it, never a
            /// screen that refuses to open.
            /// </summary>
            private List<string> Sets()
            {
                try
                {
                    return _seat.Sets().Select(set => set.Name)
                                .Where(name => !string.IsNullOrWhiteSpace(name))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                }
                catch (Exception)
                {
                    return new List<string>();
                }
            }
        }
    }
}
