using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// How the matrix is drawn, exposed as settings.
    ///
    /// Each of these was previously a fixed decision inside the drawing code: which triangle is the
    /// live one, whether the unused half echoes, what colour a clearance is, whether the code gutter
    /// is present. All defensible, none universal. A practice that has drawn its matrices in the
    /// lower triangle for fifteen years should not be told its sheet is inverted, and one whose set
    /// names already carry the discipline code does not want the column.
    ///
    /// So they are asked here, in one window, and saved with the matrix rather than with the seat,
    /// because they are properties of the matrix that gets issued rather than of the machine it was
    /// last opened on. None of them changes a test: a pair is written to both cells whichever half
    /// it was entered in, and a hidden number is still the number the test is named after.
    ///
    /// Everything here applies to the whole grid. What one symbol means and what colour it is are
    /// settings of that symbol and live on the palette with it.
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

            // Modeless, and offset from the grid. Every choice here is judged by looking at the
            // grid behind it, which is why it has no Cancel, and it was previously opened modal and
            // centred on the owner, covering the thing it is about. It now sits to the right of the
            // window it changes and the grid stays live under it, so a colour is chosen by watching
            // the cells take it.
            var window = new Window
            {
                Title = "CamelWorks - How the matrix is drawn",
                Width = 480,
                Height = 620,
                MinWidth = 400,
                MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
            };

            // Off to the right of whatever screen it opened on, so the grid it changes stays in
            // view. Its own size is not known until it is measured, so this is worked from the
            // work area rather than from the window.
            var room = SystemParameters.WorkArea;

            window.Left = Math.Max(room.Left, room.Right - window.Width - 32);
            window.Top = room.Top + 72;

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

            // Done closes the window. Every setting on it is written as it is changed, so a
            // filled primary here would be the loudest control on the screen committing nothing.
            var done = Ui.Button("Done", () => window.Close());

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

            // Shown, not shown as a dialogue: a modal window disables the frame behind it, and
            // the frame behind it is the matrix these choices are about.
            window.Show();
        }

        /// <summary>Draw the whole window's contents, which happens again after every change.</summary>
        /// <param name="into">The panel to fill.</param>
        /// <param name="style">How it is drawn now.</param>
        /// <param name="onChanged">Called after anything changes.</param>
        private static void Fill(Panel into, MatrixStyle style, Action onChanged)
        {
            into.Children.Clear();

            into.Children.Add(Grouped("The grid",
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
                into.Children.Add(Grouped("The diagonal",
                    Colours(style.SpineColour, colour =>
                    {
                        style.SpineColour = colour;
                        onChanged();
                    })));

            into.Children.Add(Grouped("A pair ruled out",
                Colours(style.RuledColour, colour =>
                {
                    style.RuledColour = colour;
                    onChanged();
                })));

            into.Children.Add(Grouped("What a line shows beside its name",
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

                Ui.Small("The name is always drawn; an unnamed line cannot be identified.")));

            into.Children.Add(Ui.Small("What one symbol means and what colour it is belong to that "
                                       + "symbol: they are on the palette, under the pencil beside "
                                       + "the plus."));
        }

        /// <summary>
        /// A group of settings under a label, in sentence case.
        ///
        /// <see cref="Bench.Group"/> runs its label through <see cref="Ui.Eyebrow"/>, which
        /// upper-cases it. It is the same layout otherwise; the fix belongs in the shared file.
        /// </summary>
        /// <param name="label">The group's label.</param>
        /// <param name="fields">The fields, in reading order.</param>
        private static UIElement Grouped(string label, params UIElement?[] fields)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            var said = new TextBlock { Text = label ?? string.Empty }.Wearing("Caps");

            said.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(said);

            foreach (var field in fields)
            {
                if (field == null) continue;

                if (field is FrameworkElement held && held.Margin == default)
                    held.Margin = new Thickness(0, 0, 0, 8);

                stack.Children.Add(Ui.Loose(field));
            }

            return stack;
        }

        /// <summary>The three-way switch a field setting is.</summary>
        /// <param name="now">What it is set to.</param>
        /// <param name="onPicked">Called with the new setting.</param>
        private static UIElement Fielded(MatrixDrawn now, Action<MatrixDrawn> onPicked)
        {
            const string auto = "When it says something";
            const string shown = "Always";
            const string hidden = "Never";

            return Ui.Segmented(new[] { auto, shown, hidden },
                now == MatrixDrawn.Shown ? shown : now == MatrixDrawn.Hidden ? hidden : auto,
                picked => onPicked(picked == shown ? MatrixDrawn.Shown
                    : picked == hidden ? MatrixDrawn.Hidden
                    : MatrixDrawn.Auto));
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

            // The strip marks what is chosen, so the strip re-marks its own swatches. This was left
            // to whoever opened it to redraw afterwards, and neither caller did: the legend's form
            // rebuilds from its third child down and keeps this one, and the drawing window rebuilds
            // the whole page from inside this swatch's own click, which removes the swatch while its
            // event is still running. Between them a press moved the ring in neither, so the strip
            // read as inert.
            var swatches = new System.Collections.Generic.List<Action<string>>();

            void Press(string colour)
            {
                foreach (var mark in swatches) mark(colour);

                // After this click has finished routing, not during it. A caller is free to
                // rebuild its whole page in response - the drawing window does - and a rebuild
                // that runs inside the handler removes the element the handler belongs to.
                row.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() => Ui.Safely(() => onPicked(colour))));
            }

            var first = One(string.Empty, chosen.Length == 0, Press, out var marked);

            swatches.Add(marked);
            row.Children.Add(first);

            foreach (var wash in Washes)
            {
                var one = One(wash, string.Equals(wash, chosen, StringComparison.OrdinalIgnoreCase),
                              Press, out var mark);

                swatches.Add(mark);
                row.Children.Add(one);
            }

            return row;
        }

        /// <summary>One colour on that strip.</summary>
        /// <param name="colour">The colour, or empty for the automatic one.</param>
        /// <param name="on">True when it is the one in use.</param>
        /// <param name="onPicked">Called when it is pressed.</param>
        /// <param name="marked">Given back: call it with the colour now chosen to re-mark this one.</param>
        private static UIElement One(string colour, bool on, Action<string> onPicked,
                                     out Action<string> marked)
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

            var chosen = on;

            void Ring()
            {
                swatch.BorderThickness = new Thickness(chosen ? 2 : 1);
                swatch.Tint(Border.BorderBrushProperty, chosen ? "Accent" : "BdStrong");
            }

            marked = now =>
            {
                chosen = string.Equals(now, colour, StringComparison.OrdinalIgnoreCase);
                Ring();
            };

            Ring();

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

            // A Border has no style, so it draws nothing at all when the keyboard reaches it.
            swatch.GotKeyboardFocus += (s, e) =>
            {
                swatch.BorderThickness = new Thickness(2);
                swatch.Tint(Border.BorderBrushProperty, "Accent");
            };

            swatch.LostKeyboardFocus += (s, e) => Ring();

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
