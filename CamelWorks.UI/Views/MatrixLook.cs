using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// How the matrix is drawn, as something somebody can change.
    ///
    /// <b>Every one of these was a decision somebody else made.</b> Which triangle is the live
    /// one, whether the dead half echoes, what colour a clearance is, whether the code gutter is
    /// there - all of them defensible, none of them universal, and all of them baked into the
    /// drawing where nobody could reach them. A practice that has drawn its matrices in the lower
    /// triangle for fifteen years does not want to be told its sheet is upside down, and one
    /// whose set names already carry the discipline code does not want the column.
    ///
    /// So they are asked here, in one window, and saved with the matrix rather than with the
    /// seat - because they are properties of the matrix that gets sent out, not of the machine it
    /// was last opened on. Nothing in here changes a single test: a pair is written to both cells
    /// whichever half it was put in, and a hidden number is still the number the test is named
    /// after.
    ///
    /// <b>Everything here is about the whole grid.</b> What one symbol means and what colour it
    /// is are settings of that symbol, and they live on the palette with it - a per-item setting
    /// filed under general ones is one nobody finds.
    /// </summary>
    internal static class MatrixLook
    {
        /// <summary>
        /// The washes on offer, pale enough to carry a letter at full contrast.
        ///
        /// The first eight are the ramp a symbol gets when nobody has chosen for it, so picking
        /// one deliberately and getting the one you already had is not a surprise. The last two
        /// are the ones people ask for that the ramp has no room for.
        /// </summary>
        private static readonly string[] Washes =
        {
            "#DCEAF8", "#DDF2E4", "#FBEFD3", "#EAE2F8", "#D6F0EF",
            "#FADCE8", "#E9F2CE", "#E2E6EC", "#FDDCD2", "#DCD9F3",
        };

        /// <summary>Open it.</summary>
        /// <param name="style">How it is drawn now. Edited in place as things are pressed.</param>
        /// <param name="onChanged">Called after anything changes, to draw it again.</param>
        internal static void Ask(MatrixStyle style, Action onChanged)
        {
            if (style == null || onChanged == null) return;

            var window = new Window
            {
                Title = "CamelWorks - How the matrix is drawn",
                Width = 560,
                Height = 640,
                MinWidth = 420,
                MinHeight = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var pages = Ui.Stack();

            // Everything applies at once and there is no Cancel, because every one of these is a
            // drawing choice you judge by looking at it - and the window it changes is the one
            // behind this. An OK button would mean choosing a colour blind and pressing it twice.
            void Redrawn()
            {
                onChanged();
                Fill(pages, style, Redrawn);
            }

            Fill(pages, style, Redrawn);

            var root = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };

            var done = Ui.Primary("Done", () => window.Close());

            done.MinWidth = 92;

            var foot = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Child = new ContentControl
                {
                    Content = done,
                    HorizontalAlignment = HorizontalAlignment.Right,
                },
            };

            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            root.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(pages),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var ground = new Border { Child = root };

            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();
        }

        /// <summary>Draw the whole window's contents, which happens again after every change.</summary>
        /// <param name="into">The panel to fill.</param>
        /// <param name="style">How it is drawn now.</param>
        /// <param name="onChanged">Called after anything changes.</param>
        private static void Fill(Panel into, MatrixStyle style, Action onChanged)
        {
            into.Children.Clear();

            into.Children.Add(Bench.Group("The grid",
                Ui.Field("The half that is worked in",
                    Ui.Segmented(new[] { "Above the diagonal", "Below it" },
                        style.UpperIsLive ? "Above the diagonal" : "Below it",
                        picked =>
                        {
                            style.UpperIsLive = picked != "Below it";
                            onChanged();
                        }),
                    "Which triangle you fill in. A pair is written to both cells either way, so "
                    + "this changes what is drawn and never what is built."),

                Ui.Toggle("The dead half echoes what the live half says", style.Echo, on =>
                {
                    style.Echo = on;
                    onChanged();
                }),

                Ui.Toggle("The diagonal has a colour of its own", style.Spine, on =>
                {
                    style.Spine = on;
                    onChanged();
                })));

            if (style.Spine)
                into.Children.Add(Bench.Group("The diagonal",
                    Colours(style.SpineColour, colour =>
                    {
                        style.SpineColour = colour;
                        onChanged();
                    })));

            into.Children.Add(Bench.Group("A pair ruled out",
                Colours(style.RuledColour, colour =>
                {
                    style.RuledColour = colour;
                    onChanged();
                })));

            into.Children.Add(Bench.Group("What a line shows beside its name",
                Ui.Field("Number",
                    Fielded(style.Numbers, at => { style.Numbers = at; onChanged(); }),
                    "On its own, the number column is drawn only when the numbers say something "
                    + "the index gutter beside them does not."),

                Ui.Field("Discipline code",
                    Fielded(style.Codes, at => { style.Codes = at; onChanged(); }),
                    "On its own, the code column is drawn only when a code is not already the "
                    + "front of its own name."),

                Ui.Field("The mark on a line that points at nothing",
                    Fielded(style.Warnings, at => { style.Warnings = at; onChanged(); }),
                    "On its own, the room is kept only when some line on that axis needs it."),

                Ui.Small("The name is always drawn. A line with nothing to call it is not a line "
                         + "anybody can act on.")));

            into.Children.Add(Ui.Small("What one symbol means and what colour it is belong to that "
                                       + "symbol: they are on the palette, under the pencil beside "
                                       + "the plus."));
        }

        /// <summary>The three-way switch a field setting is.</summary>
        /// <param name="now">What it is set to.</param>
        /// <param name="onPicked">Called with the new setting.</param>
        private static UIElement Fielded(MatrixField now, Action<MatrixField> onPicked)
        {
            const string auto = "When it says something";
            const string shown = "Always";
            const string hidden = "Never";

            return Ui.Segmented(new[] { auto, shown, hidden },
                now == MatrixField.Shown ? shown : now == MatrixField.Hidden ? hidden : auto,
                picked => onPicked(picked == shown ? MatrixField.Shown
                    : picked == hidden ? MatrixField.Hidden
                    : MatrixField.Auto));
        }

        /// <summary>
        /// A strip of colours to choose from, and the way back to letting the tool choose.
        ///
        /// Ten and no more, and no mixer. A wash behind a letter has one job - to be told apart
        /// from the wash beside it - and a full colour picker on that job produces two greens a
        /// shade apart and a matrix nobody can read. The ten are separated by hue and all pale
        /// enough to carry the letter at full contrast.
        /// </summary>
        /// <param name="chosen">The colour now, or empty for the tool's own.</param>
        /// <param name="onPicked">Called with the colour, or empty to go back to automatic.</param>
        internal static UIElement Colours(string chosen, Action<string> onPicked)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 1, 0, 0) };

            row.Children.Add(One(string.Empty, chosen.Length == 0, onPicked));

            foreach (var wash in Washes)
                row.Children.Add(One(wash,
                    string.Equals(wash, chosen, StringComparison.OrdinalIgnoreCase), onPicked));

            return row;
        }

        /// <summary>One colour on that strip.</summary>
        /// <param name="colour">The colour, or empty for the automatic one.</param>
        /// <param name="on">True when it is the one in use.</param>
        /// <param name="onPicked">Called when it is pressed.</param>
        private static UIElement One(string colour, bool on, Action<string> onPicked)
        {
            var swatch = new Border
            {
                Width = 30,
                Height = 24,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(on ? 2 : 1),
                Margin = new Thickness(0, 0, 5, 5),
                Cursor = Cursors.Hand,
                ToolTip = colour.Length == 0
                    ? "Automatic — whichever colour the tool would pick"
                    : colour,
            };

            swatch.Tint(Border.BorderBrushProperty, on ? "Accent" : "BdStrong");

            if (colour.Length == 0)
            {
                swatch.Tint(Border.BackgroundProperty, "Card");

                swatch.Child = new TextBlock
                {
                    Text = "auto",
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Hint");
            }
            else
            {
                try
                {
                    swatch.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                            .ConvertFromString(colour));
                }
                catch (Exception)
                {
                    swatch.Tint(Border.BackgroundProperty, "Nav");
                }
            }

            swatch.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
            {
                e.Handled = true;
                onPicked(colour);
            });

            System.Windows.Automation.AutomationProperties.SetName(
                swatch, swatch.ToolTip as string ?? colour);

            swatch.Focusable = true;

            swatch.KeyDown += (s, e) => Ui.Safely(() =>
            {
                if (e.Key != Key.Space && e.Key != Key.Return && e.Key != Key.Enter) return;

                e.Handled = true;
                onPicked(colour);
            });

            return swatch;
        }
    }
}
