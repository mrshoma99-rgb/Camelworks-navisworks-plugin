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
        /// <summary>How many rows of the list are drawn at once.</summary>
        private const int Shown = 500;

        // What was detected, held between visits: a federation sweep is a minute of traversal and
        // opening the window again must not cost it.
        private static List<WalkablePatch>? _patches;
        private static WalkableDiagnostics? _diagnostics;

        /// <summary>Forget what was detected. Surfaces of the last federation are not this one's.</summary>
        internal static void Forget()
        {
            _patches = null;
            _diagnostics = null;
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
            /// What the run's settings say right now.
            ///
            /// Read back off the run rather than carried, so a tick written here is seen by the
            /// next draw: the settings object is a copy of the bag and the run is the record.
            /// </summary>
            private ClearanceSettings Settings => ClearanceSettings.From(_check.Spec(_runId));

            internal UIElement Build()
            {
                var settings = Settings;

                Redraw();

                return Bench.Group(ClearanceSettings.Detected,
                    Ui.Check("Prefer elements whose category names a floor",
                             settings.PreferFloorCategories,
                             on => { _keep(one => one.PreferFloorCategories = on); Stale(); }),
                    Ui.Check("Leave stairs out — detection makes one surface per tread, and the "
                             + "flight above then reads as low headroom over the treads below it",
                             settings.ExcludeStairs,
                             on => { _keep(one => one.ExcludeStairs = on); Stale(); }),
                    Ui.Runner("Find the surfaces", _log, primary: false, work: Find),
                    _log,
                    _list,
                    Ui.Small("Detection reads triangles: it keeps every up-facing surface over "
                             + _units.Area(settings.MinPatchAreaSquareMetres)
                             + ", which is a floor, and is also a desk top, a worktop, a window "
                             + "sill and the top of a wide duct. Read the list before running "
                             + "the check and drop what is not a floor."),
                    Ui.Small("What is dropped is kept in the run's own settings, so the same desk "
                             + "tops are not dropped again next time."),
                    Ui.Small("Both ticks are off to start with: the geometric answer is the "
                             + "default, and a stair with a soffit too low over it is still "
                             + "worth reporting."));
            }

            /// <summary>What was detected no longer answers what is being asked for.</summary>
            private void Stale()
            {
                Forget();

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
                    new Ui.OnPicked("Put these {n} back", (picked, said) => Drop(picked, said, dropping: false))));

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
