using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Measurement;
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

            internal Fields(ISeat seat, FallCheck check, CheckRunSpec run, Action onChanged)
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
                var settings = FallSettings.From(_run);
                var sets = Sets();
                var blocked = Blocked;

                var sides = new List<string> { FallSettings.Loaded, FallSettings.Selected };

                sides.AddRange(sets);

                var optional = new List<string> { FallSettings.Nothing };

                optional.AddRange(sides);

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
                    Bench.Group("What to read",
                        Ui.Field("Floor from",
                                 Reading(Ui.Choice(Offered(sides, settings.Floors), settings.Floors,
                                                   pick => Keep(one => one.Floors = pick), 380))),
                        Ui.Across(
                            Sized(Ui.Field("Handrails from",
                                Reading(Ui.Choice(Offered(optional, settings.Handrails),
                                                  settings.Handrails,
                                                  pick => Keep(one => one.Handrails = pick), 180))), 190),
                            Sized(Ui.Field("Obstructions from",
                                Reading(Ui.Choice(Offered(optional, settings.Obstructions),
                                                  settings.Obstructions,
                                                  pick => Keep(one => one.Obstructions = pick), 180))), 190)),
                        Ui.Across(
                            Sized(Number("Cut elevation", settings.LevelMetres,
                                         metres => true,
                                         (one, metres) => one.LevelMetres = metres), 190),
                            Sized(Number("Slice thickness", settings.BandMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.BandMetres = metres), 190))),
                    Bench.Group("How to judge it",
                        Ui.Across(
                            Sized(Number("Maximum unprotected gap", settings.LimitMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.LimitMetres = metres), 190),
                            Sized(Number("Grid resolution", settings.CellMetres,
                                         metres => metres > 0,
                                         (one, metres) => one.CellMetres = metres), 190)),
                        Ui.Across(
                            Sized(Number("Handrail coverage distance",
                                         settings.HandrailToleranceMetres,
                                         metres => metres >= 0,
                                         (one, metres) => one.HandrailToleranceMetres = metres), 190),
                            Sized(Number("Minimum reportable gap in protection",
                                         settings.MinPassageMetres,
                                         metres => metres >= 0,
                                         (one, metres) => one.MinPassageMetres = metres), 190)),
                        Coarse(settings), Open(settings)),
                    blocked == null && sets.Count == 0
                        ? Ui.Note("This document holds no saved sets yet. Build one in Set Builder "
                                  + "and it appears in all three lists.")
                        : null,
                    Ui.Link("How the plan is drawn", Explain));
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
                    : Ui.Note("Every break in the protection is reported, however short.");

            /// <summary>
            /// The reference matter, behind one link rather than standing under the fields.
            ///
            /// Every sentence here answers "why would I change this", which is a question asked
            /// once. Standing text answers it on every visit, and it was 1,340 px of it.
            /// </summary>
            private void Explain() =>
                Ui.Configure("How the plan is drawn", null, new Ui.Page(
                    "How the plan is drawn",
                    () => Ui.Stack(
                        Bench.Group("The slice",
                            Ui.Small("An element whose box reaches the slice contributes its whole "
                                     + "outline in plan, so the thickness does not have to match a "
                                     + "slab. It is also why a column or a stair through the slice "
                                     + "reads as solid floor rather than as a hole, which is the "
                                     + "largest thing this check gets wrong."),
                            Ui.Small("Name a set of floors. Everything loaded reads the whole "
                                     + "federation at this elevation, walls and ducts included, "
                                     + "and each of those projects its own outline as though it "
                                     + "were floor.")),
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
                                     + "count as protecting an edge."),
                            Ui.Small("The minimum reportable gap in protection is the width of a "
                                     + "person, not of the void: a 300 mm break between two "
                                     + "handrails is too narrow to fall through.")))));

            /// <summary>A short number field, with its unit in the label.</summary>
            /// <param name="label">What the number is, without its unit.</param>
            /// <param name="value">What it is now, in metres.</param>
            /// <param name="allows">Whether a value read off the box may be written.</param>
            /// <param name="write">Puts an accepted value onto the settings.</param>
            private Panel Number(string label, double value, Func<double, bool> allows,
                                 Action<FallSettings, double> write) =>
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
            /// A picker whose contents come off the document, greyed with the reason when there is
            /// no document to read.
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
