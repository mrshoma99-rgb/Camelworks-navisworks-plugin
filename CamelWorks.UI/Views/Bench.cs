using System;
using System.Windows;
using System.Windows.Controls;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The surface anatomy every tool is built to. See <c>CAMELWORKS_DESIGN.md</c> §3.
    ///
    /// One shape, everywhere: a <b>setup</b> column on the left holding every input, all of them
    /// visible; the tool's <b>one action</b> at the column's foot, in the same place on every
    /// tool; and the <b>stage</b> on the right, where the answer lands and dominates. A tool is a
    /// rail row, an option is a field in setup, a result is on the stage — and nothing else
    /// exists, which is the structural answer to "you don't know what is part of a tool and what
    /// is its own tool".
    ///
    /// Built from the design-system primitives in <see cref="Ui"/> and painted by <see cref="Skin"/>
    /// keys. There are no folds anywhere in this anatomy: an option that seems to deserve hiding
    /// is either a default that should state itself or clutter to delete.
    /// </summary>
    public static class Bench
    {
        /// <summary>How wide the setup column is, unless the surface asks otherwise.</summary>
        public const double SetupWidth = 312;

        /// <summary>Below this width the two columns stack — the pane's shape, not a different screen.</summary>
        public const double NarrowBelow = 620;

        /// <summary>
        /// A surface: setup column, its foot action, and the stage.
        /// </summary>
        /// <param name="setup">The inputs, built from <see cref="Group"/> rows. May be null for a tool with none.</param>
        /// <param name="foot">The one action — usually a <see cref="Ui.Runner"/> panel. May be null while a tool stages its own commit.</param>
        /// <param name="stage">The answer's home. Scrolls on its own.</param>
        /// <param name="setupWidth">Setup column width, or zero for the default.</param>
        public static UIElement Surface(UIElement? setup, UIElement? foot, UIElement stage,
                                        double setupWidth = 0)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));

            return new SurfaceHost(setup, foot, stage,
                                   setupWidth > 0 ? setupWidth : SetupWidth);
        }

        /// <summary>
        /// A group of setup fields under a small-caps label.
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
        /// What the action will read, stated at the top of setup from the live model.
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
        /// The stage before the first run: one sentence saying what the action will do, computed
        /// from what is actually loaded, and where the answer will appear.
        /// </summary>
        /// <param name="headline">What pressing the action does, to what.</param>
        /// <param name="detail">One quieter line of consequence, or null.</param>
        public static UIElement Ready(string headline, string? detail = null)
        {
            var stack = new StackPanel
            {
                MaxWidth = 460,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 18, 0, 0),
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
        /// The result's first line: the whole outcome in one phrase, largest thing on the stage.
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

        /// <summary>
        /// The two columns, and the stacking below <see cref="NarrowBelow"/>.
        ///
        /// One control rather than two layouts a screen picks from, because an element can only
        /// live in one tree: on a size change the three parts are re-parented, never rebuilt, so
        /// a half-filled form survives docking the pane narrower.
        /// </summary>
        private sealed class SurfaceHost : ContentControl
        {
            private readonly UIElement? _setup;
            private readonly UIElement? _foot;
            private readonly UIElement _stage;
            private readonly double _setupWidth;
            private bool? _narrow;

            internal SurfaceHost(UIElement? setup, UIElement? foot, UIElement stage, double setupWidth)
            {
                _setup = setup;
                _foot = foot;
                _stage = stage;
                _setupWidth = setupWidth;

                SizeChanged += (s, e) => Arrange(e.NewSize.Width);
                Loaded += (s, e) => Arrange(ActualWidth);
            }

            private void Arrange(double width)
            {
                var narrow = width > 0 && width < NarrowBelow;
                if (_narrow == narrow) return;

                _narrow = narrow;

                Orphan(_setup);
                Orphan(_foot);
                Orphan(_stage);

                Content = narrow ? Stacked() : Columns();
            }

            private static void Orphan(UIElement? element)
            {
                if (element == null) return;

                if (System.Windows.Media.VisualTreeHelper.GetParent(element) is Panel panel)
                    panel.Children.Remove(element);
                else if (System.Windows.Media.VisualTreeHelper.GetParent(element) is ContentControl content)
                    content.Content = null;
                else if (System.Windows.Media.VisualTreeHelper.GetParent(element) is Border border)
                    border.Child = null;
                else if (System.Windows.Media.VisualTreeHelper.GetParent(element) is ScrollViewer scroll)
                    scroll.Content = null;
            }

            private UIElement Columns()
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_setupWidth) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                grid.Children.Add(SetupColumn(pinFoot: true));

                var stage = StageArea();
                Grid.SetColumn(stage, 1);
                grid.Children.Add(stage);

                return grid;
            }

            private UIElement Stacked()
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var setup = SetupColumn(pinFoot: false);
                grid.Children.Add(setup);

                var stage = StageArea();
                Grid.SetRow(stage, 1);
                grid.Children.Add(stage);

                return grid;
            }

            /// <summary>
            /// The setup column: inputs scrolling, the action pinned at the foot.
            ///
            /// Pinned in the wide shape only. Stacked, the whole column is short and the foot
            /// follows the fields, because pinning it would let a long form push the stage off
            /// the pane entirely.
            /// </summary>
            private UIElement SetupColumn(bool pinFoot)
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = pinFoot ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
                });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                if (_setup != null)
                {
                    var fields = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = _setup,
                        Padding = new Thickness(14, 12, 14, 6),
                    };

                    // Stacked, the fields must not take the pane: the stage below them is the
                    // point of the surface.
                    if (!pinFoot) fields.MaxHeight = 340;

                    grid.Children.Add(fields);
                }

                if (_foot != null)
                {
                    var foot = new Border
                    {
                        Padding = new Thickness(14, 10, 14, 12),
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Child = _foot,
                    };

                    foot.Tint(Border.BorderBrushProperty, "Bd");
                    Grid.SetRow(foot, 1);
                    grid.Children.Add(foot);
                }

                var column = new Border
                {
                    Child = grid,
                    BorderThickness = _narrow == true
                        ? new Thickness(0, 0, 0, 1)
                        : new Thickness(0, 0, 1, 0),
                };

                column.Tint(Border.BackgroundProperty, "Pane");
                column.Tint(Border.BorderBrushProperty, "Bd");
                return column;
            }

            private UIElement StageArea()
            {
                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _stage,
                    Padding = new Thickness(16, 12, 16, 16),
                };

                var ground = new Border { Child = scroll };
                ground.Tint(Border.BackgroundProperty, "Bg");
                return ground;
            }
        }
    }
}
