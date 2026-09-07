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
        /// <summary>The words the guess matches, on the element's category or type.</summary>
        internal static IReadOnlyList<string> WalkingWords => ClearanceCheck.WalkingWords;

        static ClearanceViews() => Host.WhenDocumentChanges(Forget);

        /// <summary>
        /// Forget what was detected. Surfaces detected in the federation that was open are not
        /// surfaces in the one that is. The tests themselves are in the project file and survive.
        /// </summary>
        internal static void Forget() => ClearanceSurfaces.Forget();

        /// <summary>
        /// The option's whole sentence, caveat included. An option that says only "detected
        /// walkable surfaces" reads as though the software found the floors.
        /// </summary>
        /// <param name="units">The seat's units, so the area reads in them.</param>
        /// <param name="minimumAreaSquareMetres">The smallest patch detection will report.</param>
        internal static string DetectedSaid(Measure units, double minimumAreaSquareMetres) =>
            ClearanceSettings.Detected + " — every up-facing surface over " + units.Area(minimumAreaSquareMetres)
            + ", which includes desk tops and duct tops.";

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
                var said = DetectedSaid(Units, settings.MinPatchAreaSquareMetres);
                var blocked = Blocked;

                var from = new List<string> { said, ClearanceSettings.Guessed,
                                              ClearanceSettings.Selected, ClearanceSettings.Loaded };

                from.AddRange(sets);

                var to = new List<string> { ClearanceSettings.NotTheSurfaces,
                                            ClearanceSettings.Selected, ClearanceSettings.Loaded };

                to.AddRange(sets);

                var chosen = settings.IsDetecting ? said : settings.Surfaces;

                if (!from.Contains(chosen, StringComparer.OrdinalIgnoreCase)) from.Add(chosen);
                if (!to.Contains(settings.Targets, StringComparer.OrdinalIgnoreCase)) to.Add(settings.Targets);

                var storeys = Storeys();
                var level = settings.Levels.Count == 0 ? EveryLevel : settings.Levels[0];

                return Ui.Stack(
                    _log,
                    blocked == null ? null : Ui.Note(blocked),
                    Bench.Group("Test",
                        Ui.Across(
                            Sized(Ui.Field("Name", Ui.Settled(_run.Name, string.Empty,
                                name => Keep(_ => _run.Name = name), 190)), 200),
                            Sized(Ui.Field("Folder", Ui.Settled(_run.Folder ?? string.Empty,
                                string.Empty,
                                folder => Keep(_ => _run.Folder =
                                    string.IsNullOrWhiteSpace(folder) ? null : folder),
                                160)), 170))),
                    Bench.Group("What to measure",
                        Ui.Across(
                            Sized(Number("Minimum clear height", settings.MinimumMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.MinimumMetres = metres), 190),
                            Sized(Number("Ignore shortfalls under", settings.ToleranceMetres,
                                         metres => metres >= 0,
                                         (one, metres) => one.ToleranceMetres = metres), 190)),
                        settings.ToleranceMetres > 0
                            ? null
                            : Ui.Note("Every shortfall is reported, however small."),
                        Ui.Field("Measure up from",
                                 Reading(Ui.Choice(from, chosen,
                                     pick => Keep(one => one.Surfaces =
                                         pick == said ? ClearanceSettings.Detected : pick), 380))),
                        settings.IsDetecting ? Detected() : null,
                        Ui.Field("Measure up to",
                                 Reading(Ui.Choice(to, settings.Targets,
                                                   pick => Keep(one => one.Targets = pick), 230)))),
                    Bench.Group("Scope",
                        Ui.Across(
                            Sized(Ui.Field("Level", Ui.Choice(
                                new[] { EveryLevel }.Concat(storeys).ToList(), level,
                                pick => Keep(one =>
                                {
                                    one.Levels.Clear();

                                    if (!string.Equals(pick, EveryLevel, StringComparison.Ordinal))
                                        one.Levels.Add(pick);
                                }), 180)), 190),
                            Sized(Ui.Field("Zone", Reading(Pickers.Set(settings.Zone,
                                pick => Keep(one => one.Zone = pick), 180))), 190)),
                        Sized(Ui.Field("Ignore category",
                                       Reading(Pickers.Category(settings.Ignore.FirstOrDefault(),
                                           pick => Keep(one =>
                                           {
                                               one.Ignore.Clear();

                                               if (!string.IsNullOrWhiteSpace(pick))
                                                   one.Ignore.Add(pick);
                                           }), 220))), 380),
                        storeys.Count == 0
                            ? Ui.Note("This project declares no storeys yet. Levels & Zones works "
                                      + "them out from the geometry and they appear here.")
                            : null),
                    Bench.Group("What to report",
                        Ui.Check("One row per surface — its worst obstruction only",
                                 settings.WorstPerPatch,
                                 on => Keep(one => one.WorstPerPatch = on))),
                    blocked == null && sets.Count == 0
                        ? Ui.Note("This document holds no saved sets yet. Build one in Set Builder "
                                  + "and it appears in the two lists above.")
                        : null,
                    Ui.Link("How clearance is measured", Explain));
            }

            /// <summary>What the level list calls no level filter at all.</summary>
            private const string EveryLevel = "Every level";

            /// <summary>
            /// The way into the detected surfaces, shown only while the test is detecting them.
            ///
            /// Detection reads the document's triangles, so with no model there is nothing to
            /// detect and the way in is greyed with the reason on it.
            /// </summary>
            private UIElement Detected()
            {
                var blocked = Blocked;

                var link = Ui.Link("Review the detected surfaces", () =>
                    Ui.Configure(ClearanceSettings.Detected, () => Ui.Safely(_onChanged),
                                 new Ui.Page(ClearanceSettings.Detected,
                                             () => ClearanceSurfaces.Page(_check, Units, _run.Id, Keep))));

                link.IsEnabled = blocked == null;

                if (blocked != null) link.ToolTip = blocked;

                return link;
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
                            Ui.Small("Apart from the first two entries, both lists are this "
                                     + "model's own search sets and selection sets. \""
                                     + ClearanceSettings.Guessed + "\" reads names: it takes "
                                     + "anything whose category or type contains "
                                     + string.Join(", ", WalkingWords)
                                     + ". Name a set instead and nothing is guessed."),
                            Ui.Small(ClearanceSettings.Detected + " reads the model's triangles "
                                     + "and cannot tell a floor from a desk top or a duct top, "
                                     + "which is why the detected list can be edited.")),
                        Bench.Group("Scope",
                            Ui.Small("Without a scope the first federation run returns every "
                                     + "soffit in the building. A level narrows what is reported; "
                                     + "a zone narrows what is measured. One level and one "
                                     + "category per test: the record holds a list and this panel "
                                     + "writes one of them."),
                            Ui.Small("The ignored category is matched anywhere in the category "
                                     + "name, so \"Duct\" also leaves out \"Duct Fittings\" and "
                                     + "\"Duct Insulation\". Every sprinkler head and smoke "
                                     + "detector is under the minimum somewhere.")),
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
            private static UIElement Sized(Panel field, double width)
            {
                field.Width = width;

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
