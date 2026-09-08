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
        internal static string DetectSaid => "Walkable surfaces only";

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
            "A walkable surface is any up-facing surface over "
            + units.Area(minimumAreaSquareMetres) + " tilted no more than "
            + Whole(maxSlopeDegrees) + "\u00b0. That includes desk tops and duct tops, so the "
            + "list is worth reviewing.";

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
                                name => Keep(_ => _run.Name = name), Ui.Wide.Name)), 200),
                            Sized(Ui.Field("Folder", Ui.Settled(_run.Folder ?? string.Empty,
                                string.Empty,
                                folder => Keep(_ => _run.Folder =
                                    string.IsNullOrWhiteSpace(folder) ? null : folder),
                                160)), 170))),

                    // The two sides, then what narrows them, then the two numbers. Five fields,
                    // which is the whole of what a clearance test is.
                    Bench.Group("What is checked",
                        Sized(Ui.Field("Search for walkable surfaces in",
                                 Source(from, settings.Surfaces,
                                     "Where to look for walkable surfaces",
                                     pick => Keep(one => one.Surfaces = pick), 380),
                                 null, Need.Required),
                              380),
                        Sized(Ui.Field("Search against",
                                 Source(to, settings.Targets, "What the height is measured to",
                                        pick => Keep(one => one.Targets = pick), 380),
                                 null, Need.Required),
                              380),
                        Chosen("Levels", settings.Levels, storeys, EveryLevel,
                               "Reports only the surfaces on the storeys named.",
                               (one, add) => one.Levels.Add(add),
                               (one, drop) => one.Levels.Remove(drop))),

                    Bench.Group("How much room",
                        Ui.Across(
                            Sized(Number("Height", settings.MinimumMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.MinimumMetres = metres,
                                         Need.Required), 190),
                            Sized(Number("Tolerance", settings.ToleranceMetres,
                                         metres => metres >= 0,
                                         (one, metres) => one.ToleranceMetres = metres,
                                         Need.Optional), 190)),
                        settings.ToleranceMetres > 0
                            ? null
                            : Ui.Note("Every shortfall is reported, however small.")),

                    Notices(blocked, storeys.Count, sets.Count),
                    Ui.Link("More about how this is measured", () => More(settings)));
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
                var stack = Ui.Stack(Ui.Field(label, Reading(picker), null, Need.Optional));

                if (chosen.Count > 0)
                    stack.Children.Add(Ui.Across(chosen
                        .Select(one => (UIElement)Parts.FilterChip(string.Empty, one,
                                                                   () => Keep(two => drop(two, one))))
                        .ToArray()));

                stack.Children.Add(Ui.Small(hint));

                return Sized(stack, 380);
            }

            /// <summary>One source, picked from the shared list.</summary>
            /// <param name="offered">Everything that could be read from.</param>
            /// <param name="chosen">What it is read from now.</param>
            /// <param name="prompt">What the list asks.</param>
            /// <param name="onPicked">Records the choice.</param>
            /// <param name="width">How wide the press is.</param>
            private UIElement Source(IReadOnlyList<string> offered, string chosen, string prompt,
                                     Action<string> onPicked, double width) =>
                Parts.Source(_log, offered, chosen, prompt, onPicked, width, Blocked);

            /// <summary>What the level picker reads when no level narrows the run.</summary>
            private const string EveryLevel = "Every level";

            /// <summary>What the category picker reads when nothing is left out.</summary>
            private const string NoCategories = "Everything is reported";

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
                    said.Add("This project declares no storeys yet. Settings works them out "
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
            private void More(ClearanceSettings settings) =>
                Ui.Configure("How this is measured", () => Ui.Safely(_onChanged), new Ui.Page(
                    "How this is measured",
                    () => Ui.Stack(
                        Bench.Group("What is left out",
                            Chosen("Categories to leave out", settings.Ignore, Categories(),
                                   NoCategories,
                                   "Matched anywhere in the category name, so \u201cDuct\u201d also "
                                   + "leaves out \u201cDuct Fittings\u201d.",
                                   (one, add) => one.Ignore.Add(add),
                                   (one, drop) => one.Ignore.Remove(drop)),
                            Ui.Check("One row per surface, its worst obstruction only",
                                     settings.WorstPerPatch,
                                     on => Keep(one => one.WorstPerPatch = on))),
                        Bench.Group("What counts as walkable",
                            Ui.Small(DetectHint(Units, settings.MinPatchAreaSquareMetres,
                                                settings.MaxSlopeDegrees)),
                            Blocked != null ? null : Ui.Link(
                                "Review what was found, and how it looks",
                                () => Ui.Configure("Walkable surfaces",
                                         () => Ui.Safely(_onChanged),
                                         new Ui.Page("Walkable surfaces",
                                             () => ClearanceSurfaces.Page(_check, Units, _run.Id,
                                                                          Keep))))),
                        Bench.Group("The two heights",
                            Ui.Small("The height is what the design requires. The tolerance is "
                                     + "how far below it a surface has to fall before it is "
                                     + "reported, so a slab 3 mm out of level is not a hundred "
                                     + "results.")))));

            /// <summary>A short number field, with its unit in the label.</summary>
            /// <param name="label">What the number is, without its unit.</param>
            /// <param name="value">What it is now, in metres.</param>
            /// <param name="allows">Whether a value read off the box may be written.</param>
            /// <param name="write">Puts an accepted value onto the settings.</param>
            /// <param name="need">What the field asks of the person filling it in.</param>
            private Panel Number(string label, double value, Func<double, bool> allows,
                                 Action<ClearanceSettings, double> write,
                                 Need need = Need.Unsaid) =>
                Ui.Field(label + " (" + Units.LengthUnit + ")",
                         Ui.Settled(Units.Bare(value), string.Empty, text => Keep(one =>
                         {
                             if (Units.TryRead(text, out var metres) && allows(metres))
                                 write(one, metres);
                         }), 90),
                         null, need);

            /// <summary>A field capped to a readable width, so short fields sit two to a row.</summary>
            /// <param name="field">The field.</param>
            /// <param name="width">How wide it may be.</param>
            private static UIElement Sized(FrameworkElement field, double width)
            {
                field.Width = width;

                // Left, not the default stretch. A stretched element given a fixed width is
                // centred in what is left over, so a chip row started under the picker with an
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
            private IReadOnlyList<string> Sets() => _seat.SetNames();

            /// <summary>
            /// The categories this document's elements carry, for the ignore list.
            ///
            /// The three the run leaves out by default are always offered, whether or not the
            /// bounded scan reached one: a list that cannot show what a run is set to is a list
            /// that cannot take it back off.
            /// </summary>
            private IReadOnlyList<string> Categories() =>
                Ui.Listed(Seated.Offer(() => Pickers.CategoryNames().ToList())
                                .Concat(ClearanceSettings.Unreported));

            /// <summary>The storeys this project declares, for the level scope.</summary>
            private IReadOnlyList<string> Storeys() =>
                Seated.Offer(() => LevelPlan
                    .FromJson(_seat.Store.Section(ProjectStore.ProfileSection)[LevelPlan.Key])
                    .Storeys.Select(one => one.Name).ToList());

        }
    }
}
