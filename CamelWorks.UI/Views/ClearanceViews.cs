using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Clearance: the set-up for this project's clear-height runs.
    ///
    /// Its own tab, beside Fall hazard. The two were briefly one "Checks" tab with a check picker
    /// inside it; a set-up tab per check is what the product asks for, and the common viewer is
    /// the Manage tab, where the picker belongs.
    ///
    /// The results are not here. A clearance breach is a place to argue about, assign and close,
    /// which is a clash result in everything but how it was found, so it is worked on the Manage
    /// board with the same grouping, sorting, filtering, exchange, renaming and export. This screen
    /// says what to measure and presses Run; pressing Run goes to the board.
    ///
    /// Both inputs are model-fed: every search set and selection set the document holds, the
    /// current selection, or everything loaded. The one guess it offers is labelled as a guess and
    /// says which words it matches.
    ///
    /// The runs are project-file state, so the screen is whole without a model. Nothing here
    /// collapses to a notice: the runs are listed, and one can be made, renamed, edited and
    /// deleted with nothing open. What needs a document — running the check, the pickers that read
    /// the document's sets, and surface detection — is disabled and says why.
    /// </summary>
    public static class ClearanceViews
    {
        /// <summary>The words the guess matches, on the element's category or type.</summary>
        internal static IReadOnlyList<string> WalkingWords => ClearanceCheck.WalkingWords;

        /// <summary>
        /// Which run is being set up, held between visits.
        ///
        /// This screen's own, not shared with any other check's screen. A federation sweep is a
        /// minute of traversal and glancing at another tab must not cost it.
        /// </summary>
        internal static string? Picked { get; set; }

        /// <summary>
        /// The surface, for a desk to bench.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        public static UIElement Setup(ISeat seat) => new Screen(seat).Build();

        static ClearanceViews() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Forget what was detected and which run was picked, so the next visit starts clean.
        /// Surfaces detected in the federation that was open are not surfaces in the one that is.
        /// The runs themselves are in the project file and survive, which is the point of saving
        /// them.
        /// </summary>
        internal static void Forget()
        {
            ClearanceSurfaces.Forget();

            Picked = null;
        }

        /// <summary>
        /// The option's whole sentence, caveat included. An option that says only "detected
        /// walkable surfaces" reads as though the software found the floors.
        /// </summary>
        /// <param name="units">The seat's units, so the area reads in them.</param>
        /// <param name="minimumAreaSquareMetres">The smallest patch detection will report.</param>
        internal static string DetectedSaid(Measure units, double minimumAreaSquareMetres) =>
            ClearanceSettings.Detected + " — every up-facing surface over " + units.Area(minimumAreaSquareMetres)
            + ", which includes desk tops and duct tops.";

        // -----------------------------------------------------------------------------------

        private sealed class Screen
        {
            private readonly ISeat _seat;

            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _runs = Ui.Stack();
            private readonly StackPanel _head = Ui.Stack();

            internal Screen(ISeat seat) => _seat = seat;

            private Measure Units => _seat.Profile.Units;

            /// <summary>The clearance check, or null when this session registered none.</summary>
            private ClearanceCheck? Clearance =>
                _seat.Checks.All.OfType<ClearanceCheck>().FirstOrDefault();

            /// <summary>Why the check cannot be run here, or null when it can.</summary>
            private string? Blocked
            {
                get
                {
                    var check = Clearance;

                    if (check == null)
                        return "This session registered no clearance check, which is a fault in "
                               + "this build.";

                    return check.IsAvailable
                        ? null
                        : check.Unavailable ?? "This seat cannot offer it.";
                }
            }

            /// <summary>The run being set up, or null when none is picked.</summary>
            private CheckRunSpec? Run() => Clearance?.Spec(Picked);

            internal UIElement Build()
            {
                RedrawHead();
                RedrawRuns();

                return Bench.Surface(null, Foot(), Ui.Stack(_head, _log, _runs));
            }

            /// <summary>
            /// The action row: run, then the windows the set-up lives in.
            ///
            /// Run is drawn disabled with its reason when the check cannot run. A primary button
            /// over a notice saying there is nothing to measure is a press that reports its own
            /// failure.
            /// </summary>
            private UIElement Foot()
            {
                var row = new WrapPanel();
                var blocked = Blocked;

                row.Children.Add(blocked == null
                    ? (UIElement)Ui.Runner("Run", _log, RunNow)
                    : Ui.Blocked("Run", blocked));

                row.Children.Add(new Border { Width = 14 });

                row.Children.Add(Ui.Icon(Glyphs.Ruler, "What to measure", () => Configure("measure")));
                row.Children.Add(Ui.Icon(Glyphs.Filter, "Scope and reductions", () => Configure("scope")));

                // Detection reads the document's triangles, so with no model there is nothing to
                // detect. The symbol stays on the row, greyed, with the reason on it.
                var detected = Ui.Icon(Glyphs.Layers, blocked ?? ClearanceSettings.Detected,
                                       () => Configure("detected"));

                detected.IsEnabled = blocked == null;

                row.Children.Add(detected);

                return row;
            }

            /// <summary>The one verb that makes a run, above the list of them.</summary>
            private void RedrawHead()
            {
                _head.Children.Clear();

                var row = new WrapPanel();

                row.Children.Add(Ui.Icon(Glyphs.Add, "A new run", NewRun));

                _head.Children.Add(Ui.Loose(row));
            }

            /// <summary>
            /// The runs this check has saved, with what each covers and when it last ran. A run
            /// saved and never run and a run that found nothing are opposite facts and different
            /// lines.
            /// </summary>
            private void RedrawRuns()
            {
                _runs.Children.Clear();

                var check = Clearance;

                if (check == null)
                {
                    _runs.Children.Add(Ui.Empty("This session registered no clearance check.",
                        "Nothing here has set-up of its own, which is a fault in this build."));

                    return;
                }

                // Said, not substituted for the list. The runs are in the project file, so a seat
                // that cannot measure can still list them and set one up; only Run is blocked.
                if (!check.IsAvailable)
                    _runs.Children.Add(Ui.Info(check.Unavailable ?? "This seat cannot offer it."));

                var saved = check.Runs();

                if (saved.Count == 0)
                {
                    _runs.Children.Add(Bench.Ready(
                        "Measures the clear height over every surface you name and lists every "
                        + "place the minimum is not there.",
                        "A duct 1.9 m over a corridor clashes with nothing — this is the check "
                        + "that finds it, and what it finds is worked on the Manage tab beside "
                        + "the clashes."));

                    _runs.Children.Add(Ui.Empty("No " + check.RunWord + "s yet.",
                        "A " + check.RunWord + " is a name, a clear height and what to measure it "
                        + "over. It is saved in the project, so re-running it later is one press.",
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
                    Tone = string.Equals(one.Id, Picked, StringComparison.Ordinal)
                        ? Tone.Good
                        : Tone.Plain,
                }).ToList();

                var table = Ui.Table(rows,
                                     ("Run", 0), ("Folder", 110), ("What it covers", 260),
                                     ("Threshold", 120), ("Found", 110), ("Last run", 130));

                table.SelectionChanged += (s, e) =>
                {
                    if (!(table.SelectedItem is TableRow row) || !(row.Source is CheckRun one)) return;

                    Picked = one.Id;
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
                var check = Clearance;

                if (check == null) return;

                var made = new CheckRunSpec(check.Key, Guid.NewGuid().ToString("N"),
                                            "Clearance " + (check.Runs().Count + 1)
                                                .ToString(CultureInfo.InvariantCulture));

                new ClearanceSettings().Write(made);

                var problem = check.Put(made);

                Picked = made.Id;

                RedrawRuns();

                if (problem != null) _log.Children.Add(Ui.Problem(problem));

                Configure("measure");
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
                var check = Clearance;

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

                foreach (var note in Clearance?.Notes ?? Array.Empty<string>())
                    say.Children.Add(Ui.Note(note));

                RedrawRuns();

                _seat.Record(ActivityKind.Regroup,
                             "ran " + Ui.Count(runIds.Count, check.RunWord) + " of "
                             + check.Name.ToLowerInvariant());

                Onward(runIds);
            }

            /// <summary>Run whatever the run list has picked.</summary>
            /// <param name="picked">The runs picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            private void RunPicked(IReadOnlyList<object> picked, Panel said)
            {
                var runs = Chosen(picked);

                if (runs.Count > 0) Picked = runs[0].Id;

                Ran(runs.Select(one => one.Id).ToList(), null, said);
            }

            /// <summary>
            /// Hand off to the board: the check picked, those runs selected. The same pattern the
            /// Exchange tab is handed its tests by.
            /// </summary>
            /// <param name="runIds">The runs to select.</param>
            private void Onward(IReadOnlyList<string> runIds)
            {
                var check = Clearance;

                if (check == null) return;

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
                    if (Clearance?.Naming?.Rename(one.Id, name) != true) return;

                    RedrawRuns();

                    said.Children.Add(Ui.Done("Renamed. Its findings keep their decisions: the run's "
                        + "id is what they are keyed on, not its name."));
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
                    Clearance?.Runner?.Delete(runs.Select(one => one.Id).ToList());

                    if (runs.Any(one => string.Equals(one.Id, Picked, StringComparison.Ordinal)))
                        Picked = null;

                    RedrawRuns();

                    said.Children.Add(Ui.Done(Ui.Count(runs.Count, "run") + " deleted."));
                });
            }

            /// <summary>Run the picked run from the action row.</summary>
            /// <param name="job">The job, for progress and a Stop.</param>
            private void RunNow(Job job) =>
                Ran(Picked == null ? Array.Empty<string>() : new[] { Picked },
                    read => job.Step(read, "elements read"), _log);

            /// <summary>What one symbol on the action row is for, in a window of its own.</summary>
            /// <param name="section">Which section the symbol names.</param>
            private void Configure(string section)
            {
                Ui.Page page;

                switch (section)
                {
                    case "scope": page = new Ui.Page("Scope and reductions", ScopePage); break;
                    case "detected": page = new Ui.Page(ClearanceSettings.Detected, Surfaces); break;
                    default: page = new Ui.Page("What to measure", MeasurePage); break;
                }

                Ui.Configure(page.Name, () => Ui.Safely(RedrawRuns), page);
            }

            /// <summary>Write the settings back onto the run and into the project file.</summary>
            /// <param name="change">What to change.</param>
            private void Keep(Action<ClearanceSettings> change)
            {
                var spec = Run();
                var check = Clearance;

                if (spec == null || check == null) return;

                var settings = ClearanceSettings.From(spec);

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

            /// <summary>The floor side: what detection found, and what to drop from it.</summary>
            private UIElement Surfaces()
            {
                var spec = Run();
                var check = Clearance;

                return spec == null || check == null
                    ? NoRun(ClearanceSettings.Detected)
                    : ClearanceSurfaces.Page(check, Units, spec.Id, Keep);
            }

            private UIElement MeasurePage()
            {
                var spec = Run();

                if (spec == null) return NoRun("What to measure");

                var settings = ClearanceSettings.From(spec);
                var sets = Sets();
                var said = DetectedSaid(Units, settings.MinPatchAreaSquareMetres);

                var from = new List<string> { said, ClearanceSettings.Guessed, ClearanceSettings.Selected, ClearanceSettings.Loaded };
                from.AddRange(sets);

                var to = new List<string> { ClearanceSettings.NotTheSurfaces, ClearanceSettings.Selected, ClearanceSettings.Loaded };
                to.AddRange(sets);

                var chosen = settings.IsDetecting ? said : settings.Surfaces;

                if (!from.Contains(chosen, StringComparer.OrdinalIgnoreCase)) from.Add(chosen);
                if (!to.Contains(settings.Targets, StringComparer.OrdinalIgnoreCase)) to.Add(settings.Targets);

                return Ui.Stack(
                    Bench.Group("The run",
                        Ui.Field("Name", Ui.Text(spec.Name, name => Keep(_ => spec.Name = name), 220)),
                        Ui.Field("Folder", Ui.Text(spec.Folder ?? string.Empty,
                                                   folder => Keep(_ => spec.Folder =
                                                       string.IsNullOrWhiteSpace(folder) ? null : folder),
                                                   220),
                                 "how the board's rail files it")),
                    Bench.Group("What to measure",
                        Ui.Field("Clear height",
                                 Ui.Text(Units.Bare(settings.MinimumMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres > 0)
                                                 one.MinimumMetres = metres;
                                         }), 90),
                                 "the minimum, in " + Units.LengthUnit),
                        Ui.Field("Report under",
                                 Ui.Text(Units.Bare(settings.ToleranceMetres),
                                         text => Keep(one =>
                                         {
                                             if (Units.TryRead(text, out var metres) && metres >= 0)
                                                 one.ToleranceMetres = metres;
                                         }), 90),
                                 "how far under the minimum is worth reporting"),
                        Ui.Field("Measure up from",
                                 Reading(Ui.Choice(from, chosen,
                                           pick => Keep(one => one.Surfaces = pick == said ? ClearanceSettings.Detected : pick),
                                           420)),
                                 "the walking surface"),
                        Ui.Field("Measure up to",
                                 Reading(Ui.Choice(to, settings.Targets,
                                                   pick => Keep(one => one.Targets = pick), 210)),
                                 "what might be too low over it"),
                        Ui.Small("A 2.100 m corridor measured at 2.098 m is not a coordination "
                                 + "issue and the same corridor at 1.850 m is. The clear height is "
                                 + "what the design requires; \"report under\" is how far below it "
                                 + "earns a row. Leave it at nothing and every breach is reported."),
                        Ui.Small("Apart from the first two, both lists are this model's own search "
                                 + "sets and selection sets. \"" + ClearanceSettings.Guessed + "\" reads names: it "
                                 + "takes anything whose category or type contains "
                                 + string.Join(", ", WalkingWords)
                                 + ". Name a set instead and nothing is guessed at all."),
                        Blocked != null
                            ? Ui.Note("Both lists read the document, so they are fixed until a "
                                      + "model is open. The name, the folder and the two heights "
                                      + "are the run's own and can be set now.")
                            : sets.Count == 0
                                ? Ui.Note("This document holds no saved sets yet. Build one in Set "
                                          + "Builder and it appears in both lists.")
                                : null));
            }

            private UIElement ScopePage()
            {
                var spec = Run();

                if (spec == null) return NoRun("Scope and reductions");

                var settings = ClearanceSettings.From(spec);
                var storeys = Storeys();

                return Ui.Stack(
                    Bench.Group("Scope",
                        Ui.Field("Levels",
                                 Ui.Choice(new[] { "Every level" }.Concat(storeys).ToList(),
                                           settings.Levels.Count == 0 ? "Every level" : settings.Levels[0],
                                           pick => Keep(one =>
                                           {
                                               one.Levels.Clear();

                                               if (!string.Equals(pick, "Every level", StringComparison.Ordinal))
                                                   one.Levels.Add(pick);
                                           }), 220),
                                 "the storeys this project declares"),
                        Ui.Field("Zone", Reading(Pickers.Set(settings.Zone,
                                                             pick => Keep(one => one.Zone = pick), 220)),
                                 "a saved set the run is confined to"),
                        Ui.Small("Without a scope the first federation run returns every soffit in "
                                 + "the building. A level narrows what is reported; a zone narrows "
                                 + "what is measured. One level per run: the run record holds a "
                                 + "list, and this screen writes one of them."),
                        storeys.Count == 0
                            ? Ui.Note("This project declares no storeys yet. Levels & Zones works "
                                      + "them out from the geometry and they appear here.")
                            : null),
                    Bench.Group("Left out of what is over the surface",
                        Ui.Field("Category",
                                 Reading(Pickers.Category(settings.Ignore.FirstOrDefault(),
                                                          pick => Keep(one =>
                                                          {
                                                              one.Ignore.Clear();

                                                              if (!string.IsNullOrWhiteSpace(pick))
                                                                  one.Ignore.Add(pick);
                                                          }), 220))),
                        Ui.Small("Every sprinkler head, light fitting and smoke detector is under "
                                 + "the minimum somewhere. Naming the category here is the "
                                 + "difference between a board of coordination issues and four "
                                 + "thousand rows to delete."),
                        Ui.Small("The name is matched anywhere in the category, so \"Duct\" also "
                                 + "leaves out \"Duct Fittings\" and \"Duct Insulation\". One "
                                 + "category per run: the run record holds a list, and this screen "
                                 + "writes one of them.")),
                    Bench.Group("How much to report",
                        Ui.Check("One row per surface — its worst obstruction only",
                                 settings.WorstPerPatch,
                                 on => Keep(one => one.WorstPerPatch = on)),
                        Ui.Small("A slab under a plant room has a thousand things above it and one "
                                 + "of them is the lowest. Turn this off when the answer wanted is "
                                 + "every duct rather than the one that has to move.")));
            }

            /// <summary>
            /// A picker whose contents come off the document, greyed with the reason when there is
            /// no document to read. Its list is empty in that state, and an empty list somebody can
            /// still open says nothing about why.
            /// </summary>
            /// <param name="picker">The picker.</param>
            private ComboBox Reading(ComboBox picker)
            {
                var blocked = Blocked;

                if (blocked == null) return picker;

                picker.IsEnabled = false;
                picker.ToolTip = blocked;

                return picker;
            }

            /// <summary>Every saved set this document holds, named once.</summary>
            private List<string> Sets() =>
                Named(() => _seat.Sets().Select(set => set.Name)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            /// <summary>The storeys this project declares, for the level scope.</summary>
            private List<string> Storeys() =>
                Named(() => LevelPlan
                    .FromJson(_seat.Store.Section(ProjectStore.ProfileSection)[LevelPlan.Key])
                    .Storeys.Select(one => one.Name));

            /// <summary>
            /// Names off the document or the project file, or nothing when it will not answer: a
            /// picker that cannot be filled is a picker with nothing in it, never a screen that
            /// refuses to open.
            /// </summary>
            /// <param name="supply">Where the names come from.</param>
            private static List<string> Named(Func<IEnumerable<string?>> supply)
            {
                try
                {
                    return supply().Where(name => !string.IsNullOrWhiteSpace(name))
                                   .Select(name => name!)
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
