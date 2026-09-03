using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
        /// The other anatomy: a strip, two tables side by side, and one action row.
        ///
        /// <b>Why a second shape exists at all.</b> The setup-column surface is right for a tool
        /// that is configured and then run once. It is wrong for a bench somebody works in for an
        /// afternoon: the options column becomes a wall of every field the tool has, and the two
        /// tables it exists to serve get half the window between them. Here the tables have the
        /// window, the filters that are touched every minute sit in a strip above them, and
        /// everything set up once lives behind a button on the action row.
        ///
        /// The panes are draggable: a coordinator reading long test names wants a wide left, and
        /// one working results wants it narrow.
        /// </summary>
        /// <param name="strip">The always-on filters, above both panes.</param>
        /// <param name="left">The master pane, usually built with <see cref="Pane"/>.</param>
        /// <param name="right">The detail pane.</param>
        /// <param name="foot">The action row, laid out left to right rather than right-aligned.</param>
        /// <param name="leftWidth">How wide the master pane starts.</param>
        public static UIElement Split(UIElement? strip, UIElement left, UIElement right,
                                      UIElement? foot, double leftWidth = 380)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            if (strip != null)
            {
                var band = new Border
                {
                    Padding = new Thickness(14, 8, 14, 8),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = strip,
                };

                band.Tint(Border.BackgroundProperty, "Card");
                band.Tint(Border.BorderBrushProperty, "Bd");
                root.Children.Add(band);
            }

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWidth), MinWidth = 220 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

            body.Children.Add(left);

            var grip = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,

                // A preview line while dragging, and the columns move once on release.
                //
                // Live, every pixel of the drag re-measures and re-arranges both panes - and the
                // right-hand one is a results table whose columns are shares of its width, so
                // every realised row is laid out again from scratch. That is a window that
                // visibly trails the cursor. The preview costs one line of pixels and the panes
                // land exactly where they were dropped.
                ShowsPreview = true,
            };

            grip.Tint(GridSplitter.BackgroundProperty, "Bd");
            Grid.SetColumn(grip, 1);
            body.Children.Add(grip);

            Grid.SetColumn(right, 2);
            body.Children.Add(right);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            if (foot != null)
            {
                var footer = new Border
                {
                    // Tight. The footer is a strip of buttons and one line of counts, and
                    // every pixel of it is a row of the table nobody can see.
                    Padding = new Thickness(14, 4, 14, 4),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Child = foot,
                };

                footer.Tint(Border.BackgroundProperty, "Pane");
                footer.Tint(Border.BorderBrushProperty, "Bd");

                Grid.SetRow(footer, 2);
                root.Children.Add(footer);
            }

            var ground = new Border { Child = root };
            ground.Tint(Border.BackgroundProperty, "Bg");

            return ground;
        }

        /// <summary>
        /// One pane of a <see cref="Split"/>: a label, the table, and the table's own verbs.
        ///
        /// The verbs belong under the rows they act on. A single bar of verbs serving two tables
        /// is a bar where half the buttons are wrong for whatever was last clicked.
        /// </summary>
        /// <param name="label">Small-caps pane label.</param>
        /// <param name="say">A live line beside the label — what is picked, how many are shown.</param>
        /// <param name="body">The table, scrolled by the pane.</param>
        /// <param name="tools">The icon row under it.</param>
        /// <param name="scrolls">
        /// False when the body scrolls itself.
        ///
        /// A scroller inside a scroller is given unbounded height to measure in, so it is never
        /// shorter than its own content and therefore never has anything to scroll: the wheel
        /// falls through to the outer one and the inner scrollbars never appear. A pane whose
        /// body is one big table - the clash matrix - has to keep its own scroller and be given
        /// the height of the pane to work in.
        /// </param>
        public static UIElement Pane(string label, UIElement? say, UIElement body, UIElement? tools,
                                     bool scrolls = true)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // The label and the verbs share one row: the label on the left, the verbs hard right.
            //
            // They used to sit in a bar under the table, which put the two panes' verb rows at the
            // bottom of a tall window - a long way from the rows they act on, and below whatever
            // empty space the table left. At the top they are beside the count of what is picked
            // and directly over the rows, which is where the hand already is.
            var head = new DockPanel { LastChildFill = true };

            if (tools != null)
            {
                if (tools is FrameworkElement bar)
                {
                    bar.VerticalAlignment = VerticalAlignment.Center;
                    bar.HorizontalAlignment = HorizontalAlignment.Right;
                }

                DockPanel.SetDock(tools, Dock.Right);
                head.Children.Add(tools);
            }

            var named = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            named.Children.Add(Ui.Eyebrow(label));

            if (say != null)
            {
                named.Children.Add(new Border { Width = 10 });
                named.Children.Add(say);
            }

            // Clipped, so a long "say" cannot draw over the verbs.
            //
            // A DockPanel does not clip its children: the verbs dock right and the rest fills
            // what is left, but a wider thing in that fill simply paints on past its edge and
            // over the buttons - which is what "the option buttons overlap each other" was.
            var left = new Border { Child = named, ClipToBounds = true };

            head.Children.Add(left);

            var header = new Border
            {
                Padding = new Thickness(12, 4, 8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = head,
            };

            header.Tint(Border.BackgroundProperty, "Card2");
            header.Tint(Border.BorderBrushProperty, "Bd");
            grid.Children.Add(header);

            FrameworkElement under;

            if (scrolls)
            {
                under = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = body,
                    Padding = new Thickness(10, 4, 10, 8),
                };
            }
            else
            {
                under = new Border
                {
                    Child = body,
                    Padding = new Thickness(10, 4, 10, 8),
                };
            }

            var scroll = under;

            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // A table on a bench fills its pane. Everywhere else a table is capped at two thirds
            // of the surface it is one part of, which is right when a stage stacks several things
            // - and wrong here, where the table is the whole pane and the cap left a third of it
            // white with the rows still scrolling.
            //
            // Said on the pane rather than done to the tables in it. The property is inherited, so
            // it reaches a table drawn an hour later as surely as one that was there at build
            // time - which the walk this replaced did not: reading a board replaces the results
            // table, and the replacement came back capped with the empty third underneath it.
            Ui.SetFills(scroll, true);

            var pane = new Border { Child = grid };
            pane.Tint(Border.BackgroundProperty, "Card");

            return pane;
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

                // Loose, for the same reason Ui.Stack does it: a group rebuilt around a
                // control the screen keeps in a field must not be told it already has a parent.
                stack.Children.Add(Ui.Loose(field));
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
