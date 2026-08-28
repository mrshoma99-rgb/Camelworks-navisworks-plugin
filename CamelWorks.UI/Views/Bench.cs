using System;
using System.Windows;
using System.Windows.Controls;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The app anatomy every tab renders to. See <c>CAMELWORKS_DESIGN.md</c> §3.
    ///
    /// One shape, everywhere, and it is the shape of the suites the client named: the <b>data</b>
    /// fills the window and lands first; the <b>options</b> sit in a compact right-hand panel,
    /// grouped and all visible; and the <b>one action</b> sits bottom-right in a footer bar every
    /// app shares, with its progress beside it. Read the data, set the options, press the big
    /// button — the contract a coordinator already knows from every good dialog they use.
    ///
    /// Built from the design-system primitives in <see cref="Ui"/> and painted by <see cref="Skin"/>
    /// keys. There are no folds anywhere in this anatomy: an option that seems to deserve hiding
    /// is either a default that should state itself or clutter to delete.
    /// </summary>
    public static class Bench
    {
        /// <summary>How wide the options panel is, unless the surface asks otherwise.</summary>
        public const double SetupWidth = 300;

        /// <summary>
        /// A surface: the data area, the options panel, and the footer action.
        /// </summary>
        /// <param name="setup">The options, built from <see cref="Group"/> rows, shown on the right. Null for a tab with none.</param>
        /// <param name="foot">The one action — usually a <see cref="Ui.Runner"/> panel — bottom-right. Null while a tab stages its own commit in the data area.</param>
        /// <param name="stage">The data. Fills the window and scrolls on its own.</param>
        /// <param name="setupWidth">Options panel width, or zero for the default.</param>
        /// <param name="scrollStage">
        /// False for the one stage that manages its own scrolling — the graph canvas, which needs
        /// a horizontal bar this wrapper deliberately refuses everything else.
        /// </param>
        public static UIElement Surface(UIElement? setup, UIElement? foot, UIElement stage,
                                        double setupWidth = 0, bool scrollStage = true)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));

            var width = setupWidth > 0 ? setupWidth : SetupWidth;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var data = scrollStage
                ? (UIElement)new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = stage,
                    Padding = new Thickness(16, 12, 16, 16),
                }
                : stage;

            var ground = new Border { Child = data };
            ground.Tint(Border.BackgroundProperty, "Bg");
            body.Children.Add(ground);

            if (setup != null)
            {
                var options = new Border
                {
                    Width = width,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Child = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = setup,
                        Padding = new Thickness(14, 12, 14, 12),
                    },
                };

                options.Tint(Border.BackgroundProperty, "Pane");
                options.Tint(Border.BorderBrushProperty, "Bd");

                Grid.SetColumn(options, 1);
                body.Children.Add(options);
            }

            root.Children.Add(body);

            if (foot != null)
            {
                // The footer: the same bar in every app, with the primary on the right. The foot
                // element is usually a Runner, which carries its own progress and Stop, so the
                // whole of it is right-aligned rather than split.
                var footer = new Border
                {
                    Padding = new Thickness(16, 9, 16, 10),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Child = new ContentControl
                    {
                        Content = foot,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        MaxWidth = 560,
                    },
                };

                footer.Tint(Border.BackgroundProperty, "Pane");
                footer.Tint(Border.BorderBrushProperty, "Bd");

                Grid.SetRow(footer, 1);
                root.Children.Add(footer);
            }

            return root;
        }

        /// <summary>
        /// A group of options under a small-caps label.
        ///
        /// The label carries the meaning a card's chrome used to pretend to; the fields are always
        /// visible. Null children are skipped so callers can include a row conditionally.
        /// </summary>
        /// <param name="label">The group's label. Empty for the first, unlabelled group.</param>
        /// <param name="fields">The fields, in reading order.</param>
        public static UIElement Group(string label, params UIElement?[] fields)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            if (!string.IsNullOrEmpty(label))
            {
                var eyebrow = Ui.Eyebrow(label);
                eyebrow.Margin = new Thickness(0, 0, 0, 6);
                stack.Children.Add(eyebrow);
            }

            foreach (var field in fields)
            {
                if (field == null) continue;

                if (field is FrameworkElement fe && fe.Margin == default)
                    fe.Margin = new Thickness(0, 0, 0, 8);

                stack.Children.Add(field);
            }

            return stack;
        }

        /// <summary>
        /// What the action will read, stated over the data from the live model.
        ///
        /// Never boilerplate: "3,120 elements in the selection" is a scope line; "choose a scope
        /// below" is a failure to derive one.
        /// </summary>
        /// <param name="text">The scope, in one line.</param>
        public static UIElement Scope(string text)
        {
            var line = Ui.Line(text ?? string.Empty, 1, bold: true);
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 0, 0, 12);
            return line;
        }

        /// <summary>
        /// The data area before the first run: one line saying what the action will do, computed
        /// from what is actually loaded — over the empty grid, never a welcome paragraph.
        /// </summary>
        /// <param name="headline">What pressing the action does, to what.</param>
        /// <param name="detail">One quieter line of consequence, or null.</param>
        public static UIElement Ready(string headline, string? detail = null)
        {
            var stack = new StackPanel
            {
                MaxWidth = 520,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 10),
            };

            var head = Ui.Line(headline ?? string.Empty, 0.9, bold: true);
            head.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(head);

            if (!string.IsNullOrEmpty(detail))
            {
                var line = Ui.Line(detail!, 0.7);
                line.TextWrapping = TextWrapping.Wrap;
                line.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(line);
            }

            return stack;
        }

        /// <summary>
        /// The result's first line: the whole outcome in one phrase, largest thing on the data area.
        /// </summary>
        /// <param name="text">The outcome.</param>
        /// <param name="tone">How it reads.</param>
        public static UIElement Verdict(string text, Tone tone = Tone.Plain)
        {
            var line = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = Skin.Ink(tone),
            };

            if (tone == Tone.Plain) line.Tint(TextBlock.ForegroundProperty, "Text");

            return line;
        }
    }
}
