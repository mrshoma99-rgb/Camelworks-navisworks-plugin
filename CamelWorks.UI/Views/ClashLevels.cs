using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

            // The last storey taken out, kept until the next change so Remove can be undone.
            // Remove is one click beside a row and there was no way back from it: the name and
            // the height both had to be typed again from whatever the coordinator remembered.
            LevelStorey? dropped = null;

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

                if (dropped != null)
                {
                    var gone = dropped;

                    var back = Ui.Link("Put “" + gone.Name + "” back", () => Ui.Safely(() =>
                    {
                        if (!plan.Add(gone.Name, gone.Elevation)) return;

                        dropped = null;

                        if (Keep()) Redraw();
                    }));

                    undo.Children.Add(Ui.Across(
                        Ui.Small("Removed “" + gone.Name + "”, at "
                                 + Shown(gone.Elevation, millimetres) + "."),
                        back));
                }

                foot.Children.Clear();

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

                // Labelled, because two boxes side by side with nothing over them is a form where
                // the second one has to be guessed at.
                list.Children.Add(Headings());

                foreach (var storey in plan.Storeys.Reverse().ToList())
                    list.Children.Add(Storey(plan, storey, millimetres, Keep, Redraw,
                                             gone => dropped = gone));

                var add = Ui.Button("Add a level", Add);

                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Margin = new Thickness(0, 6, 0, 0);

                foot.Children.Add(add);

                var says = Ui.Small(Ui.Count(plan.Storeys.Count, "level")
                                    + ", listed top down. The top one runs to the top of the "
                                    + "building.");

                says.Margin = new Thickness(0, 8, 0, 0);

                foot.Children.Add(says);
            }

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

            return held;
        }

        /// <summary>The editor over a plan nobody can save, so the harness can draw it.</summary>
        /// <param name="plan">The storeys.</param>
        public static UIElement Drawn(LevelPlan plan) => Panel(plan, null);

        /// <summary>
        /// The labels over the three columns.
        ///
        /// No unit in any of them. The unit is stated once, by the selector that sets it, and a
        /// header carrying it as well is a second answer to the same question. The left margins
        /// are the text box's own padding, so a heading starts where the text under it does.
        /// </summary>
        private static UIElement Headings()
        {
            var row = Row();

            var name = Ui.Small("Level");

            name.Margin = new Thickness(Inset, 0, 0, 0);

            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var at = Ui.Small("Floor height");

            at.Margin = new Thickness(Gap + Inset, 0, 0, 0);

            Grid.SetColumn(at, 1);
            row.Children.Add(at);

            var span = Ui.Small("Covers");

            span.Margin = new Thickness(Gap, 0, 0, 0);

            Grid.SetColumn(span, 2);
            row.Children.Add(span);

            row.Margin = new Thickness(0, 0, 0, 4);

            return row;
        }

        /// <summary>
        /// The four columns every row of this editor sits in.
        ///
        /// Fixed widths, left-aligned. The columns were a star and a wide tab stretched the table
        /// to the width of the window, which put each row's own Remove seven hundred pixels from
        /// the row it removes.
        /// </summary>
        private static Grid Row()
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeightWidth + Gap) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SpanWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            return row;
        }

        /// <summary>One storey: its name, its floor height, and what it runs to.</summary>
        /// <param name="plan">The plan it belongs to.</param>
        /// <param name="storey">The storey.</param>
        /// <param name="millimetres">Whether heights are typed and read in millimetres.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        /// <param name="onDropped">Called with a storey that has just been removed.</param>
        private static UIElement Storey(LevelPlan plan, LevelStorey storey, bool millimetres,
                                        Func<bool> onKeep, Action onChange,
                                        Action<LevelStorey> onDropped)
        {
            var row = Row();

            // Settled, not per keystroke: this row is rebuilt by what it reports, so a box
            // that reported per keystroke threw itself away after the first letter.
            var name = Ui.Settled(storey.Name, "Level 1", typed =>
            {
                var wanted = (typed ?? string.Empty).Trim();

                if (wanted.Length == 0 || string.Equals(wanted, storey.Name,
                                                        StringComparison.Ordinal))
                    return;

                // Said, not swallowed. Rename returns false when the name is already used, and
                // the row rebuilds from the plan either way - so a refused rename looked like
                // the box had simply reverted.
                if (!plan.Rename(storey.Name, wanted))
                {
                    Ui.Told("There is already a level called “" + wanted + "”.");
                    onChange();
                    return;
                }

                if (onKeep()) onChange();
            }, NameWidth);

            name.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var at = Ui.Settled(Shown(storey.Elevation, millimetres), "0", typed =>
            {
                if (!double.TryParse((typed ?? string.Empty).Trim(), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out var said))
                {
                    Ui.Told("A level's height is a number of " + Unit(millimetres)
                            + ", above the model's zero.");
                    onChange();
                    return;
                }

                var wanted = Held(said, millimetres);

                if (Math.Abs(wanted - storey.Elevation) < 0.0005) return;

                if (!plan.Raise(storey.Name, wanted))
                {
                    Ui.Told("There is already a level at " + Shown(wanted, millimetres) + " "
                            + Unit(millimetres) + ". Two levels at one height would leave the "
                            + "upper one permanently empty.");
                    onChange();
                    return;
                }

                if (onKeep()) onChange();
            }, HeightWidth);

            at.VerticalAlignment = VerticalAlignment.Center;
            at.Margin = new Thickness(Gap, 0, 0, 0);
            at.ToolTip = "The floor height in " + Unit(millimetres)
                       + ", above the model's zero.";

            Grid.SetColumn(at, 1);
            row.Children.Add(at);

            // The span, said outright. A column of floor heights is a column somebody has to
            // subtract in their head to know what each storey actually covers.
            var ceiling = plan.Ceiling(storey.Name);

            var span = Ui.Small(ceiling == null
                ? Shown(storey.Elevation, millimetres) + " and above"
                : Shown(storey.Elevation, millimetres) + " to "
                  + Shown(ceiling.Value, millimetres));

            span.VerticalAlignment = VerticalAlignment.Center;
            span.Margin = new Thickness(Gap, 0, 0, 0);

            Grid.SetColumn(span, 2);
            row.Children.Add(span);

            var drop = Ui.Quiet("Remove", () => Ui.Safely(() =>
            {
                if (!plan.Remove(storey.Name)) return;

                onDropped(storey);

                if (onKeep()) onChange();
            }));

            drop.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(drop, 3);
            row.Children.Add(drop);

            return row;
        }

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

        /// <summary>The table's column widths, and the space between two columns.</summary>
        private const double NameWidth = 170;
        private const double HeightWidth = 84;
        private const double SpanWidth = 160;
        private const double Gap = 8;

        /// <summary>Where a text box's own padding puts the first character.</summary>
        private const double Inset = 10;

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
