using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Measurement;

namespace CamelWorks.UI.Views
{
    /// <summary>One entry of a matrix's legend, as something to fill in.</summary>
    internal sealed class MatrixKeyEdit
    {
        /// <summary>What the cells carrying it say — a letter, a mark, a word.</summary>
        internal string Symbol { get; set; } = string.Empty;

        /// <summary>Its wash as <c>#RRGGBB</c>, or empty for the one its place gives it.</summary>
        internal string Colour { get; set; } = string.Empty;

        /// <summary>Which test its cells become.</summary>
        internal MatrixTest Test { get; set; } = MatrixTest.Hard;

        /// <summary>The tolerance that test carries, in metres.</summary>
        internal double Tolerance { get; set; } = MatrixOptions.DefaultTolerance;

        /// <summary>False to keep the symbol readable but build nothing from it.</summary>
        internal bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// The one place a legend entry is written, whether it is being made or changed.
    ///
    /// A symbol carries five decisions and they arrive together. Adding one previously asked for the
    /// letter only, so every symbol was created as a hard clash at the default tolerance and the
    /// other four decisions were made elsewhere afterwards. Its colour, which is what is seen first
    /// on the grid, could only be reached from a window about how the whole matrix is drawn: a
    /// per-item setting filed under general ones.
    ///
    /// One form, reached from the plus and from the pencil beside it, in the panel the palette lives
    /// in.
    /// </summary>
    internal static class MatrixKey
    {
        /// <summary>Fill one in.</summary>
        /// <param name="title">What the window is doing — adding one, or changing one.</param>
        /// <param name="caption">What the button that finishes it says.</param>
        /// <param name="starting">What it holds now, which the window edits a copy of.</param>
        /// <param name="taken">
        /// Says why a name cannot be used, or null when it can. The legend is keyed on the
        /// symbol, so a clash would quietly replace the entry it collided with.
        /// </param>
        /// <param name="units">The seat's units, so the tolerance is typed the way the rest is.</param>
        /// <returns>What was filled in, or null when the window was closed without finishing.</returns>
        internal static MatrixKeyEdit? Ask(string title, string caption, MatrixKeyEdit starting,
                                           Func<string, string?> taken, Measure units)
        {
            if (starting == null || taken == null || units == null) return null;

            var held = new MatrixKeyEdit
            {
                Symbol = starting.Symbol,
                Colour = starting.Colour,
                Test = starting.Test,
                Tolerance = starting.Tolerance,
                Enabled = starting.Enabled,
            };

            MatrixKeyEdit? done = null;

            var window = new Window
            {
                Title = "CamelWorks - " + title,
                Width = 430,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 620,
                MinWidth = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var trouble = Ui.Stack();
            var body = Ui.Stack();

            var name = Ui.Text(held.Symbol, "A", typed => held.Symbol = typed.Trim(), Ui.Wide.Tight);

            // A cell is thirty-eight pixels wide and nothing was clipping it. The field invites "a
            // letter, a mark or a word", and a word overdrew its neighbours in the grid and in the
            // twenty-four pixel chip on the palette. Four characters is a mark; anything longer is a
            // name, and a name belongs in the legend line beside it.
            name.MaxLength = 4;

            var mm = Ui.Text(units.Bare(held.Tolerance), null, Ui.Wide.Tight);

            body.Children.Add(Ui.Field("What the cells say", name,
                "A letter or a short mark — whatever the sheet this matrix came from uses. Four "
                + "characters at most, because it has to read inside a cell."));

            Fill(body, held, mm, units);

            // Last. It defaults to Automatic and is right almost always, and it used to be asked
            // second — above the two fields that decide what the test actually is.
            //
            // The strip marks what is chosen itself. It used to be left to this to redraw it, and
            // this cannot: the dropdown that would trigger a rebuild is one of the things a
            // rebuild would take away while its own event is still running.
            body.Children.Add(Ui.Field("Colour",
                MatrixLook.Colours(held.Colour, colour => held.Colour = colour),
                "Automatic assigns a colour from its position in the legend, which is the "
                + "default appearance."));

            var take = Ui.Primary(caption, () =>
            {
                trouble.Children.Clear();

                held.Symbol = name.Text.Trim();

                if (held.Symbol.Length == 0)
                {
                    trouble.Children.Add(Ui.Problem("Enter the mark it shows in a cell."));
                    return;
                }

                var why = taken(held.Symbol);

                if (why != null)
                {
                    trouble.Children.Add(Ui.Problem(why));
                    return;
                }

                // A tolerance that cannot be parsed is refused rather than ignored. It was parsed
                // against the invariant culture only, and anything that did not parse simply did not
                // assign, so on a seat whose decimal separator is a comma, typing "12,5" left the
                // box showing 12,5, reported nothing, and saved the old number. A wrong clearance is
                // not something caught by eye in a grid of a hundred and ninety cells.
                if (!units.TryRead(mm.Text, out var tolerance) || tolerance < 0)
                {
                    trouble.Children.Add(Ui.Problem(
                        "\u201c" + mm.Text.Trim() + "\u201d is not a tolerance. Give it a number "
                        + "of " + units.LengthUnit + ", or zero."));
                    return;
                }

                held.Tolerance = tolerance;

                done = held;
                window.Close();
            });

            take.MinWidth = 92;

            var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

            var foot = new Border
            {
                Margin = new Thickness(0, 8, 0, 0),
                Child = Ui.Actions(take, Ui.Quiet("Cancel", () => window.Close())),
            };

            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            DockPanel.SetDock(trouble, Dock.Bottom);
            root.Children.Add(Ui.Loose(trouble));

            root.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(body),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var ground = new Border { Child = root };

            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            name.Focus();
            name.SelectAll();

            window.ShowDialog();

            return done;
        }

        /// <summary>
        /// The three fields under the name and the colour, drawn again whenever one changes.
        ///
        /// The first two children are the name and the colour and they are left alone: the name
        /// is a box somebody may be halfway through typing into, and rebuilding it would take the
        /// caret with it.
        /// </summary>
        /// <param name="body">The form.</param>
        /// <param name="held">What is being filled in.</param>
        /// <param name="mm">The tolerance box, built once so what is typed into it survives.</param>
        /// <param name="units">The seat's units, for the field's label.</param>
        private static void Fill(Panel body, MatrixKeyEdit held, TextBox mm, Measure units)
        {
            while (body.Children.Count > 2) body.Children.RemoveAt(2);

            // A duplicate test has no tolerance to give: it is the same thing modelled twice, and
            // the field sat there live and meaningful-looking with nothing to mean. Set from here
            // rather than by rebuilding the form, because the dropdown that changes it is one of
            // the things a rebuild would take away while its own event is still running.
            var tolerance = Ui.Small(string.Empty);

            void Kinded()
            {
                var duplicate = held.Test == MatrixTest.Duplicate;

                mm.IsEnabled = !duplicate;
                tolerance.Text = duplicate
                    ? "A duplicate is the same thing modelled twice, so there is no tolerance to set."
                    : string.Empty;
                tolerance.Visibility = duplicate ? Visibility.Visible : Visibility.Collapsed;
            }

            body.Children.Add(Ui.Field("Kind of test",
                Ui.Choice(new[] { "Hard", "Hard conservative", "Clearance", "Duplicate" },
                    MatrixNaming.Worded(held.Test),
                    picked =>
                    {
                        held.Test = picked == "Hard conservative" ? MatrixTest.HardConservative
                            : picked == "Clearance" ? MatrixTest.Clearance
                            : picked == "Duplicate" ? MatrixTest.Duplicate
                            : MatrixTest.Hard;

                        Kinded();
                    }, Ui.Wide.Word),
                "Hard is solids overlapping. Hard conservative tests the triangles instead — "
                + "slower, and it catches more. Clearance is closer than they are allowed to be. "
                + "Duplicate is the same thing modelled twice."));

            body.Children.Add(Ui.Field("Tolerance (" + units.LengthUnit + ")", mm));

            tolerance.TextWrapping = TextWrapping.Wrap;
            tolerance.Margin = new Thickness(0, -6, 0, 9);

            body.Children.Add(tolerance);

            Kinded();

            var live = Ui.Check("Build tests from this symbol", held.Enabled,
                                on => held.Enabled = on);

            live.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(live);

            body.Children.Add(Ui.Small("Unticked, its cells still say what was agreed and are "
                                       + "drawn struck through, and no test is built for them."));
        }

    }
}
