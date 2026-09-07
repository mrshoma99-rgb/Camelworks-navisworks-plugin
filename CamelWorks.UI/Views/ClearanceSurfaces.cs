using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Measurement;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The floor side of a clearance run: the surfaces detection found, and what to drop from them.
    ///
    /// Its own screen because the detector cannot tell a floor from a desk top, and everything here
    /// exists to say so before a run is paid for: the count, the total area, the elements the host
    /// refused, and a list ordered largest first so what is obviously not a floor can be taken out.
    /// A screen that said "detected walkable surfaces" and stopped there would read as though the
    /// software found the floors.
    ///
    /// What is dropped is written into the run's own settings, so the same desk tops are not dropped
    /// again on the next run.
    /// </summary>
    internal static class ClearanceSurfaces
    {
        /// <summary>One slope, as the picker writes it.</summary>
        /// <param name="degrees">The angle.</param>
        private static string Degrees(double degrees) =>
            degrees.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "°";

        /// <summary>How many rows of the list are drawn at once.</summary>
        private const int Shown = 500;

        /// <summary>
        /// The smallest-surface sizes offered, in square metres.
        ///
        /// Offered rather than typed: the setting is an area, this product has no area parser, and
        /// a number read in the wrong unit would silently drop every surface on an imperial job.
        /// The first is the detector's own default.
        /// </summary>
        private static readonly double[] Areas = { 0.05, 0.25, 1.0, 4.0, 10.0 };

        /// <summary>
        /// The slopes offered, in degrees.
        ///
        /// A floor laid to falls, a level-access ramp, the detector's own default, a car-park ramp
        /// at 1:5, and a stair pitch. Degrees are the same number on every seat, so unlike the
        /// areas these are typed the same way everywhere.
        /// </summary>
        private static readonly double[] Slopes = { 3.0, 5.0, 10.0, 12.0, 30.0 };

        // What was detected, held between visits: a federation sweep is a minute of traversal and
        // opening the window again must not cost it. Held with the run it was detected for, since
        // two runs detect different surfaces - a different minimum area, the stairs tick, the
        // floor-category preference - and the list of one is not the list of the other.
        private static List<WalkablePatch>? _patches;
        private static WalkableDiagnostics? _diagnostics;
        private static string? _detectedFor;

        /// <summary>Forget what was detected. Surfaces of the last federation are not this one's.</summary>
        internal static void Forget()
        {
            _patches = null;
            _diagnostics = null;
            _detectedFor = null;
        }

        /// <summary>
        /// The page, as the set-up window's own section.
        /// </summary>
        /// <param name="check">The check, which is what detects.</param>
        /// <param name="units">The seat's units, so areas and heights read in them.</param>
        /// <param name="runId">Which run is being set up.</param>
        /// <param name="keep">Writes a change back onto the run and into the project file.</param>
        internal static UIElement Page(ClearanceCheck check, Measure units, string runId,
                                       Action<Action<ClearanceSettings>> keep) =>
            new Screen(check, units, runId, keep).Build();

        private sealed class Screen
        {
            private readonly ClearanceCheck _check;
            private readonly Measure _units;
            private readonly Action<Action<ClearanceSettings>> _keep;

            private readonly StackPanel _log = Ui.Stack();
            private readonly StackPanel _list = Ui.Stack();

            private readonly string _runId;

            internal Screen(ClearanceCheck check, Measure units, string runId,
                            Action<Action<ClearanceSettings>> keep)
            {
                _check = check;
                _units = units;
                _runId = runId;
                _keep = keep;
            }

            /// <summary>
            /// The sizes the picker offers, including whatever the run is set to now.
            ///
            /// A run saved by another build may hold a size this list does not, and a picker whose
            /// current value is not one of its own entries draws as empty.
            /// </summary>
            /// <param name="held">The size the run holds, in square metres.</param>
            private List<string> Offered(double held)
            {
                var sizes = new List<double>(Areas);

                if (!sizes.Any(one => Math.Abs(one - held) < 0.0001)) sizes.Add(held);

                return sizes.OrderBy(one => one)
                            .Select(one => _units.Area(one))
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
            }

            /// <summary>
            /// What the run's settings say right now.
            ///
            /// Read back off the run rather than carried, so a tick written here is seen by the
            /// next draw: the settings object is a copy of the bag and the run is the record.
            /// </summary>
            private ClearanceSettings Settings => ClearanceSettings.From(_check.Spec(_runId));

            internal UIElement Build()
            {
                var settings = Settings;

                // What is held answers for the run it was detected for and no other.
                if (!string.Equals(_detectedFor, _runId, StringComparison.Ordinal)) Forget();

                Redraw();

                var stairs = Ui.Check("Leave stairs out",
                                      settings.ExcludeStairs,
                                      on => { _keep(one => one.ExcludeStairs = on); Stale(); });

                stairs.ToolTip = "Detection makes one surface per tread, so the flight above "
                                 + "reads as low headroom over the treads below it.";

                return Bench.Group("Walkable surfaces",
                    // The orientation is one sentence, over the list rather than under it: it says
                    // what the list is before it is read, and the rest of what used to stand here
                    // - what dropping a surface keeps, and why both ticks start off - was answering
                    // a question nobody had yet asked.
                    Ui.Small("Detection reads triangles and keeps every up-facing surface over "
                             + _units.Area(settings.MinPatchAreaSquareMetres)
                             + ", which is a floor, and is also a desk top, a worktop, a window "
                             + "sill and the top of a wide duct: read the list and drop what is "
                             + "not a floor."),
                    Ui.Field("Smallest surface kept",
                             Ui.Choice(Areas.Select(one => _units.Area(one)).ToList(),
                                       _units.Area(settings.MinPatchAreaSquareMetres),
                                       pick =>
                                       {
                                           var chosen = Areas.FirstOrDefault(
                                               one => string.Equals(_units.Area(one), pick,
                                                                    StringComparison.Ordinal));

                                           if (chosen <= 0) return;

                                           _keep(one => one.MinPatchAreaSquareMetres = chosen);
                                           Stale();
                                       }, 160)),
                    Ui.Field("Steepest surface still walked on",
                             Ui.Choice(Slopes.Select(one => Degrees(one)).ToList(),
                                       Degrees(settings.MaxSlopeDegrees),
                                       pick =>
                                       {
                                           var chosen = Slopes.FirstOrDefault(
                                               one => string.Equals(Degrees(one), pick,
                                                                    StringComparison.Ordinal));

                                           if (chosen <= 0) return;

                                           _keep(one => one.MaxSlopeDegrees = chosen);
                                           Stale();
                                       }, 160),
                             "A floor laid to falls is under 3°. A level-access ramp is 5°. A "
                             + "car-park ramp at 1:5 is 11.3°, and at anything under that it is "
                             + "not a walkable surface and nothing over it is checked."),
                    Ui.Check("Prefer elements whose category names a floor",
                             settings.PreferFloorCategories,
                             on => { _keep(one => one.PreferFloorCategories = on); Stale(); }),
                    stairs,
                    Ui.Runner("Find the surfaces", _log, primary: false, work: Find),
                    _log,
                    _list);
            }

            /// <summary>What was detected no longer answers what is being asked for.</summary>
            private void Stale()
            {
                Forget();

                // The dropped list goes too. A patch id is derived from the surface, so changing
                // what detection keeps re-derives every one of them and the ids left behind name
                // nothing: the drops would neither apply nor be visible to take back off.
                _keep(one => one.Excluded.Clear());

                _log.Children.Clear();

                Redraw();
            }

            /// <summary>Detect, and redraw the list underneath the button.</summary>
            /// <param name="job">The job, for progress and a Stop.</param>
            private void Find(Job job)
            {
                _diagnostics = new WalkableDiagnostics();

                _patches = _check.Detect(Settings, _diagnostics,
                                         read => job.Step(read, "elements read")).ToList();

                _detectedFor = _runId;

                Redraw();
            }

            /// <summary>
            /// What detection found and what it could not do, over the list. Stated before the run,
            /// not after it: 412 surfaces and 8,940 m² on two storeys of offices is a list with desk
            /// tops in it, and the number is the only thing that says so.
            /// </summary>
            /// <param name="say">Where the lines go.</param>
            private void Report(Panel say)
            {
                if (_diagnostics == null || _patches == null) return;

                var area = _patches.Sum(patch => patch.AreaSquareMetres);

                say.Children.Add(Ui.Line(Ui.Count(_patches.Count, "surface") + ", " + _units.Area(area)
                    + ", " + Ui.Count(_diagnostics.ElementsRefused, "element") + " refused", 1, true));

                if (_diagnostics.UpAxis != 2)
                    say.Children.Add(Ui.Problem("Most of this model's flat area faces "
                        + (_diagnostics.UpAxis == 0 ? "X" : "Y") + ", not Z. The clear height is "
                        + "measured up the Z axis, so what comes back will be wrong."));

                foreach (var note in _diagnostics.Notes) say.Children.Add(Ui.Small(note));
            }

            /// <summary>
            /// The detected surfaces, largest first, with the ones somebody dropped marked. Largest
            /// first because a 40 m² slab and a 0.4 m² sill are told apart by size before anything
            /// else.
            /// </summary>
            private void Redraw()
            {
                _list.Children.Clear();

                var settings = Settings;

                if (_patches == null)
                {
                    _list.Children.Add(Ui.Small("Nothing detected yet."));
                    return;
                }

                Report(_list);

                if (_patches.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("No surfaces were found.",
                        "Nothing loaded has an up-facing surface over "
                        + _units.Area(settings.MinPatchAreaSquareMetres)
                        + ". If this seat will not give triangles, use a set or the guess instead."));

                    return;
                }

                var rows = _patches
                    .OrderByDescending(patch => patch.AreaSquareMetres)
                    .Take(Shown)
                    .Select(patch => new TableRow(patch,
                        patch.Level ?? "—",
                        _units.Area(patch.AreaSquareMetres),
                        _units.Length(patch.TopMetres),
                        settings.Excluded.Contains(patch.Id) ? "dropped" : string.Empty)
                    {
                        Tone = settings.Excluded.Contains(patch.Id) ? Tone.Warn : Tone.Plain,
                    })
                    .ToList();

                var table = Ui.Table(rows, ("Level", 0), ("Area", 90), ("Top", 90), ("In the check", 90));

                _list.Children.Add(table);

                _list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Drop these {n}", (picked, said) => Drop(picked, said, dropping: true)),
                    new Ui.OnPicked("Include these {n}", (picked, said) => Drop(picked, said, dropping: false))));

                _list.Children.Add(Ui.Small(
                    Ui.Count(_patches.Count(patch => !settings.Excluded.Contains(patch.Id)), "surface")
                    + " in the check"
                    + (settings.Excluded.Count == 0
                        ? string.Empty
                        : ", " + settings.Excluded.Count + " dropped")
                    + (_patches.Count > Shown ? ". The largest " + Shown + " are listed." : string.Empty)));
            }

            /// <summary>Drop the picked surfaces from the run, or put them back.</summary>
            /// <param name="picked">The rows picked.</param>
            /// <param name="said">Where the bar says what it did.</param>
            /// <param name="dropping">Whether they are dropped.</param>
            private void Drop(IReadOnlyList<object> picked, Panel said, bool dropping)
            {
                var touched = 0;

                _keep(settings =>
                {
                    foreach (var one in picked)
                    {
                        if (!(one is WalkablePatch patch)) continue;

                        if (dropping ? settings.Excluded.Add(patch.Id) : settings.Excluded.Remove(patch.Id))
                            touched++;
                    }
                });

                Redraw();

                said.Children.Add(Ui.Done(Ui.Count(touched, "surface")
                    + (dropping ? " dropped from the run." : " back in the run.")));
            }
        }
    }
}
