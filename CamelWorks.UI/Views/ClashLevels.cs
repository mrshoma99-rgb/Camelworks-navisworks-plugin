using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The storeys the project says the building has.
    ///
    /// <see cref="LevelSet"/> can read levels off a coordination grid, off a Level property, or
    /// infer them from where things sit. None of the three is what the client calls the second
    /// floor: a federation routinely has no grid, spells its storeys three ways across three
    /// disciplines, and infers levels named after their own elevation. This is the coordinator
    /// stating it — the ground floor at zero, the first floor at five metres, and so on up — and
    /// it is what makes "show me this floor's clashes" a question the board can answer.
    ///
    /// A storey is one height, not a range. It runs from its own floor up to the next one's, so
    /// there is nowhere for a gap to open; a range per storey would let somebody leave five to six
    /// metres unclaimed, and a clash in that gap would vanish from every band on the screen.
    ///
    /// Listed top down, the way a building is drawn on a section.
    /// </summary>
    public static class ClashLevels
    {
        /// <summary>
        /// The editor, as a panel.
        /// </summary>
        /// <param name="plan">The storeys, edited in place.</param>
        /// <param name="near">What a prompt belongs to, or null in the harness.</param>
        /// <param name="onKeep">Writes the plan to the project file, and says whether it landed.</param>
        /// <param name="onChanged">Called after a change was kept, so the board can redraw.</param>
        public static UIElement Panel(LevelPlan plan, FrameworkElement? near,
                                      Func<bool>? onKeep = null, Action? onChanged = null)
        {
            var held = Ui.Stack();

            if (plan == null) return held;

            var list = Ui.Stack();
            var foot = Ui.Stack();
            var undo = Ui.Stack();

            // Heights are stored in metres and typed in whichever unit the project works in.
            // A model set up in millimetres has its storeys written on the drawings as 9600, and
            // asking for 9.6 is asking somebody to convert every one of them by hand.
            var millimetres = false;

            // The storeys taken out by the last Remove, kept until the next change so it can be
            // undone. Remove now acts on the picked rows, so what it takes out is a list: the
            // alternative to putting them back is typing every name and height again.
            var dropped = new List<LevelStorey>();

            bool Keep()
            {
                var landed = onKeep == null || onKeep();

                if (landed) onChanged?.Invoke();

                return landed;
            }

            void Redraw()
            {
                list.Children.Clear();
                undo.Children.Clear();
                foot.Children.Clear();

                if (dropped.Count > 0)
                {
                    var gone = dropped.ToList();

                    var back = Ui.Link(gone.Count == 1
                        ? "Put “" + gone[0].Name + "” back"
                        : "Put these " + gone.Count.ToString(CultureInfo.InvariantCulture)
                          + " back", () => Ui.Safely(() =>
                    {
                        var any = false;

                        foreach (var storey in gone)
                            any |= plan.Add(storey.Name, storey.Elevation);

                        if (!any) return;

                        dropped.Clear();

                        if (Keep()) Redraw();
                    }));

                    undo.Children.Add(Ui.Across(Ui.Small(Removed(gone, millimetres)), back));
                }

                if (plan.IsEmpty)
                {
                    // The state and the verb that ends it, together. A line of prose saying what
                    // is missing leaves the button that fixes it somewhere else on the panel.
                    list.Children.Add(Ui.Empty(
                        "No levels yet",
                        "Add the ground floor at its own height, usually zero, then each storey "
                        + "above it.",
                        "Add a level", Add));

                    return;
                }

                var table = Table(plan, millimetres);

                // The verbs are the bar's, not each row's. A Remove per row is one press per
                // storey and no way to clear several; the bar carries the count, All and None,
                // and a Remove that states how many it is about to take out.
                list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Rename", (picked, said) =>
                        Rename(plan, near, picked, said, Keep, Redraw)),
                    new Ui.OnPicked("Set height", (picked, said) =>
                        Raise(plan, near, picked, said, millimetres, Keep, Redraw)),
                    new Ui.OnPicked("Remove {n}", (picked, said) =>
                        Drop(plan, near, picked, said, millimetres, Keep, Redraw,
                             gone => { dropped.Clear(); dropped.AddRange(gone); }),
                        danger: true)));

                var add = Ui.Button("Add a level", Add);

                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Margin = new Thickness(0, 6, 0, 0);

                foot.Children.Add(add);

                // How many there are is on the picking bar, so this says only what the order
                // means and what the top row covers.
                var says = Ui.Small("Listed top down. The top level runs to the top of the "
                                    + "building.");

                says.Margin = new Thickness(0, 8, 0, 0);

                foot.Children.Add(says);
            }

            // Working the storeys out reads the height of every element in the federation, which
            // is the most expensive read this product makes. It is a press somebody chooses,
            // once, rather than something every board load pays for.
            var worked = Ui.Stack();

            var derive = Ui.Runner("Work them out from the model", worked, job =>
                Derive(plan, job, worked, Keep, Redraw), primary: false);

            void Add() => Ui.Safely(() => Adding(plan, near, millimetres, Keep, Redraw));

            var unit = Ui.Choice(new[] { Metres, Millimetres },
                                 Metres, chosen =>
            {
                millimetres = string.Equals(chosen, Millimetres, StringComparison.Ordinal);

                Redraw();
            }, 140);

            var units = Ui.Field("Heights in", unit);

            units.HorizontalAlignment = HorizontalAlignment.Left;
            units.Margin = new Thickness(0, 0, 0, 12);

            Redraw();

            held.Children.Add(ClashSettingsViews.Head(
                "Levels",
                "The storeys this project declares. The board bands clashes by them.",
                "These outrank the levels read off a coordination grid or a Level property, "
                + "because a federation routinely has no grid and spells its storeys differently "
                + "in each discipline. A storey runs from its own floor up to the next one's, so "
                + "no height between the lowest and the highest is unclaimed. A clash below the "
                + "lowest storey is banded as below it rather than pushed into it."));

            held.Children.Add(units);
            held.Children.Add(list);
            held.Children.Add(foot);
            held.Children.Add(undo);

            var derived = Ui.Small("Or have them worked out from where the elements actually sit. "
                + "What it finds is added to the list above, named after each height, for you to "
                + "rename to the client's own words.");

            derived.Margin = new Thickness(0, 10, 0, 0);

            held.Children.Add(derived);

            held.Children.Add(derive);
            held.Children.Add(worked);

            return held;
        }

        /// <summary>
        /// Work the storeys out of the model and put what is found in the plan.
        ///
        /// A histogram of where elements sit, which is what <see cref="LevelSet.Derive"/> does.
        /// The heights it finds are added to whatever the plan already holds rather than
        /// replacing it: a coordinator who has typed three storeys and wants the rest found
        /// keeps the three they typed, and a height already claimed is refused by the plan.
        /// </summary>
        /// <param name="plan">The storeys, added to in place.</param>
        /// <param name="job">The run this is part of, for progress and for stopping.</param>
        /// <param name="said">Where it reports what it found.</param>
        /// <param name="keep">Writes the plan to the project file.</param>
        /// <param name="redraw">Redraws the list.</param>
        private static void Derive(LevelPlan plan, Job job, Panel said, Func<bool> keep,
                                   Action redraw)
        {
            var session = Host.Current;

            if (session == null)
            {
                said.Children.Add(Ui.Line(Host.NoModel));
                return;
            }

            var scale = session.MetresPerUnit;
            var elevations = new List<double>();

            job.Say("Measuring the model...");

            foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
            {
                if (!item.HasGeometry) continue;

                elevations.Add(item.Bounds.MinZ * scale);

                if (!job.Step(elevations.Count, "elements measured")) return;
            }

            if (elevations.Count == 0)
            {
                said.Children.Add(Ui.Note("Nothing loaded has geometry to measure."));
                return;
            }

            var found = LevelSet.Derive(elevations);
            var added = 0;

            foreach (var band in found.Bands)
                if (plan.Add(band.Name, band.Elevation)) added++;

            if (added == 0)
            {
                said.Children.Add(Ui.Note("Nothing new: the heights it found are already claimed "
                    + "by storeys in the list."));
                return;
            }

            if (!keep())
            {
                said.Children.Add(Ui.Problem("The project file would not take them."));
                return;
            }

            redraw();

            said.Children.Add(Ui.Done(Ui.Count(added, "level") + " added, from "
                + Ui.Count(found.ElementsConsidered, "element") + ". "
                + (found.DiscardedClusters > 0
                    ? Ui.Count(found.DiscardedClusters, "other height")
                      + " had too little standing on them to be a storey. "
                    : string.Empty)
                + "Rename them to whatever the client calls them."));
        }

        /// <summary>The editor over a plan nobody can save, so the harness can draw it.</summary>
        /// <param name="plan">The storeys.</param>
        public static UIElement Drawn(LevelPlan plan) => Panel(plan, null);

        /// <summary>
        /// The storeys as a table, top down.
        ///
        /// Each column carries its own unit, so the header states what the numbers under it are
        /// in. The span is a column of its own because a column of floor heights is one somebody
        /// has to subtract in their head to know what each storey covers.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="millimetres">Whether heights are shown in millimetres.</param>
        private static ListView Table(LevelPlan plan, bool millimetres)
        {
            var rows = plan.Storeys.Reverse().Select(storey =>
            {
                var ceiling = plan.Ceiling(storey.Name);

                return new TableRow(storey,
                    storey.Name,
                    Shown(storey.Elevation, millimetres),
                    ceiling == null
                        ? Shown(storey.Elevation, millimetres) + " and above"
                        : Shown(storey.Elevation, millimetres) + " to "
                          + Shown(ceiling.Value, millimetres));
            }).ToList();

            return Ui.Table(rows,
                            ("Level", 190),
                            ("Floor height (" + Unit(millimetres) + ")", 120),
                            ("Covers (" + Unit(millimetres) + ")", 0));
        }

        /// <summary>The storeys behind the picked rows, in the order the table holds them.</summary>
        /// <param name="picked">What the picking bar handed back.</param>
        private static IReadOnlyList<LevelStorey> Storeys(IReadOnlyList<object> picked) =>
            (picked ?? Array.Empty<object>()).OfType<LevelStorey>().ToList();

        /// <summary>Rename the one picked storey.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="near">What the prompt belongs to, or null in the harness.</param>
        /// <param name="picked">The picked rows.</param>
        /// <param name="said">Where the bar says what happened.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onChange">Called after the plan changed.</param>
        private static void Rename(LevelPlan plan, FrameworkElement? near,
                                   IReadOnlyList<object> picked, Panel said,
                                   Func<bool> onKeep, Action onChange)
        {
            var storeys = Storeys(picked);

            said.Children.Clear();

            if (storeys.Count != 1)
            {
                said.Children.Add(Ui.Note("Pick one level to rename."));
                return;
            }

            if (near == null) return;

            var storey = storeys[0];

            Parts.Ask(near, "What is this level called?", storey.Name, "Rename", typed =>
            {
                var wanted = (typed ?? string.Empty).Trim();

                if (wanted.Length == 0
                    || string.Equals(wanted, storey.Name, StringComparison.Ordinal))
                    return;

                if (!plan.Rename(storey.Name, wanted))
                {
                    Ui.Told("There is already a level called “" + wanted + "”.");
                    return;
                }

                if (onKeep()) onChange();
            });
        }

        /// <summary>Set the one picked storey's floor height.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="near">What the prompt belongs to, or null in the harness.</param>
        /// <param name="picked">The picked rows.</param>
        /// <param name="said">Where the bar says what happened.</param>
        /// <param name="millimetres">Whether heights are typed in millimetres.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onChange">Called after the plan changed.</param>
        private static void Raise(LevelPlan plan, FrameworkElement? near,
                                  IReadOnlyList<object> picked, Panel said, bool millimetres,
                                  Func<bool> onKeep, Action onChange)
        {
            var storeys = Storeys(picked);

            said.Children.Clear();

            if (storeys.Count != 1)
            {
                said.Children.Add(Ui.Note("Pick one level to set a height on."));
                return;
            }

            if (near == null) return;

            var storey = storeys[0];

            Parts.Ask(near, "What height is " + storey.Name + "’s floor at, in "
                            + Unit(millimetres) + "?",
                      Shown(storey.Elevation, millimetres), "Set", typed =>
            {
                if (!double.TryParse((typed ?? string.Empty).Trim(), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out var height))
                {
                    Ui.Told("A level's height is a number of " + Unit(millimetres)
                            + ", above the model's zero.");
                    return;
                }

                var wanted = Held(height, millimetres);

                if (Math.Abs(wanted - storey.Elevation) < 0.0005) return;

                if (!plan.Raise(storey.Name, wanted))
                {
                    Ui.Told("There is already a level at " + Shown(wanted, millimetres) + " "
                            + Unit(millimetres) + ". Two levels at one height would leave the "
                            + "upper one permanently empty.");
                    return;
                }

                if (onKeep()) onChange();
            });
        }

        /// <summary>
        /// Take the picked storeys out, once the count has been confirmed.
        ///
        /// The confirmation states the count because the bar can be holding every row in the
        /// table, and a plan with no storeys bands nothing on the board.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="near">What the prompt belongs to, or null in the harness.</param>
        /// <param name="picked">The picked rows.</param>
        /// <param name="said">Where the bar says what happened.</param>
        /// <param name="millimetres">Whether heights are shown in millimetres.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onChange">Called after the plan changed.</param>
        /// <param name="onDropped">Called with what was taken out, so it can be put back.</param>
        private static void Drop(LevelPlan plan, FrameworkElement? near,
                                 IReadOnlyList<object> picked, Panel said, bool millimetres,
                                 Func<bool> onKeep, Action onChange,
                                 Action<IReadOnlyList<LevelStorey>> onDropped)
        {
            var storeys = Storeys(picked);

            said.Children.Clear();

            if (storeys.Count == 0) return;

            void Take()
            {
                var gone = new List<LevelStorey>();

                foreach (var storey in storeys)
                    if (plan.Remove(storey.Name)) gone.Add(storey);

                if (gone.Count == 0) return;

                onDropped(gone);

                if (onKeep()) onChange();
            }

            if (near == null) { Take(); return; }

            Parts.Sure(near,
                       storeys.Count == 1
                           ? "Remove “" + storeys[0].Name + "”, at "
                             + Shown(storeys[0].Elevation, millimetres) + " "
                             + Unit(millimetres) + "? Clashes banded by it move to the storey "
                             + "below."
                           : "Remove these " + storeys.Count.ToString(CultureInfo.InvariantCulture)
                             + " levels? Clashes banded by them move to the storey below.",
                       "Remove " + Ui.Count(storeys.Count, "level"),
                       () => Ui.Safely(Take));
        }

        /// <summary>What the undo line says about the last Remove.</summary>
        /// <param name="gone">What was taken out.</param>
        /// <param name="millimetres">Whether heights are shown in millimetres.</param>
        private static string Removed(IReadOnlyList<LevelStorey> gone, bool millimetres) =>
            gone.Count == 1
                ? "Removed “" + gone[0].Name + "”, at "
                  + Shown(gone[0].Elevation, millimetres) + "."
                : "Removed " + Ui.Count(gone.Count, "level") + ": "
                  + string.Join(", ", gone.Select(one => one.Name)) + ".";

        /// <summary>Ask for a storey's name and height, and add it.</summary>
        /// <param name="plan">The plan to add to.</param>
        /// <param name="near">What the prompt belongs to.</param>
        /// <param name="millimetres">Whether heights are typed and read in millimetres.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onAdded">Called once one is added.</param>
        private static void Adding(LevelPlan plan, FrameworkElement? near, bool millimetres,
                                   Func<bool> onKeep, Action onAdded)
        {
            if (near == null) return;

            var storeys = plan.Storeys;

            // Offered above the top one by the gap the building already uses, so a block of equal
            // storeys is typed by pressing this and taking what it says.
            var wanted = storeys.Count == 0
                ? 0
                : storeys.Count == 1
                    ? storeys[0].Elevation + 3
                    : storeys[storeys.Count - 1].Elevation
                      + (storeys[storeys.Count - 1].Elevation
                         - storeys[storeys.Count - 2].Elevation);

            Parts.Ask(near, "What is the level called? “Ground floor”, “Level 1”.",
                      "Level " + (storeys.Count + 1).ToString(CultureInfo.InvariantCulture),
                      "Next", said =>
            {
                var title = (said ?? string.Empty).Trim();

                if (title.Length == 0) return;

                if (plan.Has(title))
                {
                    Ui.Told("There is already a level called “" + title + "”.");
                    return;
                }

                Parts.Ask(near, "What height is " + title + "’s floor at, in "
                                + Unit(millimetres) + "?",
                          Shown(wanted, millimetres), "Add", height =>
                {
                    if (!double.TryParse((height ?? string.Empty).Trim(), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out var said))
                    {
                        Ui.Told("A level's height is a number of " + Unit(millimetres)
                                + ", above the model's zero.");
                        return;
                    }

                    var held = Held(said, millimetres);

                    if (!plan.Add(title, held))
                    {
                        Ui.Told("There is already a level at " + Shown(held, millimetres) + " "
                                + Unit(millimetres) + ". Two levels at one height would leave "
                                + "the upper one permanently empty.");
                        return;
                    }

                    onKeep();
                    onAdded();
                });
            });
        }

        /// <summary>The two units a height can be typed in.</summary>
        private const string Metres = "Metres";
        private const string Millimetres = "Millimetres";

        /// <summary>The unit's short form, for a label or a sentence.</summary>
        /// <param name="millimetres">Whether millimetres are in force.</param>
        private static string Unit(bool millimetres) => millimetres ? "mm" : "m";

        /// <summary>
        /// A stored height as it is shown and typed.
        ///
        /// Held in metres whatever is on screen, because that is what the level set and every
        /// band on the board read. Millimetres are shown whole: a storey at 9.6 m is 9600, and
        /// three decimal places on it would be three zeroes.
        /// </summary>
        /// <param name="value">The height in metres.</param>
        /// <param name="millimetres">Whether millimetres are in force.</param>
        private static string Shown(double value, bool millimetres) =>
            millimetres
                ? (value * 1000).ToString("0.#", CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>A typed height as it is stored: metres.</summary>
        /// <param name="said">What was typed.</param>
        /// <param name="millimetres">Whether millimetres are in force.</param>
        private static double Held(double said, bool millimetres) =>
            millimetres ? said / 1000 : said;
    }
}
