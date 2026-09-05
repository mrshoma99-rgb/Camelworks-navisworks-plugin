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
        private const string ByHand = "By hand";
        private const string Counted = "Counted";
        private const string Everywhere = "Same on all";

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

                foreach (var field in board.Scheme.Fields.ToList())
                    list.Children.Add(Card(board, field,
                                           () => { onTouched?.Invoke(); Redraw(); }));

                foot.Children.Clear();
                foot.Children.Add(Standing(board));
            }

            Redraw();

            var adding = Ui.Quiet("Add a field", () => Ui.Safely(() =>
                Naming(near, board, () => { onTouched?.Invoke(); Redraw(); })));

            adding.HorizontalAlignment = HorizontalAlignment.Left;
            adding.Margin = new Thickness(0, 2, 0, 0);

            var says = Ui.Small(
                "A field is filled the same way on every row and column. Counted writes a series "
                + "down the axis, so renumbering the whole matrix is one edit here rather than one "
                + "per line. A line can be taken out of a field and held by hand from its header.");

            says.TextWrapping = TextWrapping.Wrap;
            says.Margin = new Thickness(0, 0, 0, 10);

            var body = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 14, 16, 0) };

            DockPanel.SetDock(says, Dock.Top);
            body.Children.Add(says);

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
                Child = Ui.Actions(Ui.Primary("Done", () => onDone?.Invoke())),
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
                    : "  ·  " + Ui.Count(pinned, "line") + " hold a field by hand."));

            said.TextWrapping = TextWrapping.Wrap;
            said.Margin = new Thickness(0, 8, 0, 0);

            return said;
        }

        /// <summary>One field, as a card.</summary>
        /// <param name="board">The board.</param>
        /// <param name="field">The field.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static UIElement Card(MatrixBoard board, MatrixField field, Action onChange)
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

            stack.Children.Add(Head(board, field, onChange, field.Fill == MatrixFill.ByHand));

            // A field typed line by line has nothing to configure and nothing to preview, so it
            // is one line rather than a card with a sentence in it saying so. Four of those
            // sentences down a window is the state a scheme starts in, and it read as four
            // warnings rather than as the default.
            if (field.Fill != MatrixFill.ByHand)
                stack.Children.Add(Filling(board, field, onChange));

            card.Padding = field.Fill == MatrixFill.ByHand
                ? new Thickness(12, 8, 12, 8)
                : new Thickness(12, 10, 12, 12);

            card.Child = stack;

            return card;
        }

        /// <summary>The card's top line: what it is called, its token, and the way to remove it.</summary>
        private static UIElement Head(MatrixBoard board, MatrixField field, Action onChange,
                                      bool inline)
        {
            // A grid rather than a dock. Docked, the title, the chooser and the token each took
            // the width they wanted and the chooser was the one that lost: on a field set to
            // "Same on all" the selected segment was clipped away entirely, so the row did not
            // say which of the three it was on. The star column takes the squeeze, and the token
            // — the one part that can be read from the tooltip instead — trims before the
            // control does.
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

            var pick = Picker(field, onChange);

            pick.Margin = new Thickness(10, 0, 0, 0);
            pick.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(pick, 1);
            head.Children.Add(pick);

            // One token rather than the pair. Both forms doubled the width for a value that is
            // mechanical once the first is known, and the width was what squeezed the control.
            var token = new TextBlock
            {
                Text = field.RowToken,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "In a naming pattern: " + field.RowToken + " for the row, "
                          + field.ColumnToken + " for the column.",
            }.Wearing("Mono");

            token.Tint(TextBlock.ForegroundProperty, "Muted");

            Grid.SetColumn(token, 2);
            head.Children.Add(token);

            if (!field.IsBuiltIn)
            {
                var drop = Ui.Quiet("Remove", () => Ui.Safely(() =>
                {
                    board.Scheme.Remove(field.Key, board.Rows.Concat(board.Columns));
                    onChange();
                }));

                drop.ToolTip = "Remove this field and every line's value for it. "
                               + "The matrix's own undo takes it back.";

                drop.VerticalAlignment = VerticalAlignment.Center;

                Grid.SetColumn(drop, 3);
                head.Children.Add(drop);
            }

            return head;
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
                                Ui.Field("Step", step, null), Ui.Field("Digits", pad, null));
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
