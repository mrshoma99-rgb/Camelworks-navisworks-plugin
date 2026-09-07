using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The fields every line of the matrix carries, and how they are filled in.
    ///
    /// A line held three things somebody could type, so a job filing its tests by a sub-discipline
    /// code, a phase or a package had nowhere to put it. Worse, everything was typed: numbering
    /// forty rows was forty edits and changing where the numbering started was forty more, which
    /// is why a matrix once drawn was never renumbered.
    ///
    /// The window is built around the preview line under each field. Setting a start of 10 and a
    /// step of 10 across forty rows is only safe to press if what it is about to write is on the
    /// screen first, so every field shows the values it would produce before anything is applied.
    ///
    /// A field also shows its token. That is the link to the rest of the matrix: a field called
    /// Phase is <c>{rowPhase}</c> in a naming pattern, so the same list drives what a test is
    /// called and what a read can recover from a name it already has.
    /// </summary>
    public static class MatrixFields
    {
        // The three ways a field is filled, named by what each one does to a line. "By hand",
        // "Counted" and "Same on all" were the window's three central terms and none of them said
        // what it acted on.
        private const string ByHand = "Typed on each line";
        private const string Counted = "Counted down the axis";
        private const string Everywhere = "Same on every line";

        /// <summary>
        /// Show the fields and let them be changed.
        /// </summary>
        /// <param name="board">The board, whose scheme is edited in place.</param>
        /// <returns>True when something changed, so the caller can apply and redraw.</returns>
        internal static bool Ask(MatrixBoard board)
        {
            if (board == null) return false;

            var window = new Window
            {
                Title = "CamelWorks - Fields",
                Width = 620,
                Height = 640,
                MinWidth = 520,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var touched = false;

            var ground = new Border
            {
                Child = Body(board, window, () => touched = true, () => window.Close()),
            };

            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            Skin.Apply(window);
            HostWindow.Own(window);

            window.ShowDialog();

            return touched;
        }

        /// <summary>
        /// The window's content as a panel, so the render harness can capture it.
        ///
        /// A WPF window has to be shown to render and the harness has no screen, so a window-only
        /// surface is one nobody can look at until it is installed on a licensed seat.
        /// </summary>
        /// <param name="board">The board, whose scheme is edited in place.</param>
        public static UIElement Drawn(MatrixBoard board) =>
            Body(board, null, null, null);

        /// <summary>Build the surface.</summary>
        /// <param name="board">The board.</param>
        /// <param name="near">The window a prompt belongs to, or null in the harness.</param>
        /// <param name="onTouched">Called the first time anything is edited.</param>
        /// <param name="onDone">Called by the closing action.</param>
        private static UIElement Body(MatrixBoard board, FrameworkElement? near,
                                      Action? onTouched, Action? onDone)
        {
            var list = Ui.Stack();
            var foot = Ui.Stack();

            void Redraw()
            {
                list.Children.Clear();

                var fields = board.Scheme.Fields.ToList();

                for (var at = 0; at < fields.Count; at++)
                    list.Children.Add(Card(board, fields[at], at, fields.Count,
                                           () => { onTouched?.Invoke(); Redraw(); }));

                foot.Children.Clear();
                foot.Children.Add(Standing(board));
            }

            Redraw();

            var adding = Ui.Quiet("Add a field", () => Ui.Safely(() =>
                Naming(near, board, () => { onTouched?.Invoke(); Redraw(); })));

            adding.HorizontalAlignment = HorizontalAlignment.Left;
            adding.Margin = new Thickness(0, 2, 0, 0);

            // One sentence of orientation. The window opened on two reference paragraphs before
            // any control, one of which sent the reader to a settings page mid-sentence; the rest
            // of what they said is on the tooltip of the control it is about.
            var says = Ui.Small("Every row and column carries these fields, in this order, and "
                                + "each is filled the same way on all of them.");

            says.TextWrapping = TextWrapping.Wrap;

            var help = Ui.Quiet("What these do", () => Ui.Told(
                "A field is one value a row or a column carries: its name, its number, a code, "
                + "or one of your own. The order here is the order the panel lists them in and "
                + "the order the board lays its gutters out in.\n\n"
                + "Counted writes a series down the axis, so renumbering the whole matrix is one "
                + "edit here rather than one per line. A line can be taken out of a field and "
                + "typed by hand from its own header.\n\n"
                + "Where the name, number, code and folder are drawn is a board setting, under "
                + "Settings, Grid, symbols and fields. A field of your own says on its own card."));

            help.HorizontalAlignment = HorizontalAlignment.Left;
            help.Margin = new Thickness(0, 2, 0, 10);

            var head = Ui.Stack(Ui.Heading("Fields"), says, help);

            head.Margin = new Thickness(0, 0, 0, 2);

            var body = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 14, 16, 0) };

            DockPanel.SetDock(head, Dock.Top);
            body.Children.Add(head);

            var under = Ui.Stack(adding, foot);

            under.Margin = new Thickness(0, 8, 0, 0);

            DockPanel.SetDock(under, Dock.Bottom);
            body.Children.Add(under);

            body.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(list),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            // Capped at the width the window opens at, so what the render harness draws at 1180
            // is the layout that ships rather than the same controls stretched across a screen
            // nobody sees them on.
            var root = new DockPanel
            {
                LastChildFill = true,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var acts = new Border
            {
                Margin = new Thickness(16, 10, 16, 14),
                // Done closes the window; it commits nothing, because every card writes as it is
                // edited. A filled primary on a closing action is the loudest control on the
                // screen doing the least.
                Child = Ui.Actions(null, Ui.Button("Done", () => onDone?.Invoke())),
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            DockPanel.SetDock(acts, Dock.Bottom);
            root.Children.Add(acts);
            root.Children.Add(body);

            return root;
        }

        /// <summary>How many lines the scheme is about, said once at the foot.</summary>
        /// <param name="board">The board.</param>
        private static UIElement Standing(MatrixBoard board)
        {
            var pinned = board.Rows.Concat(board.Columns).Count(one => one.IsCustom);

            var said = Ui.Small(
                Ui.Count(board.Rows.Count, "row") + " and "
                + Ui.Count(board.Columns.Count, "column") + " take these fields"
                + (pinned == 0
                    ? "."
                    : "  ·  " + Ui.Count(pinned, "of them")
                      + " keep a value typed by hand, outside these fields."));

            said.TextWrapping = TextWrapping.Wrap;
            said.Margin = new Thickness(0, 8, 0, 0);

            return said;
        }

        /// <summary>One field, as a card.</summary>
        /// <param name="board">The board.</param>
        /// <param name="field">The field.</param>
        /// <param name="at">Where it sits in the list, from zero.</param>
        /// <param name="of">How many fields there are, so the last one cannot move down.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static UIElement Card(MatrixBoard board, MatrixField field, int at, int of,
                                      Action onChange)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 10, 12, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
            };

            card.Tint(Border.BackgroundProperty, "Card2");
            card.Tint(Border.BorderBrushProperty, "Bd");

            var stack = Ui.Stack();

            // Never inline now: the placement row always follows the head, so a head with no
            // gap under it would sit hard against its own ticks.
            stack.Children.Add(Head(board, field, at, of, onChange, inline: false));

            // Where it is drawn, on every card. A field is carried by every line whether it is
            // drawn or not, so this hides one from the screen without taking it off the lines or
            // out of the test names — which is what deleting it would do.
            stack.Children.Add(Placing(field, onChange));

            // A field typed line by line has nothing to configure and nothing to preview, so it
            // carries no filling block and no sentence saying so. Four of those sentences down a
            // window is the state a scheme starts in, and they read as four warnings rather than
            // as the default.
            if (field.Fill != MatrixFill.ByHand)
                stack.Children.Add(Filling(board, field, onChange));

            card.Padding = field.Fill == MatrixFill.ByHand
                ? new Thickness(12, 9, 12, 9)
                : new Thickness(12, 10, 12, 12);

            card.Child = stack;

            return card;
        }

        /// <summary>The card's top line: what it is called, its token, and the way to remove it.</summary>
        private static UIElement Head(MatrixBoard board, MatrixField field, int at, int of,
                                      Action onChange, bool inline)
        {
            // Two rows, not one. The title, the fill chooser and the token shared a line, and at
            // the window's width the token was trimmed to "{row…" on six of seven cards while the
            // chooser lost its selected segment. The chooser is now a row of its own, which is
            // also what lets the three ways of filling a field carry names long enough to say
            // what they do.
            var head = new Grid { Margin = new Thickness(0, 0, 0, inline ? 0 : 8) };

            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Settled, not per keystroke: this card is rebuilt by what it reports.
            var title = Ui.Settled(field.Title, "What it is called", typed =>
            {
                var said = (typed ?? string.Empty).Trim();

                if (said.Length == 0 || said == field.Title) return;

                field.Title = said;
                onChange();
            }, 150);

            title.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(title, 0);
            head.Children.Add(title);

            // One token rather than the pair. Both forms doubled the width for a value that is
            // mechanical once the first is known.
            var token = new TextBlock
            {
                Text = Tokened(field),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "In a naming pattern: " + Tokened(field) + " for the row, "
                          + Tokened(field, column: true) + " for the column.",
            }.Wearing("Mono");

            token.Tint(TextBlock.ForegroundProperty, "Muted");

            Grid.SetColumn(token, 1);
            head.Children.Add(token);

            // The order is the order. It is what the panel lists the boxes in and what the board
            // lays the gutters out in, so moving a card here moves the field in both.
            var moves = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var up = Ui.Quiet("\u2191", () => Ui.Safely(() =>
            {
                if (board.Scheme.Move(field.Key, at - 1)) onChange();
            }));

            up.IsEnabled = at > 0;
            up.ToolTip = "Move it up, in the panel and on the board.";

            var down = Ui.Quiet("\u2193", () => Ui.Safely(() =>
            {
                if (board.Scheme.Move(field.Key, at + 1)) onChange();
            }));

            down.IsEnabled = at < of - 1;
            down.ToolTip = "Move it down, in the panel and on the board.";

            moves.Children.Add(up);
            moves.Children.Add(down);

            // The Remove slot is kept whether or not this field can be removed, so the two arrows
            // sit at one position down the panel. Built-in fields left it out, and every card
            // above one shifted sideways.
            var drop = Ui.Quiet("Remove", () => Ui.Safely(() =>
            {
                board.Scheme.Remove(field.Key, board.Rows.Concat(board.Columns));
                onChange();
            }));

            drop.ToolTip = "Remove this field and every line's value for it. "
                           + "The matrix's own undo takes it back.";

            if (field.IsBuiltIn)
            {
                drop.Visibility = Visibility.Hidden;
                drop.IsHitTestVisible = false;
                drop.ToolTip = null;
            }

            moves.Children.Add(drop);

            Grid.SetColumn(moves, 3);
            head.Children.Add(moves);

            var pick = Picker(field, onChange);

            var filled = Ui.Field("Filled", pick, null);

            filled.HorizontalAlignment = HorizontalAlignment.Left;
            filled.Margin = new Thickness(0, 0, 0, inline ? 0 : 4);

            return Ui.Stack(head, filled);
        }

        /// <summary>
        /// The token a pattern actually carries for this field.
        ///
        /// The four built-in fields had their tokens before the scheme existed, and they are not
        /// built from the key: the name is <c>{row}</c>, not <c>{rowName}</c>. Printing the key's
        /// form put a token on screen that no pattern can parse, beside a tooltip inviting
        /// somebody to write it into one.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="column">True for the column's form.</param>
        private static string Tokened(MatrixField field, bool column = false)
        {
            if (field.Key == MatrixField.NameKey) return column ? "{column}" : "{row}";
            if (field.Key == MatrixField.NumberKey) return column ? "{columnNumber}" : "{rowNumber}";
            if (field.Key == MatrixField.CodeKey) return column ? "{columnCode}" : "{rowCode}";
            if (field.Key == MatrixField.FolderKey) return column ? "{columnFolder}" : "{rowFolder}";

            return column ? field.ColumnToken : field.RowToken;
        }

        /// <summary>
        /// Where the field is drawn: on the board, in the panel, both, or neither.
        ///
        /// Two ticks rather than a four-way chooser, because they are two independent questions
        /// and a coordinator asks them separately: a package code belongs in the panel where a
        /// line is set up, and not as a fourth gutter down the side of a board that is already
        /// carrying a number, a code and a name.
        ///
        /// The four built-in fields are drawn by the board's own appearance settings, which are on
        /// the drawing window, so their cards carry no placement row at all; the window says once,
        /// at the top, where those settings live.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static UIElement Placing(MatrixField field, Action onChange)
        {
            // Nothing on a built-in card. Where those four are drawn is the board's own
            // appearance setting, said once at the top of the window: a sentence repeated on four
            // cards is four lines of the same reading, and it pushed the fields this window is
            // actually for off the bottom of it.
            if (field.IsBuiltIn) return Ui.Stack();

            var board = Ui.Check("On the board",
                                 (field.Shown & MatrixShown.Header) == MatrixShown.Header,
                                 on =>
                                 {
                                     field.Shown = on
                                         ? field.Shown | MatrixShown.Header
                                         : field.Shown & ~MatrixShown.Header;

                                     onChange();
                                 });

            board.ToolTip = "A gutter beside the name, down the side and across the top. "
                            + "A field no line has a value for takes no room either way.";

            var panel = Ui.Check("In the panel",
                                 (field.Shown & MatrixShown.Panel) == MatrixShown.Panel,
                                 on =>
                                 {
                                     field.Shown = on
                                         ? field.Shown | MatrixShown.Panel
                                         : field.Shown & ~MatrixShown.Panel;

                                     onChange();
                                 });

            panel.ToolTip = "A box on the panel beside the grid, where a line's fields are typed.";

            var row = Ui.Across(board, panel);

            row.Margin = new Thickness(0, 0, 0, 2);

            if (field.Shown != MatrixShown.Nowhere) return row;

            // Said out loud, because a field drawn nowhere is still carried by every line and
            // still written into the test names, and a card with both ticks off would otherwise
            // read as a field that had been switched off altogether.
            var kept = Ui.Small("Drawn nowhere. Every line still carries it and the test names "
                                + "still use it.");

            kept.TextWrapping = TextWrapping.Wrap;
            kept.Margin = new Thickness(0, 2, 0, 2);

            return Ui.Stack(row, kept);
        }

        /// <summary>The three ways a field can be filled.</summary>
        private static FrameworkElement Picker(MatrixField field, Action onChange)
        {
            var chosen = field.Fill switch
            {
                MatrixFill.Series => Counted,
                MatrixFill.Same => Everywhere,
                _ => ByHand,
            };

            var pick = Ui.Segmented(new[] { ByHand, Counted, Everywhere }, chosen, said =>
            {
                var wanted = said == Counted ? MatrixFill.Series
                    : said == Everywhere ? MatrixFill.Same
                    : MatrixFill.ByHand;

                if (wanted == field.Fill) return;

                field.Fill = wanted;
                onChange();
            });

            return pick as FrameworkElement ?? new Border { Child = pick };
        }

        /// <summary>The fill chooser, its controls, and the preview of what it would write.</summary>
        private static UIElement Filling(MatrixBoard board, MatrixField field, Action onChange)
        {
            var stack = Ui.Stack();

            if (field.Fill == MatrixFill.Series) stack.Children.Add(Series(field, onChange));
            if (field.Fill == MatrixFill.Same) stack.Children.Add(Constant(field, onChange));

            stack.Children.Add(Preview(board, field));

            return stack;
        }

        /// <summary>The controls a counted field needs, on one line.</summary>
        private static UIElement Series(MatrixField field, Action onChange)
        {
            var kinds = Ui.Segmented(new[] { "1, 2, 3", "A, B, C" },
                field.Steps == MatrixSteps.Letters ? "A, B, C" : "1, 2, 3",
                said =>
                {
                    var wanted = said == "A, B, C" ? MatrixSteps.Letters : MatrixSteps.Numbers;

                    if (wanted == field.Steps) return;

                    field.Steps = wanted;

                    // A start left over from the other kind cannot be counted from, so it moves
                    // to that kind's own first value rather than producing an empty column.
                    field.Start = wanted == MatrixSteps.Letters ? "A" : "1";

                    onChange();
                });

            var start = Ui.Settled(field.Start, "1", typed =>
            {
                field.Start = (typed ?? string.Empty).Trim();
                onChange();
            }, 62);

            var step = Ui.Settled(field.Step.ToString(CultureInfo.InvariantCulture), "1", typed =>
            {
                if (!int.TryParse((typed ?? string.Empty).Trim(), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out var held) || held == 0)
                    return;

                field.Step = held;
                onChange();
            }, 52);

            var row = Ui.Across(
                kinds,
                Ui.Field("Start at", start, null),
                Ui.Field("Step", step, null));

            if (field.Steps == MatrixSteps.Numbers)
            {
                var pad = Ui.Settled(field.Pad == 0 ? string.Empty : field.Pad.ToString(CultureInfo.InvariantCulture),
                    "none", typed =>
                    {
                        var said = (typed ?? string.Empty).Trim();

                        if (said.Length == 0) { field.Pad = 0; onChange(); return; }

                        if (!int.TryParse(said, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                          out var held) || held < 0 || held > 9)
                            return;

                        field.Pad = held;
                        onChange();
                    }, 52);

                pad.ToolTip = "Leading zeros, so 1 reads as 01.";

                row = Ui.Across(kinds, Ui.Field("Start at", start, null),
                                Ui.Field("Step", step, null), Ui.Field("Least digits", pad, null));
            }

            row.Margin = new Thickness(0, 8, 0, 0);

            var folders = Ui.Check("Start again in each folder", field.PerFolder, on =>
            {
                field.PerFolder = on;
                onChange();
            });

            folders.Margin = new Thickness(0, 6, 0, 0);

            return Ui.Stack(row, folders);
        }

        /// <summary>The one box a same-everywhere field needs.</summary>
        private static UIElement Constant(MatrixField field, Action onChange)
        {
            var box = Ui.Settled(field.Same, "P2", typed =>
            {
                field.Same = (typed ?? string.Empty).Trim();
                onChange();
            }, 150);

            var held = Ui.Field("Value", box, null);

            held.Margin = new Thickness(0, 8, 0, 0);
            held.HorizontalAlignment = HorizontalAlignment.Left;

            return held;
        }

        /// <summary>
        /// What this field would write, on the rows the board actually has.
        ///
        /// The reason the window is usable. A start and a step are two numbers whose effect on
        /// forty rows nobody can hold in their head, and the cost of getting them wrong is the
        /// whole matrix renumbered. Printed here, the answer is read before it is applied.
        /// </summary>
        private static UIElement Preview(MatrixBoard board, MatrixField field)
        {
            var said = Ui.Small(Says(board, field));

            said.TextWrapping = TextWrapping.Wrap;
            said.Margin = new Thickness(0, 8, 0, 0);
            said.Tint(TextBlock.ForegroundProperty,
                      field.Fill == MatrixFill.ByHand ? "Muted" : "Accent");

            return said;
        }

        /// <summary>The preview's words.</summary>
        private static string Says(MatrixBoard board, MatrixField field)
        {
            var lines = board.Rows.Count;

            if (field.Fill == MatrixFill.ByHand)
                return "Typed on each line. Nothing is written across the board.";

            if (field.Fill == MatrixFill.Same)
                return field.Same.Length == 0
                    ? "Every line is left empty until a value is typed."
                    : "Every line reads " + field.Same + ".";

            if (lines == 0) return "Counted down the axis once the matrix has rows.";

            // Six is enough to show the step and the padding without wrapping to a second line.
            var shown = Math.Min(6, lines);
            var held = Enumerable.Range(0, shown).Select(field.At).ToList();

            if (held.Any(one => one.Length == 0))
                return "That start cannot be counted from.";

            var run = string.Join(", ", held);

            if (lines > shown) run += ", … " + field.At(lines - 1);

            return (field.PerFolder ? "In each folder: " : "Down the rows: ") + run;
        }

        /// <summary>Ask for a new field's name, and add it.</summary>
        /// <param name="near">The window the prompt belongs to.</param>
        /// <param name="board">The board.</param>
        /// <param name="onAdded">Called once one is added.</param>
        private static void Naming(FrameworkElement? near, MatrixBoard board, Action onAdded)
        {
            if (near == null) return;

            Parts.Ask(near, "What is the field called? “Phase”, “Sub-discipline”, “Package”.",
                      string.Empty, "Add", said =>
            {
                var title = (said ?? string.Empty).Trim();

                if (title.Length == 0) return;

                var key = Keyed(title);

                if (!MatrixField.IsUsable(key))
                {
                    Ui.Told("A field's name has to start with a letter.");
                    return;
                }

                if (board.Scheme.Has(key))
                {
                    Ui.Told("There is already a field called “" + title + "”.");
                    return;
                }

                board.Scheme.Add(key, title);
                onAdded();
            });
        }

        /// <summary>A title reduced to the key its token is built from.</summary>
        private static string Keyed(string title) =>
            new string((title ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
    }
}
