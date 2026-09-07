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
                Width = 760,
                Height = 560,
                MinWidth = 620,
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
            var detail = Ui.Stack();
            var foot = Ui.Stack();

            // Which field the detail block is about, by key, so the pick survives the redraw a
            // change causes: the table is built again and the row object with it.
            var chosen = board.Scheme.Fields.Select(one => one.Key).FirstOrDefault();

            void Redraw()
            {
                list.Children.Clear();
                detail.Children.Clear();

                var fields = board.Scheme.Fields.ToList();

                if (fields.Count == 0)
                {
                    list.Children.Add(Ui.Empty(
                        "No fields yet",
                        "A field is one value every row and column carries. Add one to start."));

                    foot.Children.Clear();
                    foot.Children.Add(Standing(board));

                    return;
                }

                var table = Table(board, fields);

                void Changed() { onTouched?.Invoke(); Redraw(); }

                // The verbs are the bar's. Seven cards each carrying their own arrows and their
                // own Remove was seven copies of the same three controls, and there was no way to
                // take two fields out at once.
                list.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Rename", (picked, said) =>
                        Rename(near, picked, said, Changed)),
                    new Ui.OnPicked("Up", (picked, said) =>
                        Shift(board, picked, said, up: true, onChange: Changed)),
                    new Ui.OnPicked("Down", (picked, said) =>
                        Shift(board, picked, said, up: false, onChange: Changed)),
                    new Ui.OnPicked("Remove {n}", (picked, said) =>
                        Drop(board, near, picked, said, Changed), danger: true)));

                // The detail block follows the pick, so the fill controls and the preview are
                // drawn once rather than on every card.
                table.SelectionChanged += (s, e) => Ui.Safely(() =>
                {
                    var picked = table.PickedMany().OfType<MatrixField>().ToList();

                    chosen = picked.Count == 1 ? picked[0].Key : null;

                    Detail(board, picked, detail, Changed);
                });

                var opening = fields.FirstOrDefault(one =>
                    string.Equals(one.Key, chosen, StringComparison.Ordinal));

                if (opening != null)
                {
                    foreach (var item in table.Items)
                        if (item is TableRow row && ReferenceEquals(row.Source, opening))
                            table.SelectedItem = item;
                }

                Detail(board, opening == null
                                  ? Array.Empty<MatrixField>()
                                  : new[] { opening }, detail, Changed);

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
                + "Settings, Grid, symbols and fields. A field of your own is placed by the "
                + "ticks under the table."));

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
                Content = Ui.Stack(Ui.Loose(list), Ui.Loose(detail)),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            // Capped at the width the window opens at, so what the render harness draws at 1180
            // is the layout that ships rather than the same controls stretched across a screen
            // nobody sees them on.
            var root = new DockPanel
            {
                LastChildFill = true,
                MaxWidth = 740,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            var acts = new Border
            {
                Margin = new Thickness(16, 10, 16, 14),
                // Done closes the window; it commits nothing, because every edit writes as it is
                // made. A filled primary on a closing action is the loudest control on the
                // screen doing the least.
                Child = Ui.Actions(null, Ui.Button("Done", () => onDone?.Invoke())),
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            DockPanel.SetDock(acts, Dock.Bottom);
            root.Children.Add(acts);
            root.Children.Add(body);

            return root;
        }

        /// <summary>
        /// The fields as a table, in the order they are carried.
        ///
        /// One row per field, so the order, the tokens and what each one writes read down a
        /// column instead of being seven cards apart.
        /// </summary>
        /// <param name="board">The board.</param>
        /// <param name="fields">The fields, in order.</param>
        private static ListView Table(MatrixBoard board, IReadOnlyList<MatrixField> fields)
        {
            var rows = fields.Select(field => new TableRow(field,
                field.Title,
                Tokened(field),
                Filled(field),
                Where(field),
                Says(board, field))).ToList();

            return Ui.Table(rows,
                            ("Field", 130),
                            ("Token", 120),
                            ("Filled", 140),
                            ("Drawn", 110),
                            ("Writes", 0));
        }

        /// <summary>How a field is filled, in one cell.</summary>
        /// <param name="field">The field.</param>
        private static string Filled(MatrixField field) => field.Fill switch
        {
            MatrixFill.Series => Counted,
            MatrixFill.Same => Everywhere,
            _ => ByHand,
        };

        /// <summary>
        /// Where a field is drawn, in one cell.
        ///
        /// The four built-in fields are placed by the board's own appearance settings rather than
        /// here, so their cell says so instead of reading as a placement nobody can change.
        /// </summary>
        /// <param name="field">The field.</param>
        private static string Where(MatrixField field)
        {
            if (field.IsBuiltIn) return "Board settings";

            var board = (field.Shown & MatrixShown.Header) == MatrixShown.Header;
            var panel = (field.Shown & MatrixShown.Panel) == MatrixShown.Panel;

            if (board && panel) return "Board and panel";
            if (board) return "Board";
            if (panel) return "Panel";

            return "Nowhere";
        }

        /// <summary>
        /// The picked field's own settings: how it is filled, where it is drawn, and a preview.
        ///
        /// One block under the table rather than one per row. The controls a counted field needs
        /// are four boxes and a tick, which is most of a card's height on every field that does
        /// not use them.
        /// </summary>
        /// <param name="board">The board.</param>
        /// <param name="picked">The picked fields.</param>
        /// <param name="into">Where to draw it.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static void Detail(MatrixBoard board, IReadOnlyList<MatrixField> picked,
                                   Panel into, Action onChange)
        {
            into.Children.Clear();

            if (picked.Count != 1)
            {
                var none = Ui.Small(picked.Count == 0
                    ? "Pick a field to set how it is filled and where it is drawn."
                    : "Pick one field to set how it is filled and where it is drawn.");

                none.Margin = new Thickness(0, 10, 0, 0);

                into.Children.Add(none);

                return;
            }

            var field = picked[0];

            var card = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(12, 10, 12, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
            };

            card.Tint(Border.BackgroundProperty, "Card2");
            card.Tint(Border.BorderBrushProperty, "Bd");

            var stack = Ui.Stack();

            var title = Ui.Line(field.Title, 1, bold: true);

            title.Margin = new Thickness(0, 0, 0, 6);

            stack.Children.Add(title);

            var filled = Ui.Field("Filled", Picker(field, onChange), null);

            filled.HorizontalAlignment = HorizontalAlignment.Left;
            filled.Margin = new Thickness(0, 0, 0, 4);

            stack.Children.Add(filled);
            stack.Children.Add(Placing(field, onChange));

            // A field typed line by line has nothing to configure and nothing to preview, so it
            // carries no filling block. What it writes is already in the table's own column.
            if (field.Fill != MatrixFill.ByHand)
                stack.Children.Add(Filling(board, field, onChange));

            card.Child = stack;

            into.Children.Add(card);
        }

        /// <summary>The fields behind the picked rows.</summary>
        /// <param name="picked">What the picking bar handed back.</param>
        private static IReadOnlyList<MatrixField> Fields(IReadOnlyList<object> picked) =>
            (picked ?? Array.Empty<object>()).OfType<MatrixField>().ToList();

        /// <summary>Rename the one picked field.</summary>
        /// <param name="near">The window the prompt belongs to, or null in the harness.</param>
        /// <param name="picked">The picked rows.</param>
        /// <param name="said">Where the bar says what happened.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static void Rename(FrameworkElement? near, IReadOnlyList<object> picked,
                                   Panel said, Action onChange)
        {
            var fields = Fields(picked);

            said.Children.Clear();

            if (fields.Count != 1)
            {
                said.Children.Add(Ui.Note("Pick one field to rename."));
                return;
            }

            if (near == null) return;

            var field = fields[0];

            // The key, and so the token, is not renamed with the title: a pattern already written
            // with this field's token would stop parsing.
            Parts.Ask(near, "What is this field called? Its token stays " + Tokened(field) + ".",
                      field.Title, "Rename", typed =>
            {
                var wanted = (typed ?? string.Empty).Trim();

                if (wanted.Length == 0 || wanted == field.Title) return;

                field.Title = wanted;
                onChange();
            });
        }

        /// <summary>
        /// Move the one picked field up or down the order.
        ///
        /// The order is the order: it is what the panel lists the boxes in and what the board
        /// lays the gutters out in.
        /// </summary>
        /// <param name="board">The board.</param>
        /// <param name="picked">The picked rows.</param>
        /// <param name="said">Where the bar says what happened.</param>
        /// <param name="up">True to move it towards the top.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static void Shift(MatrixBoard board, IReadOnlyList<object> picked, Panel said,
                                  bool up, Action onChange)
        {
            var fields = Fields(picked);

            said.Children.Clear();

            if (fields.Count != 1)
            {
                said.Children.Add(Ui.Note("Pick one field to move."));
                return;
            }

            var field = fields[0];
            var at = board.Scheme.Fields.ToList().FindIndex(one =>
                string.Equals(one.Key, field.Key, StringComparison.Ordinal));

            if (at < 0) return;

            if (!board.Scheme.Move(field.Key, up ? at - 1 : at + 1))
            {
                said.Children.Add(Ui.Note(up
                    ? "“" + field.Title + "” is already the first field."
                    : "“" + field.Title + "” is already the last field."));

                return;
            }

            onChange();
        }

        /// <summary>
        /// Take the picked fields out, once the count has been confirmed.
        ///
        /// Removing a field takes every line's value for it with it, which is why the count is
        /// stated before anything is written. The four built-in fields cannot be removed, so a
        /// selection holding one of them removes the rest and says how many it kept.
        /// </summary>
        /// <param name="board">The board.</param>
        /// <param name="near">The window the prompt belongs to, or null in the harness.</param>
        /// <param name="picked">The picked rows.</param>
        /// <param name="said">Where the bar says what happened.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static void Drop(MatrixBoard board, FrameworkElement? near,
                                 IReadOnlyList<object> picked, Panel said, Action onChange)
        {
            var fields = Fields(picked);

            said.Children.Clear();

            var kept = fields.Count(one => one.IsBuiltIn);
            var going = fields.Where(one => !one.IsBuiltIn).ToList();

            if (going.Count == 0)
            {
                said.Children.Add(Ui.Note(
                    "The name, number, code and folder are built in and cannot be removed."));

                return;
            }

            void Take()
            {
                var gone = 0;

                foreach (var field in going)
                    if (board.Scheme.Remove(field.Key, board.Rows.Concat(board.Columns))) gone++;

                if (gone == 0) return;

                onChange();
            }

            if (near == null) { Take(); return; }

            Parts.Sure(near,
                       "Remove " + Ui.Count(going.Count, "field")
                       + " and every line's value for "
                       + (going.Count == 1 ? "it" : "them") + "? "
                       + (kept == 0
                              ? string.Empty
                              : Ui.Count(kept, "built-in field") + " in the selection "
                                + (kept == 1 ? "is" : "are") + " kept. ")
                       + "The matrix's own undo takes this back.",
                       "Remove " + Ui.Count(going.Count, "field"),
                       () => Ui.Safely(Take));
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
        /// the drawing window, so they carry no ticks; the block says where those settings are
        /// instead.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="onChange">Called after anything is edited.</param>
        private static UIElement Placing(MatrixField field, Action onChange)
        {
            // No ticks on a built-in field. Where those four are drawn is the board's own
            // appearance setting, so the block says where to change it rather than offering a
            // control that writes nothing.
            if (field.IsBuiltIn)
            {
                var settings = Ui.Small("Drawn where the board's own settings say, under "
                                        + "Settings, Grid, symbols and fields.");

                settings.TextWrapping = TextWrapping.Wrap;
                settings.Margin = new Thickness(0, 0, 0, 2);

                return settings;
            }

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
            // still written into the test names, and both ticks off would otherwise read as a
            // field that had been switched off altogether.
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
