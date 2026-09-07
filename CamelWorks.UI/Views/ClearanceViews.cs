using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The settings panel for one clearance test, drawn beside the list on the Create tab.
    ///
    /// Clearance had a tab of its own, holding a list of tests, a footer and these fields. The list
    /// and the footer are the Create tab's, which lists every kind at once; what is left here is
    /// the fields, which are the only part of the screen that is clearance's own.
    ///
    /// A clearance test is a <see cref="CheckRunSpec"/> in the project file, so every field is
    /// editable with no document open. What needs a document — the pickers fed by the document's
    /// sets, and surface detection — is disabled and says why.
    ///
    /// Results are not here. A clearance breach is a place to argue about, assign and close, so it
    /// is worked on the Manage board with the same grouping, sorting, filtering, exchange, renaming
    /// and export as a clash.
    /// </summary>
    public static class ClearanceViews
    {
        static ClearanceViews() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Forget what was detected. Surfaces detected in the federation that was open are not
        /// surfaces in the one that is. The tests themselves are in the project file and survive.
        /// </summary>
        internal static void Forget() => ClearanceSurfaces.Forget();

        /// <summary>What the detection tick says, caveat included.</summary>
        internal static string DetectSaid => "Find the walkable surfaces in it";

        /// <summary>
        /// What detection will keep, said in the seat's units.
        ///
        /// The caveat belongs beside the tick. A control that says only "find the walkable
        /// surfaces" reads as though the software can tell a floor from a desk top.
        /// </summary>
        /// <param name="units">The seat's units, so the area reads in them.</param>
        /// <param name="minimumAreaSquareMetres">The smallest patch detection will report.</param>
        /// <param name="maxSlopeDegrees">How far a surface may tilt and still be walked on.</param>
        internal static string DetectHint(Measure units, double minimumAreaSquareMetres,
                                          double maxSlopeDegrees) =>
            "Every up-facing surface over " + units.Area(minimumAreaSquareMetres) + " tilted no more "
            + "than " + Whole(maxSlopeDegrees) + "°, which includes desk tops and duct tops. Off, "
            + "the source's elements are the surfaces.";

        /// <summary>A degree count without a trailing zero.</summary>
        /// <param name="degrees">The angle.</param>
        private static string Whole(double degrees) =>
            degrees.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);

        /// <summary>
        /// One clearance test's settings.
        /// </summary>
        /// <param name="seat">The seat this is running in.</param>
        /// <param name="check">The clearance check that holds the test.</param>
        /// <param name="run">The test being set up. It must already be saved.</param>
        /// <param name="onChanged">Called after each edit, so the list beside this redraws.</param>
        public static UIElement Settings(ISeat seat, ClearanceCheck check, CheckRunSpec run,
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
            private readonly ClearanceCheck _check;
            private readonly CheckRunSpec _run;
            private readonly Action _onChanged;

            private readonly StackPanel _log = Ui.Stack();

            internal Fields(ISeat seat, ClearanceCheck check, CheckRunSpec run, Action onChanged)
            {
                _seat = seat;
                _check = check;
                _run = run;
                _onChanged = onChanged;
            }

            private Measure Units => _seat.Profile.Units;

            /// <summary>Why the document cannot be read here, or null when it can.</summary>
            private string? Blocked =>
                _check.IsAvailable ? null : _check.Unavailable ?? "This seat cannot offer it.";

            internal UIElement Build()
            {
                var settings = ClearanceSettings.From(_run);
                var sets = Sets();
                var blocked = Blocked;
                var storeys = Storeys();

                // A source, never a mode: detection refines what is named here. The three
                // whole-model answers plus the document's own sets, and nothing that guesses.
                var from = new List<string> { ClearanceSettings.Loaded, ClearanceSettings.Selected };

                from.AddRange(sets);

                var to = new List<string> { ClearanceSettings.NotTheSurfaces,
                                            ClearanceSettings.Selected, ClearanceSettings.Loaded };

                to.AddRange(sets);

                if (!from.Contains(settings.Surfaces, StringComparer.OrdinalIgnoreCase))
                    from.Add(settings.Surfaces);

                if (!to.Contains(settings.Targets, StringComparer.OrdinalIgnoreCase))
                    to.Add(settings.Targets);

                return Ui.Stack(
                    _log,
                    // No heading over the name and the folder: the tab is called Create and the
                    // list beside this has a Test column.
                    Bench.Group(string.Empty,
                        Ui.Across(
                            Sized(Ui.Field("Name", Ui.Settled(_run.Name, string.Empty,
                                name => Keep(_ => _run.Name = name), 190)), 200),
                            Sized(Ui.Field("Folder", Ui.Settled(_run.Folder ?? string.Empty,
                                string.Empty,
                                folder => Keep(_ => _run.Folder =
                                    string.IsNullOrWhiteSpace(folder) ? null : folder),
                                160)), 170))),
                    // Scope first. It is the decision that makes the answer usable, and the panel
                    // used to ask it third, under two numbers and two sides.
                    Bench.Group("Where",
                        Sized(Ui.Field("Zone",
                                 Reading(Ui.Choice(new[] { WholeModel }.Concat(sets).ToList(),
                                     string.IsNullOrWhiteSpace(settings.Zone)
                                         ? WholeModel : settings.Zone!,
                                     pick => Keep(one => one.Zone =
                                         string.Equals(pick, WholeModel, StringComparison.Ordinal)
                                             ? null : pick), 230)),
                                 "Narrows what is measured. Nothing named measures the whole "
                                 + "federation."),
                              380),
                        Chosen("Levels", settings.Levels, storeys, EveryLevel,
                               "Narrows what is reported, by the storey a surface stands on.",
                               (one, add) => one.Levels.Add(add),
                               (one, drop) => one.Levels.Remove(drop))),
                    Bench.Group("What to measure",
                        Sized(Ui.Field("Measure up from",
                                 Reading(Ui.Choice(from, settings.Surfaces,
                                     pick => Keep(one => one.Surfaces = pick), 380))),
                              380),
                        Detecting(settings),
                        Sized(Ui.Field("Measure up to",
                                 Reading(Ui.Choice(to, settings.Targets,
                                                   pick => Keep(one => one.Targets = pick), 230))),
                              380),
                        // With the side it modifies. What counts as an obstruction is one decision,
                        // and it used to be split across two groups with the scope between them.
                        Chosen("Categories not worth reporting", settings.Ignore, Categories(),
                               NoCategories,
                               "Matched anywhere in the category name, so \"Duct\" also leaves out "
                               + "\"Duct Fittings\".",
                               (one, add) => one.Ignore.Add(add),
                               (one, drop) => one.Ignore.Remove(drop))),
                    Bench.Group("How much clearance",
                        Ui.Across(
                            Sized(Number("Minimum clear height", settings.MinimumMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.MinimumMetres = metres), 190),
                            Sized(Number("Ignore shortfalls under", settings.ToleranceMetres,
                                         metres => metres >= 0,
                                         (one, metres) => one.ToleranceMetres = metres), 190)),
                        settings.ToleranceMetres > 0
                            ? null
                            : Ui.Note("Every shortfall is reported, however small.")),
                    Bench.Group("What to report",
                        Ui.Check("One row per surface — its worst obstruction only",
                                 settings.WorstPerPatch,
                                 on => Keep(one => one.WorstPerPatch = on))),
                    Notices(blocked, storeys.Count, sets.Count),
                    Ui.Link("How clearance is measured", Explain));
            }

            /// <summary>
            /// The detection tick and, when it is on, the way into what it found.
            ///
            /// A tick over the source rather than an entry in the source list. Detection refines
            /// what the source names; as a list entry it replaced it, so a run scoped to a floors
            /// set swept the whole federation and threw the set away.
            /// </summary>
            /// <param name="settings">The run's settings as they stand.</param>
            private UIElement Detecting(ClearanceSettings settings)
            {
                var blocked = Blocked;

                var tick = Ui.Check(DetectSaid, settings.DetectSurfaces,
                                    on => Keep(one => one.DetectSurfaces = on));

                if (blocked != null)
                {
                    tick.IsEnabled = false;
                    tick.ToolTip = blocked;
                }

                if (!settings.DetectSurfaces)
                    return Ui.Stack(tick, Ui.Small(DetectHint(Units, settings.MinPatchAreaSquareMetres,
                                                              settings.MaxSlopeDegrees)));

                var link = Ui.Link("Review what detection found, and how it looks", () =>
                    Ui.Configure("Walkable surfaces", () => Ui.Safely(_onChanged),
                                 new Ui.Page("Walkable surfaces",
                                             () => ClearanceSurfaces.Page(_check, Units, _run.Id, Keep))));

                link.IsEnabled = blocked == null;

                if (blocked != null) link.ToolTip = blocked;

                return Ui.Stack(tick,
                                Ui.Small(DetectHint(Units, settings.MinPatchAreaSquareMetres,
                                                    settings.MaxSlopeDegrees)),
                                link);
            }

            /// <summary>
            /// A list-valued setting: what is chosen, as chips that come off, and one picker to add.
            ///
            /// The record has held lists here since it was written and the panel wrote one of them,
            /// so a 2.4 m minimum across levels 2 to 4 excluding three categories was three tests.
            /// </summary>
            /// <param name="label">What the list is.</param>
            /// <param name="chosen">What is in it now.</param>
            /// <param name="offered">Everything that could go in it.</param>
            /// <param name="empty">What the picker reads when the list is empty.</param>
            /// <param name="hint">One line under the picker.</param>
            /// <param name="add">Puts one value into the settings.</param>
            /// <param name="drop">Takes one value out.</param>
            private UIElement Chosen(string label, IList<string> chosen, IReadOnlyList<string> offered,
                                     string empty, string hint,
                                     Action<ClearanceSettings, string> add,
                                     Action<ClearanceSettings, string> drop)
            {
                var left = offered.Where(one => !chosen.Contains(one, StringComparer.OrdinalIgnoreCase))
                                  .ToList();

                var picker = Ui.Choice(new[] { chosen.Count == 0 ? empty : Add }.Concat(left).ToList(),
                                       chosen.Count == 0 ? empty : Add,
                                       pick =>
                                       {
                                           if (string.Equals(pick, empty, StringComparison.Ordinal)
                                               || string.Equals(pick, Add, StringComparison.Ordinal))
                                               return;

                                           Keep(one => add(one, pick));
                                       }, 230);

                // Picker, then what is chosen, then the line that says what the list does. The chips
                // are the answer and belong next to the control that adds to it.
                var stack = Ui.Stack(Ui.Field(label, Reading(picker)));

                if (chosen.Count > 0)
                    stack.Children.Add(Ui.Across(chosen
                        .Select(one => (UIElement)Parts.FilterChip(string.Empty, one,
                                                                   () => Keep(two => drop(two, one))))
                        .ToArray()));

                stack.Children.Add(Ui.Small(hint));

                return Sized(stack, 380);
            }

            /// <summary>What the level picker reads when no level narrows the run.</summary>
            private const string EveryLevel = "Every level";

            /// <summary>What the category picker reads when nothing is left out.</summary>
            private const string NoCategories = "Everything is reported";

            /// <summary>What the zone picker reads when the run is not confined to a set.</summary>
            private const string WholeModel = "The whole model";

            /// <summary>What a list picker reads once it holds something.</summary>
            private const string Add = "Add another";

            /// <summary>
            /// One notice region for everything this panel's surroundings are missing.
            ///
            /// One banner per missing thing stacked: with no document open the panel drew a banner
            /// saying so, a second saying the project declares no storeys and a third saying the
            /// document holds no sets, all of which are the same fact said three times.
            ///
            /// The seat's own reason is not repeated here. It is on the footer beside the greyed
            /// Run, which is where the reader is when it matters, and on each control it disables.
            /// </summary>
            /// <param name="blocked">Why the document cannot be read, or null.</param>
            /// <param name="storeys">How many storeys the project declares.</param>
            /// <param name="sets">How many saved sets the document holds.</param>
            private static UIElement? Notices(string? blocked, int storeys, int sets)
            {
                if (blocked != null) return null;

                var said = new List<string>();

                if (storeys == 0)
                    said.Add("This project declares no storeys yet. Levels & Zones works them out "
                             + "from the geometry and they appear in Level.");

                if (sets == 0)
                    said.Add("This document holds no saved sets yet. Build one in Set Builder and "
                             + "it appears in the zone and the two surface lists.");

                return said.Count == 0 ? null : Ui.Note(string.Join(Environment.NewLine, said));
            }

            /// <summary>
            /// The reference matter, behind one link rather than standing under the fields.
            ///
            /// Every sentence here answers "why would I change this", which is a question asked
            /// once. Standing text answers it on every visit and pushes the fields off the panel.
            /// </summary>
            private void Explain() =>
                Ui.Configure("How clearance is measured", null, new Ui.Page(
                    "How clearance is measured",
                    () => Ui.Stack(
                        Bench.Group("The two heights",
                            Ui.Small("The minimum clear height is what the design requires. "
                                     + "\"Ignore shortfalls under\" is how far below it earns a "
                                     + "row: a 2.100 m corridor measured at 2.098 m is a survey "
                                     + "difference and the same corridor at 1.850 m is a "
                                     + "coordination issue.")),
                        Bench.Group("The two sides",
                            Ui.Small("Apart from the fixed entries, both lists are this model's "
                                     + "own search sets and selection sets."),
                            Ui.Small("Detection reads the model's triangles. It cannot tell a "
                                     + "floor from a desk top or a duct top, which is why what it "
                                     + "found can be reviewed and dropped. It refines the source "
                                     + "rather than replacing it: naming a floors set and leaving "
                                     + "the tick on sweeps that set, not the federation.")),
                        Bench.Group("Scope",
                            Ui.Small("Without a scope the first federation run returns every "
                                     + "soffit in the building. A zone narrows what is measured; "
                                     + "levels narrow what is reported. Both take more than one "
                                     + "value."),
                            Ui.Small("An ignored category is matched anywhere in the category "
                                     + "name, so \"Duct\" also leaves out \"Duct Fittings\" and "
                                     + "\"Duct Insulation\". Sprinklers, lighting and fire alarm "
                                     + "devices start in the list because every one of them is "
                                     + "under the minimum somewhere.")),
                        Bench.Group("What to report",
                            Ui.Small("A slab under a plant room has a thousand things above it "
                                     + "and one of them is the lowest. Turn the one-row setting "
                                     + "off when the answer wanted is every obstruction rather "
                                     + "than the one that has to move.")))));

            /// <summary>A short number field, with its unit in the label.</summary>
            /// <param name="label">What the number is, without its unit.</param>
            /// <param name="value">What it is now, in metres.</param>
            /// <param name="allows">Whether a value read off the box may be written.</param>
            /// <param name="write">Puts an accepted value onto the settings.</param>
            private Panel Number(string label, double value, Func<double, bool> allows,
                                 Action<ClearanceSettings, double> write) =>
                Ui.Field(label + " (" + Units.LengthUnit + ")",
                         Ui.Settled(Units.Bare(value), string.Empty, text => Keep(one =>
                         {
                             if (Units.TryRead(text, out var metres) && allows(metres))
                                 write(one, metres);
                         }), 90));

            /// <summary>A field capped to a readable width, so short fields sit two to a row.</summary>
            /// <param name="field">The field.</param>
            /// <param name="width">How wide it may be.</param>
            private static UIElement Sized(FrameworkElement field, double width)
            {
                field.Width = width;

                // Left, not the default stretch. A stretched element given a fixed width is
                // centred in what is left over, so Ignore category started under Zone with an
                // empty cell to its left.
                field.HorizontalAlignment = HorizontalAlignment.Left;

                return field;
            }

            /// <summary>Write the settings back onto the test and into the project file.</summary>
            /// <param name="change">What to change.</param>
            private void Keep(Action<ClearanceSettings> change)
            {
                var settings = ClearanceSettings.From(_run);

                change(settings);
                settings.Write(_run);

                var problem = _check.Put(_run);

                if (problem != null) _log.Children.Add(Ui.Problem(problem));

                _onChanged();
            }

            /// <summary>
            /// A picker whose contents come off the document, greyed with the reason when there is
            /// no document to read. Its list is empty in that state, and an empty list that can
            /// still be opened says nothing about why.
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

            /// <summary>
            /// The categories this document's elements carry, for the ignore list.
            ///
            /// The three the run leaves out by default are always offered, whether or not the
            /// bounded scan reached one: a list that cannot show what a run is set to is a list
            /// that cannot take it back off.
            /// </summary>
            private List<string> Categories()
            {
                var found = Named(() => Pickers.CategoryNames());

                foreach (var one in ClearanceSettings.Unreported)
                    if (!found.Contains(one, StringComparer.OrdinalIgnoreCase)) found.Add(one);

                found.Sort(StringComparer.OrdinalIgnoreCase);

                return found;
            }

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
