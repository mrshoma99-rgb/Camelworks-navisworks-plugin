using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;

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
    /// <b>A symbol is five decisions and they arrive together.</b> Adding one used to ask for the
    /// letter and nothing else, so every symbol was born a hard clash at the default tolerance and
    /// the other four decisions were made somewhere else afterwards - if the person knew where to
    /// look. And its colour, which is the thing you see first on the grid, could only be reached
    /// from a window about how the whole matrix is drawn: a per-item setting filed under general
    /// ones, which is where you go looking for it last.
    ///
    /// One form, from the plus and from the pencil beside it, in the panel the palette lives in.
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
        /// <returns>What was filled in, or null when the window was closed without finishing.</returns>
        internal static MatrixKeyEdit? Ask(string title, string caption, MatrixKeyEdit starting,
                                           Func<string, string?> taken)
        {
            if (starting == null || taken == null) return null;

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

            var name = Ui.Text(held.Symbol, "A", typed => held.Symbol = typed.Trim(), 70);

            var mm = Ui.Text(
                (held.Tolerance * 1000).ToString("0.###", CultureInfo.CurrentCulture), null, 80);

            body.Children.Add(Ui.Field("What the cells say", name,
                "A letter, a mark or a word — whatever the sheet this matrix came from uses."));

            body.Children.Add(Ui.Field("Colour",
                MatrixLook.Colours(held.Colour, colour =>
                {
                    held.Colour = colour;

                    // Redrawn rather than re-tinted: the strip marks which one is chosen with a
                    // border, and the one that was chosen a moment ago has to lose it.
                    Fill(body, held, mm);
                }),
                "Automatic gives it one from where it sits in the legend, which is what a matrix "
                + "nobody has coloured looks like."));

            Fill(body, held, mm);

            var take = Ui.Primary(caption, () =>
            {
                trouble.Children.Clear();

                held.Symbol = name.Text.Trim();

                if (held.Symbol.Length == 0)
                {
                    trouble.Children.Add(Ui.Problem("Give it something to say in a cell."));
                    return;
                }

                var why = taken(held.Symbol);

                if (why != null)
                {
                    trouble.Children.Add(Ui.Problem(why));
                    return;
                }

                // <b>A tolerance that cannot be read is refused, not ignored.</b> It was parsed
                // as it was typed, against the invariant culture only, and anything that did not
                // parse simply did not assign - so on a seat where the decimal separator is a
                // comma, typing "12,5" left the box saying 12,5, said nothing, and saved the old
                // number. A wrong clearance is not a thing anybody catches by eye in a grid of a
                // hundred and ninety cells.
                if (!Millimetres(mm.Text, out var tolerance))
                {
                    trouble.Children.Add(Ui.Problem(
                        "\u201c" + mm.Text.Trim() + "\u201d is not a tolerance. Give it a number "
                        + "of millimetres, or zero."));
                    return;
                }

                held.Tolerance = tolerance / 1000;

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
        private static void Fill(Panel body, MatrixKeyEdit held, TextBox mm)
        {
            while (body.Children.Count > 2) body.Children.RemoveAt(2);

            body.Children.Add(Ui.Field("Kind of test",
                Ui.Choice(new[] { "Hard", "Hard conservative", "Clearance", "Duplicate" },
                    MatrixNaming.Worded(held.Test),
                    picked =>
                    {
                        held.Test = picked == "Hard conservative" ? MatrixTest.HardConservative
                            : picked == "Clearance" ? MatrixTest.Clearance
                            : picked == "Duplicate" ? MatrixTest.Duplicate
                            : MatrixTest.Hard;
                    }, 160)));

            body.Children.Add(Ui.Field("Tolerance, in millimetres", mm));

            var live = Ui.Check("Build tests from this symbol", held.Enabled,
                                on => held.Enabled = on);

            live.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(live);

            body.Children.Add(Ui.Small("Unticked, its cells still say what was agreed and are "
                                       + "drawn struck through, and no test is built for them."));
        }

        /// <summary>
        /// A tolerance in millimetres, read the way the person typing it writes numbers.
        ///
        /// Their own culture first, because that is what their keyboard and their habits give;
        /// the invariant one after, so a sheet or a colleague's "12.5" is still read on a seat
        /// that writes "12,5". Anything that is neither is refused rather than dropped.
        /// </summary>
        /// <param name="said">What was typed.</param>
        /// <param name="millimetres">The number, when there is one.</param>
        private static bool Millimetres(string? said, out double millimetres)
        {
            millimetres = 0;

            var text = (said ?? string.Empty).Trim();

            if (text.Length == 0) return false;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out millimetres)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out millimetres))
                return false;

            return millimetres >= 0;
        }
    }
}
