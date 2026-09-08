using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Findings;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The keys the results table answers, and the sheet that lists them.
    ///
    /// One list, read twice: the board asks <see cref="Meaning"/> what a keystroke means, and the
    /// sheet is drawn from <see cref="All"/>. A shortcuts page that lists a key the product does
    /// not have sends somebody pressing it and then doubting the rest of the page, and that had
    /// already happened here - the Help window documented four keys belonging to a Review mode
    /// that no longer exists.
    ///
    /// The status keys come from <see cref="FindingStatusKeys"/>, so adding a status gives it a
    /// digit and gives the sheet a line without either being written down.
    ///
    /// Nothing destructive is bound. Every key here is undone by pressing another one: a status
    /// goes back, a tick comes off, the camera moves again. Delete, remove and run are not on the
    /// keyboard, because a bare key pressed by somebody who thought the search box had focus must
    /// not be able to take work away.
    /// </summary>
    public static class BoardKeys
    {
        /// <summary>What the board does with one key.</summary>
        public enum Does
        {
            /// <summary>Nothing. The key is passed on.</summary>
            Nothing,

            /// <summary>Read the row above.</summary>
            Up,

            /// <summary>Read the row below.</summary>
            Down,

            /// <summary>Tick or untick the row being read.</summary>
            Tick,

            /// <summary>Put the camera on the row being read.</summary>
            Focus,

            /// <summary>Drop the ticks.</summary>
            Clear,

            /// <summary>Set the row's status. <see cref="Meant.Status"/> says which.</summary>
            Status,
        }

        /// <summary>What one keystroke means to the board.</summary>
        public sealed class Meant
        {
            /// <summary>Build one.</summary>
            /// <param name="does">What the board does.</param>
            /// <param name="status">The status to set, for <see cref="Does.Status"/>.</param>
            internal Meant(Does does, FindingStatus? status = null)
            {
                Did = does;
                Status = status;
            }

            /// <summary>What the board does.</summary>
            public Does Did { get; }

            /// <summary>The status to set, or null.</summary>
            public FindingStatus? Status { get; }
        }

        /// <summary>One line of the shortcuts sheet.</summary>
        public sealed class Line
        {
            /// <summary>Build one.</summary>
            /// <param name="key">How the key is written.</param>
            /// <param name="what">What it does, in words.</param>
            internal Line(string key, string what)
            {
                Key = key;
                What = what;
            }

            /// <summary>How the key is written.</summary>
            public string Key { get; }

            /// <summary>What it does.</summary>
            public string What { get; }
        }

        /// <summary>
        /// What a keystroke means, or a <see cref="Does.Nothing"/> for one the board leaves alone.
        ///
        /// Modifiers are not consulted here: the caller drops anything held with Ctrl, Alt or Win
        /// before asking, because those belong to the host and to the standard edit commands.
        /// </summary>
        /// <param name="key">The key pressed.</param>
        public static Meant Meaning(Key key)
        {
            switch (key)
            {
                case Key.Up: return new Meant(Does.Up);
                case Key.Down: return new Meant(Does.Down);
                case Key.Space: return new Meant(Does.Tick);
                case Key.F: return new Meant(Does.Focus);
                case Key.Escape: return new Meant(Does.Clear);
            }

            var digit = Digit(key);

            if (digit == null) return new Meant(Does.Nothing);

            var status = FindingStatusKeys.Of(digit.Value);

            return status == null ? new Meant(Does.Nothing) : new Meant(Does.Status, status);
        }

        /// <summary>The digit a key stands for, taking the number row and the pad alike.</summary>
        /// <param name="key">The key pressed.</param>
        private static int? Digit(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return key - Key.D0;
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return key - Key.NumPad0;

            return null;
        }

        /// <summary>Every key the table answers, in the order the sheet lists them.</summary>
        public static IReadOnlyList<Line> All { get; } = Listed();

        /// <summary>Build the list once.</summary>
        private static IReadOnlyList<Line> Listed()
        {
            var said = new List<Line>
            {
                new Line("Up, Down", "Read the row above or below"),
                new Line("Space", "Tick or untick the row being read"),
            };

            said.AddRange(FindingStatusKeys.All.Select(one => new Line(
                one.Key.ToString(),
                "Set the row being read to " + FindingStatusWords.Of(one.Value))));

            said.Add(new Line("F", "Put the camera on the row being read"));
            said.Add(new Line("Esc", "Drop the ticks"));

            return said;
        }

        /// <summary>
        /// The sheet, as a panel.
        ///
        /// Apart from the window that holds it so the harness can draw it: a WPF window needs a
        /// screen and the harness has none, and a sheet nobody has looked at is the kind that
        /// lists a key twice.
        /// </summary>
        public static UIElement Drawn()
        {
            var rows = Ui.Stack();

            rows.Children.Add(Ui.Small(
                "The results table answers these while it has the keyboard. Click a row to give "
                + "it the keyboard. While you are typing in a box, every key goes to the box."));

            rows.Children.Add(new Border { Height = 8 });

            foreach (var line in All)
            {
                var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 5) };

                var chip = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 2),
                    Margin = new Thickness(0, 0, 9, 0),
                    MinWidth = 62,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = line.Key,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    }.Wearing("Body"),
                };

                chip.Tint(Border.BorderBrushProperty, "BdStrong");
                chip.Tint(Border.BackgroundProperty, "Card");

                DockPanel.SetDock(chip, Dock.Left);
                row.Children.Add(chip);

                var what = Ui.Small(line.What);

                what.TextWrapping = TextWrapping.Wrap;
                what.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(what);

                rows.Children.Add(row);
            }

            rows.Children.Add(new Border { Height = 6 });

            rows.Children.Add(Ui.Note(
                "Nothing on this list takes work away. A status goes back, a tick comes off. "
                + "Deleting, removing from the board and running a test are buttons only."));

            return rows;
        }

        /// <summary>Open the sheet.</summary>
        public static void Show() =>
            Ui.Configure("Keyboard shortcuts", null,
                new Ui.Page("Keyboard shortcuts", () => Ui.Loose(Drawn())));
    }
}
