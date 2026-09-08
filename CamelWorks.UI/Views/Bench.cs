using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The app anatomy every tab renders to. See <c>CAMELWORKS_DESIGN.md</c> §3.
    ///
    /// One shape everywhere: the data fills the window and lands first; the options sit in a compact
    /// right-hand panel, grouped and all visible; and the one action sits bottom-right in a footer
    /// bar every app shares, with its progress beside it. Read the data, set the options, press the
    /// action.
    ///
    /// Built from the design-system primitives in <see cref="Ui"/> and painted by <see cref="Skin"/>
    /// keys. There are no folds in this anatomy: an option that seems to deserve hiding is either a
    /// default that should state itself or clutter to delete.
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
        /// <param name="setupShare">
        /// The share of the width the options panel takes, between nought and one, for a screen
        /// where the panel holds the thing being edited rather than settings beside a table. Zero
        /// keeps the fixed width, which is what every other surface wants.
        /// </param>
        public static UIElement Surface(UIElement? setup, UIElement? foot, UIElement stage,
                                        double setupWidth = 0, bool scrollStage = true,
                                        double setupShare = 0)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));

            var width = setupWidth > 0 ? setupWidth : SetupWidth;

            // A share rather than a width, for a screen where the panel is the work rather than
            // the settings the work is spent on. A fixed panel is right when it holds a handful of
            // fields beside a table; it is wrong when what is in it is the thing being edited, and
            // it does not grow with the window.
            var shared = setupShare > 0 && setupShare < 1;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var body = new Grid();

            body.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(shared ? 1 - setupShare : 1, GridUnitType.Star),
                MinWidth = shared ? 260 : 0,
            });

            // The grip's own column, so a shared surface can be dragged. Zero wide and empty on
            // a fixed panel, where there is nothing to trade.
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            body.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = shared
                    ? new GridLength(setupShare, GridUnitType.Star)
                    : GridLength.Auto,
                MinWidth = shared ? 300 : 0,
            });

            UIElement data = stage;

            if (scrollStage)
            {
                var staged = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = stage,
                    Padding = new Thickness(16, 12, 16, 16),
                };

                Ui.WheelThrough(staged);

                data = staged;
            }

            var ground = new Border { Child = data };
            ground.Tint(Border.BackgroundProperty, "Bg");
            body.Children.Add(ground);

            if (setup != null)
            {
                var sidebar = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = setup,
                    Padding = new Thickness(14, 12, 14, 12),
                };

                Ui.WheelThrough(sidebar);

                var options = new Border
                {
                    Width = shared ? double.NaN : width,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Child = sidebar,
                };

                options.Tint(Border.BackgroundProperty, "Pane");
                options.Tint(Border.BorderBrushProperty, "Bd");

                if (shared)
                {
                    // Two star columns, so what one takes the other gives. A preview line while
                    // dragging rather than a live re-layout, for the same reason as the split
                    // bench: both sides re-measure on every pixel otherwise.
                    var grip = new GridSplitter
                    {
                        Width = 4,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                        ShowsPreview = true,
                    };

                    grip.Tint(GridSplitter.BackgroundProperty, "Bd");
                    Grid.SetColumn(grip, 1);
                    body.Children.Add(grip);
                }

                Grid.SetColumn(options, 2);
                body.Children.Add(options);
            }

            root.Children.Add(body);

            if (foot != null)
            {
                // The footer: the same bar in every app, with the primary on the right.
                //
                // A footer built by Ui.Foot lays itself out across the whole width - the primary
                // docked right, the reason taking what is left - so it is given the width rather
                // than right-aligned inside a box. Anything else is a Runner or a row of buttons
                // that sizes to its content, and is still pushed right.
                //
                // No maximum. It was capped at 560, which is half the window: the Filter screen's
                // footer came to 472 pixels of buttons, so the primary wrapped onto a second line
                // and the reason had to be shortened to fit. The reason is the part that varies,
                // and Ui.Foot wraps it in place without moving the buttons.
                var footer = new Border
                {
                    Padding = new Thickness(16, 9, 16, 10),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Child = new ContentControl
                    {
                        Content = foot,
                        HorizontalAlignment = Spread(foot)
                            ? HorizontalAlignment.Stretch
                            : HorizontalAlignment.Right,
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
        /// Whether a footer lays itself out across the width it is given.
        ///
        /// A <see cref="Ui.Foot"/> row does: it docks the primary right and gives the reason the
        /// rest. A runner column carrying one is the same thing with a progress bar under it.
        /// Anything else sizes to its content and is pushed right instead.
        /// </summary>
        /// <param name="foot">The footer element.</param>
        private static bool Spread(UIElement foot)
        {
            switch (foot)
            {
                case DockPanel _:
                    return true;

                // Through the holder to what it holds. A screen that redraws its footer in place
                // keeps it in a ContentControl and sets the row as its content, which is the
                // ordinary pattern here — and the holder is not a DockPanel, so the footer was
                // sized to its content and pushed right. Ui.Foot docks one end left and the other
                // right, so with no width to work in the two ends were arranged on top of each
                // other: "0 tests in this project" under "There is no test to run."
                case ContentControl held:
                    return held.Content is UIElement inside && Spread(inside);

                case Border edged:
                    return edged.Child != null && Spread(edged.Child);

                case StackPanel column:
                    return column.Orientation == Orientation.Vertical
                        && column.Children.OfType<DockPanel>().Any();

                default:
                    return false;
            }
        }

        /// <summary>
        /// The other anatomy: a strip, two tables side by side, and one action row.
        ///
        /// The setup-column surface suits a tool that is configured and then run once. It is wrong
        /// for a bench worked in for an afternoon: the options column becomes a wall of every field
        /// the tool has, and the two tables it exists to serve get half the window between them.
        /// Here the tables have the window, the filters used continuously sit in a strip above them,
        /// and everything set up once lives behind a button on the action row.
        ///
        /// The panes are draggable: reading long test names wants a wide left pane, and working
        /// results wants it narrow.
        /// </summary>
        /// <param name="strip">The always-on filters, above both panes.</param>
        /// <param name="left">The master pane, usually built with <see cref="Pane"/>.</param>
        /// <param name="right">The detail pane.</param>
        /// <param name="foot">The action row, laid out left to right rather than right-aligned.</param>
        /// <param name="leftWidth">How wide the master pane starts.</param>
        /// <param name="leftShut">
        /// True to give the master pane no room at all. A screen whose detail pane is the work —
        /// a board of results being read — gets the whole window for it, and the pane comes back
        /// when the reader wants to choose something else.
        /// </param>
        /// <param name="onFolder">
        /// Handed a way to fold the left pane away and bring it back, for a screen with a control
        /// that does that. Without it the pane's state is settled when the split is built, and
        /// the only way to change it is to build the screen again.
        /// </param>
        public static UIElement Split(UIElement? strip, UIElement left, UIElement right,
                                      UIElement? foot, double leftWidth = 380,
                                      bool leftShut = false,
                                      Action<Action<bool>>? onFolder = null)
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
            body.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(leftShut ? 0 : leftWidth),
                MinWidth = leftShut ? 0 : 220,
            });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

            // Collapsed, not merely narrow. A pane given no width still measures and arranges
            // everything in it, which is the cost of a list nobody can see and, on the layout
            // reader, fifteen of its own children reported as overlapping in a column zero
            // pixels wide.
            if (leftShut && left is FrameworkElement folded)
                folded.Visibility = Visibility.Collapsed;

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

            // Nothing to drag between when one side has no width, and a grip against the window
            // edge reads as a scrollbar that does not scroll.
            if (leftShut) grip.Visibility = Visibility.Collapsed;

            Grid.SetColumn(grip, 1);
            body.Children.Add(grip);

            // The same three moves the build above makes, as something the screen can call. The
            // fold used to be done by asking the shell to show the tab again, which does nothing
            // when the tab is already the one on screen - so the button did nothing.
            onFolder?.Invoke(shut =>
            {
                body.ColumnDefinitions[0].Width = new GridLength(shut ? 0 : leftWidth);
                body.ColumnDefinitions[0].MinWidth = shut ? 0 : 220;

                if (left is FrameworkElement one)
                    one.Visibility = shut ? Visibility.Collapsed : Visibility.Visible;

                grip.Visibility = shut ? Visibility.Collapsed : Visibility.Visible;
            });

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
                if (say is FrameworkElement line) line.VerticalAlignment = VerticalAlignment.Center;

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
                var scroller = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = body,
                    Padding = new Thickness(10, 4, 10, 8),
                };

                Ui.WheelThrough(scroller);

                under = scroller;
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
        /// A pane body whose rows scroll and whose verbs do not.
        ///
        /// Three benches needed this and each derived it: Exchange docked the verbs and made the
        /// table the filling child; the Create tab put the table inside a second scroller sized
        /// to the pane, which measured it against a height it did not use and drew the rows at
        /// the foot of an otherwise empty pane.
        ///
        /// A dock, not a grid with a scroller in it. A table here is a <see cref="ListView"/> and
        /// scrolls itself; giving it a scroller of its own is the fault, not the fix.
        ///
        /// Hand the result to <see cref="Pane"/> with <c>scrolls: false</c> — the pane must not
        /// wrap this in a scroller either.
        ///
        /// Where the verbs come from <see cref="Ui.Picking(ListView, Ui.OnPicked[])"/>, build
        /// them first: that returns the table stacked above its own bar, so it has to be taken
        /// back out with <see cref="Ui.Loose{T}"/> before it is handed here as the rows.
        /// </summary>
        /// <param name="rows">The table, which fills what the verbs leave.</param>
        /// <param name="verbs">The bar under it, or null when there is none.</param>
        /// <param name="said">A line or two above the table — a log, a count — or null.</param>
        public static UIElement Listing(UIElement rows, UIElement? verbs, UIElement? said = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var body = new DockPanel { LastChildFill = true };

            if (verbs != null)
            {
                var pinned = Ui.Loose(verbs);

                DockPanel.SetDock(pinned, Dock.Bottom);
                body.Children.Add(pinned);
            }

            if (said != null)
            {
                var top = Ui.Loose(said);

                DockPanel.SetDock(top, Dock.Top);
                body.Children.Add(top);
            }

            // The table fills the pane rather than stopping at two thirds of it: on a bench the
            // table is what the pane is for.
            Ui.SetFills(body, true);

            body.Children.Add(Ui.Loose(rows));

            return body;
        }

        /// <summary>
        /// The same shape, filled into a pane body the screen redraws in place.
        ///
        /// A screen that rebuilds one pane holds its body as a field and hands it to
        /// <see cref="Pane"/> once, so it needs the dock filled rather than returned.
        /// </summary>
        /// <param name="body">The pane body, cleared first.</param>
        /// <param name="rows">The table, which fills what the docked children leave.</param>
        /// <param name="verbs">The bar under it, or null when there is none.</param>
        /// <param name="under">
        /// What lands under the verb bar — a note, a mapping, a line about what was left out.
        /// Docked before the verbs, because a dock stacks inward from the edge.
        /// </param>
        /// <param name="over">What lands above the table, or null.</param>
        public static void Listed(DockPanel body, UIElement rows, UIElement? verbs,
                                  UIElement? under = null, UIElement? over = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            body.Children.Clear();
            body.LastChildFill = true;

            if (under != null)
            {
                var below = Ui.Loose(under);

                DockPanel.SetDock(below, Dock.Bottom);
                body.Children.Add(below);
            }

            if (verbs != null)
            {
                var bar = Ui.Loose(verbs);

                DockPanel.SetDock(bar, Dock.Bottom);
                body.Children.Add(bar);
            }

            if (over != null)
            {
                var top = Ui.Loose(over);

                DockPanel.SetDock(top, Dock.Top);
                body.Children.Add(top);
            }

            Ui.SetFills(body, true);

            body.Children.Add(Ui.Loose(rows));
        }

        /// <summary>
        /// A group of options under a small-caps label.
        ///
        /// The label carries the meaning a card's chrome used to pretend to; the fields are always
        /// visible. Null children are skipped so callers can include a row conditionally.
        /// </summary>
        /// <param name="label">The group's label. Empty for the first, unlabelled group.</param>
        /// <param name="fields">The fields, in reading order.</param>
        public static UIElement Group(string label, params UIElement?[] fields) =>
            Group(label, false, fields);

        /// <summary>
        /// The same group, with the fields laid out left to right.
        ///
        /// A strip above two panes has the window's width and one line of height, so its fields
        /// run across; the options column has the opposite shape and stacks them. Both are the
        /// same group under the same label, so they are one control rather than a second copy of
        /// the label rules per surface.
        /// </summary>
        /// <param name="label">The group's label. Empty for the first, unlabelled group.</param>
        /// <param name="across">
        /// True for a strip: the fields wrap left to right and the group carries the trailing gap
        /// that separates it from the next one. False for the options column.
        /// </param>
        /// <param name="fields">The fields, in reading order.</param>
        public static UIElement Group(string label, bool across, params UIElement?[] fields)
        {
            var stack = new StackPanel
            {
                Margin = across ? new Thickness(0, 0, 28, 0) : new Thickness(0, 0, 0, 14),
            };

            if (!string.IsNullOrEmpty(label))
            {
                // A heading, not an eyebrow. At 11px bold it was smaller than the 12px labels
                // beneath it, so "What is checked" did not read as governing the three fields
                // under it.
                var heading = Ui.Grouping(label);
                heading.Margin = new Thickness(0, 0, 0, 8);
                stack.Children.Add(heading);
            }

            var row = across ? new WrapPanel() : null;

            foreach (var field in fields)
            {
                if (field == null) continue;

                if (field is FrameworkElement fe && fe.Margin == default)
                    fe.Margin = across ? new Thickness(0, 0, 14, 0) : new Thickness(0, 0, 0, 8);

                // Loose, for the same reason Ui.Stack does it: a group rebuilt around a
                // control the screen keeps in a field must not be told it already has a parent.
                if (row != null) row.Children.Add(Ui.Loose(field));
                else stack.Children.Add(Ui.Loose(field));
            }

            if (row != null) stack.Children.Add(row);

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
            // The heading primitive, not a size of its own. This was a hard-coded 16 that
            // happened to match the Head style, so the two drifted apart the first time the
            // scale moved.
            var line = Ui.Heading(text ?? string.Empty);

            line.Margin = new Thickness(0, 0, 0, 8);

            if (tone != Tone.Plain) line.Foreground = Skin.Ink(tone);

            return line;
        }
    }
}
