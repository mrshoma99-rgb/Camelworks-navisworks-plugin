using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Data;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The settings panel for one fall-hazard test, drawn beside the list on the Create tab.
    ///
    /// Fall hazard had a tab of its own, holding a list of tests, a footer and these fields in two
    /// windows off an action row. The list and the footer are the Create tab's; the fields are here
    /// and are laid out to fit the panel without scrolling, which is what they did not do when each
    /// of eleven of them held a whole row and a line of grey explanation.
    ///
    /// A fall-hazard test is a <see cref="CheckRunSpec"/> in the project file, so every field is
    /// editable with no document open. The three side pickers read the document's sets and are
    /// disabled with the reason when there is none.
    ///
    /// Results are not here. An opening to cover and an edge to rail are places to argue about,
    /// assign and close, so they are worked on the Manage board with everything else.
    /// </summary>
    public static class FallHazardViews
    {
        /// <summary>
        /// One fall-hazard test's settings.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="check">The fall-hazard check that holds the test.</param>
        /// <param name="run">The test being set up. It must already be saved.</param>
        /// <param name="onChanged">Called after each edit, so the list beside this redraws.</param>
        public static UIElement Settings(ISeat seat, FallCheck check, CheckRunSpec run,
                                         Action onChanged)
        {
            if (check == null) throw new ArgumentNullException(nameof(check));
            if (run == null) throw new ArgumentNullException(nameof(run));

            return new Fields(seat, check, run, onChanged).Build();
        }

        // -----------------------------------------------------------------------------------

        private sealed class Fields
        {
            private readonly ISeat _seat;
            private readonly FallCheck _check;
            private readonly CheckRunSpec _run;
            private readonly Action _onChanged;

            private readonly StackPanel _log = Ui.Stack();

            /// <summary>
            /// What the three source pickers hold, built once per panel rather than per draw.
            ///
            /// The picker edits its value in place and redraws itself; rebuilding it from the saved
            /// run on every keystroke would take the Find box away from whoever is typing in it.
            /// </summary>
            private readonly SourcePicker.Pick _floors;

            private readonly SourcePicker.Pick _handrails;

            private readonly SourcePicker.Pick _obstructions;

            internal Fields(ISeat seat, FallCheck check, CheckRunSpec run, Action onChanged)
            {
                _seat = seat;
                _check = check;
                _run = run;
                _onChanged = onChanged;

                var settings = FallSettings.From(run);

                _floors = SourcePicker.Read(settings.Floors);
                _handrails = SourcePicker.Read(settings.Handrails);
                _obstructions = SourcePicker.Read(settings.Obstructions);
            }

            private Measure Units => _seat.Profile.Units;

            /// <summary>Why the document cannot be read here, or null when it can.</summary>
            private string? Blocked =>
                _check.IsAvailable ? null : _check.Unavailable ?? "This seat cannot offer it.";

            internal UIElement Build()
            {
                var settings = FallSettings.From(_run);
                var sets = Sets();
                var blocked = Blocked;
                var storeys = Storeys();

                return Ui.Stack(
                    _log,
                    // No heading over the name and the folder: the tab is called Create and the
                    // list beside this has a Test column.
                    Bench.Group(string.Empty,
                        Ui.Evenly(
                            Filled(Ui.Field("Name", Ui.Settled(_run.Name, string.Empty,
                                name => Keep(_ => _run.Name = name), Ui.Wide.Name))),
                            Filled(Ui.Field("Folder", Ui.Folder(_run.Folder,
                                Seated.Folders(_check),
                                folder => Keep(_ => _run.Folder =
                                    string.IsNullOrWhiteSpace(folder) ? null : folder),
                                160))))),
                    // Where the plan is cut comes first. Every other field on the panel is
                    // conditioned on it: the floor side is filtered by the band around it, the
                    // rails and obstructions are read at it, and there is no plan without it. It
                    // used to be asked fourth, under three set pickers, as a number with no
                    // default that could be right.
                    Bench.Group("Where the plan is cut",
                        Storey(settings, storeys),
                        Ui.Evenly(
                            Filled(Number("Cut elevation", settings.HasLevel ? settings.LevelMetres
                                                                            : (double?)null,
                                         metres => true,
                                         (one, metres) => one.LevelMetres = metres)),
                            Filled(Ui.Field("Band above and below (" + Units.LengthUnit + ")",
                                Ui.Settled(Units.Bare(settings.BandMetres), string.Empty,
                                    text => Keep(one =>
                                    {
                                        if (Units.TryRead(text, out var metres) && metres > 0)
                                            one.BandMetres = metres;
                                    }), 90)))),
                        settings.HasLevel
                            ? null
                            : Ui.Note("This test does not say where to cut. Pick the storey, or "
                                      + "type the elevation the plan is cut at.")),
                    Bench.Group("What to read",
                        Filled(SourcePicker.Drawn(_seat, "Floor from", _floors,
                                  () => Keep(one => one.Floors = SourcePicker.Written(_floors)),
                                  blocked,
                                  new[] { SourcePicker.Everywhere })),
                        Filled(Ui.Small("A set of floors or slabs. Everything loaded reads the "
                                       + "whole federation at this elevation, and a wall or a duct "
                                       + "through the band projects its outline as though it were "
                                       + "floor.")),
                        settings.HasFloors
                            ? null
                            : Ui.Note("This test names nothing to read the floor from, so it "
                                      + "cannot run."),
                        Filled(SourcePicker.Drawn(_seat, "Handrails from", _handrails,
                                  () => Keep(one => one.Handrails = SourcePicker.Written(_handrails)),
                                  blocked,
                                  new[] { SourcePicker.Everywhere })),
                        Filled(SourcePicker.Drawn(_seat, "Obstructions from", _obstructions,
                                  () => Keep(one =>
                                      one.Obstructions = SourcePicker.Written(_obstructions)),
                                  blocked,
                                  new[] { SourcePicker.Everywhere })),
                        settings.HasHandrails
                            ? null
                            : Ui.Note("With no handrails named, every edge over the gap is "
                                      + "reported as unprotected.")),
                    Bench.Group("How to judge it",
                        Ui.Evenly(
                            Filled(Number("Maximum unprotected gap", settings.LimitMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.LimitMetres = metres)),
                            Filled(Number("Ignore gaps under", settings.MinPassageMetres,
                                         metres => metres >= 0,
                                         (one, metres) => one.MinPassageMetres = metres))),
                        Filled(Number("Grid resolution", settings.CellMetres,
                                     metres => metres > 0,
                                     (one, metres) => one.CellMetres = metres)),
                        Coarse(settings), Open(settings),
                        // Handrail coverage distance is behind the link with the sentence that
                        // says what it is worth: matching is proximity only, so a rail 300 mm
                        // across the void protects an edge as well as one on it. Tuning an
                        // approximation on the first screen invites treating it as a real number.
                        Ui.Link("Handrail coverage distance and how the plan is drawn", Explain)),
                    Notices(blocked, sets.Count, storeys.Count));
            }

            /// <summary>
            /// One notice region for what this panel's surroundings are missing.
            ///
            /// The seat's own reason is not repeated here. It is on the footer beside the greyed
            /// Run, and on each control it disables.
            /// </summary>
            /// <param name="blocked">Why the document cannot be read, or null.</param>
            /// <param name="sets">How many saved sets the document holds.</param>
            /// <param name="storeys">How many storeys the project declares.</param>
            private static UIElement? Notices(string? blocked, int sets, int storeys)
            {
                if (blocked != null) return null;

                var said = new List<string>();

                if (storeys == 0)
                    said.Add("This project declares no storeys yet. Levels & Zones works them out "
                             + "from the geometry and they appear in Storey.");

                if (sets == 0)
                    said.Add("This document holds no saved sets yet. Build one in Set Builder and "
                             + "it appears in all three lists.");

                return said.Count == 0 ? null : Ui.Note(string.Join(Environment.NewLine, said));
            }

            /// <summary>What the storey picker reads when the elevation was typed rather than picked.</summary>
            private const string Typed = "A typed elevation";

            /// <summary>
            /// The storey the plan is cut at.
            ///
            /// The project already knows every storey and its height, and the clearance panel next
            /// to this one has offered them since it was written. Fall hazard asked for the number:
            /// eleven storeys of a frame was eleven lookups in another tab.
            /// </summary>
            /// <param name="settings">The test's settings as they stand.</param>
            /// <param name="storeys">The storeys this project declares, with their heights.</param>
            private UIElement Storey(FallSettings settings, IReadOnlyList<LevelStorey> storeys)
            {
                var named = storeys.Select(one => one.Name).ToList();

                // Whatever the test says, kept in the list. A test written against a project whose
                // storeys have since been renamed named one this list may not hold, and dropping it
                // showed "A typed elevation" for a test that had a storey picked.
                if (!string.IsNullOrWhiteSpace(settings.LevelName)
                    && !named.Contains(settings.LevelName!, StringComparer.OrdinalIgnoreCase))
                    named.Add(settings.LevelName!);

                var chosen = string.IsNullOrWhiteSpace(settings.LevelName)
                    ? Typed : settings.LevelName!;

                var picker = Ui.Choice(new[] { Typed }.Concat(named).ToList(), chosen, pick =>
                {
                    if (string.Equals(pick, Typed, StringComparison.Ordinal))
                    {
                        Keep(one => one.LevelName = null);

                        return;
                    }

                    var storey = storeys.FirstOrDefault(
                        one => string.Equals(one.Name, pick, StringComparison.OrdinalIgnoreCase));

                    if (storey == null) return;

                    Keep(one =>
                    {
                        one.LevelName = storey.Name;
                        one.LevelMetres = storey.Elevation;
                    });
                }, 230);

                if (named.Count == 0)
                {
                    picker.IsEnabled = false;
                    picker.ToolTip = "This project declares no storeys yet.";
                }

                return Filled(Ui.Field("Storey", picker,
                                      "Fills the elevation from the project's storeys. The "
                                      + "elevation stays the authority: editing a storey's height "
                                      + "does not move a test that has already been run."));
            }

            /// <summary>The storeys this project declares, with their heights.</summary>
            private List<LevelStorey> Storeys()
            {
                try
                {
                    return LevelPlan
                        .FromJson(_seat.Store.Section(ProjectStore.ProfileSection)[LevelPlan.Key])
                        .Storeys.Where(one => !string.IsNullOrWhiteSpace(one.Name)).ToList();
                }
                catch (Exception)
                {
                    return new List<LevelStorey>();
                }
            }

            /// <summary>
            /// The grid warning, shown only when the grid cannot resolve the gap being judged.
            ///
            /// A cell is judged at its centre, so a grid coarser than half the gap decides some
            /// cells by rounding. Said as validation of a value rather than as standing text under
            /// the field, because at a sensible grid there is nothing to say.
            /// </summary>
            /// <param name="settings">The test's settings.</param>
            private UIElement? Coarse(FallSettings settings) =>
                settings.CellMetres * 2 <= settings.LimitMetres
                    ? null
                    : Ui.Note("A " + Units.Length(settings.CellMetres) + " grid cannot tell a "
                              + Units.Length(settings.LimitMetres) + " gap from solid floor. Set "
                              + "the grid to a few times under the gap.");

            /// <summary>The other risky value: nothing is too short to report.</summary>
            /// <param name="settings">The test's settings.</param>
            private static UIElement? Open(FallSettings settings) =>
                settings.MinPassageMetres > 0
                    ? null
                    : Ui.Note("Every gap in the protection is reported, however narrow.");

            /// <summary>
            /// The reference matter, behind one link rather than standing under the fields.
            ///
            /// Every sentence here answers "why would I change this", which is a question asked
            /// once. Standing text answers it on every visit, and it was 1,340 px of it.
            /// </summary>
            private void Explain() =>
                Ui.Configure("How the plan is drawn", () => Ui.Safely(_onChanged), new Ui.Page(
                    "How the plan is drawn",
                    () => Ui.Stack(
                        Bench.Group("The slice",
                            Ui.Small("An element whose box reaches the slice contributes its whole "
                                     + "outline in plan, so the thickness does not have to match a "
                                     + "slab. It is also why a column or a stair through the slice "
                                     + "reads as solid floor rather than as a hole, which is the "
                                     + "largest thing this check gets wrong."),
                            Ui.Small("The band is a selection band, not a slice thickness: it "
                                     + "decides which elements are read, and each of them then "
                                     + "contributes its whole outline whatever its depth.")),
                        Bench.Group("The grid",
                            Ui.Small("The plan is drawn cell by cell and a cell is judged at its "
                                     + "centre, so anything thinner than one cell is decided by "
                                     + "rounding."),
                            Ui.Small("Halving the grid doubles the plan in each direction, so it "
                                     + "costs four times the work. Over six million cells the run "
                                     + "stops and says so rather than taking the session with "
                                     + "it.")),
                        Bench.Group("The two protection figures",
                            Ui.Small("Handrail coverage distance is how close a rail has to be to "
                                     + "count as protecting an edge. Matching is proximity only "
                                     + "and rails are not height-filtered, so a rail 300 mm across "
                                     + "the void protects an edge as well as one standing on it. "
                                     + "It is a tolerance on an approximation, not a design "
                                     + "figure."),
                            Coverage(),
                            Ui.Small("\"Ignore gaps under\" is the width of a person, not of the "
                                     + "void: a 300 mm gap between two handrails is too narrow to "
                                     + "fall through.")))));

            /// <summary>A short number field, with its unit in the label.</summary>
            /// <param name="label">What the number is, without its unit.</param>
            /// <param name="value">What it is now, in metres.</param>
            /// <param name="allows">Whether a value read off the box may be written.</param>
            /// <param name="write">Puts an accepted value onto the settings.</param>
            private Panel Number(string label, double? value, Func<double, bool> allows,
                                 Action<FallSettings, double> write) =>
                Ui.Field(label + " (" + Units.LengthUnit + ")",
                         // Empty, not "0", when nothing has set it. A zero in the box is a value
                         // somebody chose, and the elevation is the one field here with no safe
                         // default: on a site-coordinate model zero reads nothing at all.
                         Ui.Settled(value == null ? string.Empty : Units.Bare(value.Value),
                                    string.Empty, text => Keep(one =>
                         {
                             if (Units.TryRead(text, out var metres) && allows(metres))
                                 write(one, metres);
                         }), 90));

            /// <summary>Handrail coverage distance, on the page that says what it is worth.</summary>
            private UIElement Coverage() =>
                Filled(Number("Handrail coverage distance",
                             FallSettings.From(_run).HandrailToleranceMetres,
                             metres => metres >= 0,
                             (one, metres) => one.HandrailToleranceMetres = metres));

            /// <summary>
            /// A field that takes the width of the panel it is in.
            ///
            /// It carried a ceiling before. A cap plus the default stretch centres an element in
            /// what is left over, which is why the panel's fields started at three different left
            /// edges; a cap alone leaves the panel's right half white at every window width. This
            /// panel is already the narrower half of the tab, so its fields take all of it and a
            /// pair of them splits it through <see cref="Ui.Evenly"/>.
            /// </summary>
            /// <param name="field">The field.</param>
            private static UIElement Filled(FrameworkElement field)
            {
                field.HorizontalAlignment = HorizontalAlignment.Stretch;

                return field;
            }

            /// <summary>Write the settings back onto the test and into the project file.</summary>
            /// <param name="change">What to change.</param>
            private void Keep(Action<FallSettings> change)
            {
                var settings = FallSettings.From(_run);

                change(settings);
                settings.Write(_run);

                var problem = _check.Put(_run);

                if (problem != null) _log.Children.Add(Ui.Problem(problem));

                _onChanged();
            }

            /// <summary>
            /// Every saved set this document holds, named once, or nothing when it will not
            /// answer: a picker that cannot be filled is a picker with nothing in it, never a
            /// screen that refuses to open.
            /// </summary>
            private IReadOnlyList<string> Sets() => _seat.SetNames();
        }
    }
}
