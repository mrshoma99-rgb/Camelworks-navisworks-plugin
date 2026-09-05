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

            bool Keep()
            {
                var landed = onKeep == null || onKeep();

                if (landed) onChanged?.Invoke();

                return landed;
            }

            void Redraw()
            {
                list.Children.Clear();

                // Labelled, because two boxes side by side with nothing over them is a form
                // where the second one has to be guessed at — and the guess that matters is
                // whether the number is metres or millimetres.
                if (!plan.IsEmpty) list.Children.Add(Headings());

                foreach (var storey in plan.Storeys.Reverse().ToList())
                    list.Children.Add(Storey(plan, storey, Keep, Redraw));

                if (plan.IsEmpty)
                    list.Children.Add(Ui.Small(
                        "None yet. Add the ground floor at its own height — usually zero — then "
                        + "each storey above it. A clash between one storey's floor and the next "
                        + "one's is on that storey."));

                foot.Children.Clear();

                var add = Ui.Quiet("Add a level",
                                   () => Ui.Safely(() => Adding(plan, near, Keep, Redraw)));

                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Margin = new Thickness(0, 4, 0, 0);

                foot.Children.Add(add);

                if (plan.IsEmpty) return;

                var says = Ui.Small(
                    Ui.Count(plan.Storeys.Count, "level") + ", listed top down. The highest runs "
                    + "to the top of the building, and a clash below the lowest is banded as such "
                    + "rather than pushed into it.");

                says.TextWrapping = TextWrapping.Wrap;
                says.Margin = new Thickness(0, 6, 0, 0);

                foot.Children.Add(says);
            }

            Redraw();

            held.Children.Add(list);
            held.Children.Add(foot);

            return held;
        }

        /// <summary>The editor over a plan nobody can save, so the harness can draw it.</summary>
        /// <param name="plan">The storeys.</param>
        public static UIElement Drawn(LevelPlan plan) => Panel(plan, null);

        /// <summary>The labels over the three columns.</summary>
        private static UIElement Headings()
        {
            var row = Row();

            var name = Ui.Eyebrow("Level");

            name.Margin = new Thickness(0);

            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var at = Ui.Eyebrow("Floor at, m");

            at.Margin = new Thickness(8, 0, 0, 0);

            Grid.SetColumn(at, 1);
            row.Children.Add(at);

            var span = Ui.Eyebrow("Covers");

            span.Margin = new Thickness(10, 0, 0, 0);

            Grid.SetColumn(span, 2);
            row.Children.Add(span);

            row.Margin = new Thickness(0, 0, 0, 6);

            return row;
        }

        /// <summary>The four columns every row of this editor sits in.</summary>
        private static Grid Row()
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            return row;
        }

        /// <summary>One storey: its name, its floor height, and what it runs to.</summary>
        /// <param name="plan">The plan it belongs to.</param>
        /// <param name="storey">The storey.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static UIElement Storey(LevelPlan plan, LevelStorey storey, Func<bool> onKeep,
                                        Action onChange)
        {
            var row = Row();

            // Settled, not per keystroke: this row is rebuilt by what it reports, so a box
            // that reported per keystroke threw itself away after the first letter.
            var name = Ui.Settled(storey.Name, "Level 1", typed =>
            {
                if (plan.Rename(storey.Name, typed) && onKeep()) onChange();
            }, 160);

            name.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var at = Ui.Settled(Metres(storey.Elevation), "0", typed =>
            {
                if (!double.TryParse((typed ?? string.Empty).Trim(), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out var wanted))
                    return;

                if (plan.Raise(storey.Name, wanted) && onKeep()) onChange();
            }, 76);

            at.VerticalAlignment = VerticalAlignment.Center;
            at.Margin = new Thickness(8, 0, 0, 0);
            at.ToolTip = "The floor height in metres, above the model's zero.";

            Grid.SetColumn(at, 1);
            row.Children.Add(at);

            // The span, said outright. A column of floor heights is a column somebody has to
            // subtract in their head to know what each storey actually covers.
            var ceiling = plan.Ceiling(storey.Name);

            var span = Ui.Small(ceiling == null
                ? Metres(storey.Elevation) + " m and above"
                : Metres(storey.Elevation) + " to " + Metres(ceiling.Value) + " m");

            span.VerticalAlignment = VerticalAlignment.Center;
            span.Margin = new Thickness(10, 0, 0, 0);

            Grid.SetColumn(span, 2);
            row.Children.Add(span);

            var drop = Ui.Quiet("Remove", () => Ui.Safely(() =>
            {
                if (plan.Remove(storey.Name) && onKeep()) onChange();
            }));

            drop.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(drop, 3);
            row.Children.Add(drop);

            return row;
        }

        /// <summary>Ask for a storey's name and height, and add it.</summary>
        /// <param name="plan">The plan to add to.</param>
        /// <param name="near">What the prompt belongs to.</param>
        /// <param name="onKeep">Writes the plan, and says whether it landed.</param>
        /// <param name="onAdded">Called once one is added.</param>
        private static void Adding(LevelPlan plan, FrameworkElement? near, Func<bool> onKeep,
                                   Action onAdded)
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

                Parts.Ask(near, "What height is " + title + "’s floor at, in metres?",
                          Metres(wanted), "Add", height =>
                {
                    if (!double.TryParse((height ?? string.Empty).Trim(), NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out var held))
                    {
                        Ui.Told("A level's height is a number of metres, above the model's zero.");
                        return;
                    }

                    if (!plan.Add(title, held))
                    {
                        Ui.Told("There is already a level at " + Metres(held) + " m. Two levels "
                                + "at one height would leave the upper one permanently empty.");
                        return;
                    }

                    onKeep();
                    onAdded();
                });
            });
        }

        /// <summary>A height as it is typed and read: metres, to the millimetre.</summary>
        /// <param name="value">The height in metres.</param>
        private static string Metres(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
