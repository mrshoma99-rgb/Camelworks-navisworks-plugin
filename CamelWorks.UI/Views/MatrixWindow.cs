using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The clash matrix: draw one, read one out of the model, or open one from a spreadsheet -
    /// and make the tests it asks for.
    ///
    /// <b>The matrix is the document, and the tests are what falls out of it.</b> That is the
    /// whole shape of this screen. A coordination matrix is drawn at a kick-off, argued over,
    /// re-cut when a package moves, and lived with for a year; the clash tests are how it gets
    /// run this week. So the grid is editable whatever it came from, it saves into the project,
    /// it goes back out as a spreadsheet, and building it a second time changes only what
    /// actually changed rather than throwing every status and comment away.
    ///
    /// <b>Rows are side A, columns are side B.</b> Both axes are lists of their own, so the
    /// square grid everybody draws is the case where they hold the same things - and a matrix of
    /// four packages against every discipline is possible without inventing categories that mean
    /// nothing on the other axis.
    ///
    /// <b>Nothing here walks the model.</b> Listing the sets, listing the loaded models, reading
    /// the tests and their sides - all of it is the document answering about itself. Capturing a
    /// selection reads what is already selected. The one press that costs anything is the build,
    /// and it is a press somebody chose.
    /// </summary>
    public static class MatrixWindow
    {
        // The matrix survives a tab switch, like every other bench in this app: an afternoon's
        // grid lost to a glance at another tab is an afternoon nobody offers this tool again.
        private static MatrixBoard _board = new MatrixBoard();
        private static MatrixLegend _legend = new MatrixLegend();
        private static string _pattern = MatrixNaming.Default;
        private static string _folder = string.Empty;

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static UIElement Surface(Session session) => new Editor(session).View;

        /// <summary>Where the matrix is kept in the project file.</summary>
        private const string Kept = "clashmatrix";

        /// <summary>One editing session over the matrix.</summary>
        private sealed class Editor
        {
            private readonly Session _session;

            /// <summary>
            /// The symbol on the brush, or null while there is none.
            ///
            /// <b>A cell at a time was the whole cost of a matrix.</b> Twenty disciplines is two
            /// hundred and ten cells to fill, and every one of them was a click, a menu, and a
            /// press on a line of that menu. Armed from the legend, a symbol is painted by
            /// clicking a cell or dragging across a run of them, and a whole row or column by
            /// clicking its number or its letter - which is how a person fills in a grid with a
            /// pen, and how every spreadsheet has worked for forty years.
            ///
            /// The menu is still there on the right button, and on the left one whenever nothing
            /// is armed, so nothing has been taken away.
            /// </summary>
            private MatrixSymbol? _brush;

            /// <summary>The strip over the grid: what is armed, and how to put it down.</summary>
            private readonly Border _armed = new Border();

            /// <summary>The count and the brush strip, over the grid.</summary>
            private readonly StackPanel _over = new StackPanel();

            /// <summary>The key at the foot of the screen.</summary>
            private readonly StackPanel _key = new StackPanel();

            private readonly Border _grid = new Border();
            private readonly StackPanel _legendRows = Ui.Stack();
            /// <summary>
            /// What the last act had to say, in a strip that can be put away.
            ///
            /// <b>It used to be an open-ended stack.</b> A sanity check on a real matrix says
            /// twenty things, a spreadsheet read says five, a build says one per test it could
            /// not write - and each of them was another line pushed into the top of the screen,
            /// with nothing to close them and the grid squeezed into whatever was left. So the
            /// notes live in a bordered strip with a fixed height, they scroll inside it, and
            /// the cross puts the whole strip away.
            /// </summary>
            private readonly StackPanel _notes = Ui.Stack();

            private readonly Border _said = new Border();
            private readonly TextBlock _count = new TextBlock();

            // Every painted cell, so setting a symbol repaints one border rather than the grid.
            // A matrix of twenty by twenty is four hundred cells, and rebuilding all of them to
            // change one letter is exactly the fault this product has been reported for.
            private readonly Dictionary<(MatrixLine Row, MatrixLine Column), Border> _cells =
                new Dictionary<(MatrixLine, MatrixLine), Border>();

            internal Editor(Session session)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));

                Load();

                // The notices go in the body, not beside the title.
                //
                // A pane's header is a name on the left and its verbs hard right; anything else
                // put on that line shares the space with the verbs and, since the header does
                // not clip, paints across them. A strip of notes is not a subtitle.
                // The body keeps its own scroller, so the grid scrolls both ways inside the
                // pane rather than being handed unbounded height and never scrolling at all.
                View = Bench.Pane("Clash matrix", null, Body(), Tools(), scrolls: false);

                Redraw();
            }

            /// <summary>What the desk shows.</summary>
            internal UIElement View { get; }

            // ---------------------------------------------------------------------------
            // The frame
            // ---------------------------------------------------------------------------

            /// <summary>The strip over the matrix: what this seat cannot do, and what was said.</summary>
            private UIElement Head()
            {
                var say = Ui.Stack();

                if (!_session.Clash.IsAvailable)
                    say.Children.Add(Ui.Info("This Navisworks edition has no clash engine, so the "
                        + "matrix cannot be run into tests here - but everything else works. Draw "
                        + "it, save it, send it out as a spreadsheet, and build it on a seat that "
                        + "has one. "
                        + (_session.ClashProblem ?? string.Empty).Trim()));

                var close = Ui.Icon(Glyphs.Close, "Put these away", Quiet);

                close.VerticalAlignment = VerticalAlignment.Top;
                close.Margin = new Thickness(6, 0, 0, 0);

                var inside = new DockPanel { LastChildFill = true };

                DockPanel.SetDock(close, Dock.Right);
                inside.Children.Add(close);

                inside.Children.Add(new ScrollViewer
                {
                    Content = Ui.Loose(_notes),

                    // Enough for a handful of lines. A check that finds forty things is a
                    // scrollbar, not a screen of notes over the matrix it is about.
                    MaxHeight = 150,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });

                _said.Padding = new Thickness(10, 8, 8, 8);
                _said.Margin = new Thickness(0, 0, 0, 8);
                _said.BorderThickness = new Thickness(1);
                _said.CornerRadius = new CornerRadius(8);
                _said.Child = inside;
                _said.Visibility = Visibility.Collapsed;

                _said.Tint(Border.BackgroundProperty, "Card2");
                _said.Tint(Border.BorderBrushProperty, "Bd");

                say.Children.Add(Ui.Loose(_said));

                return say;
            }

            /// <summary>Put the notices away, leaving the screen to the matrix.</summary>
            private void Quiet()
            {
                _notes.Children.Clear();
                _said.Visibility = Visibility.Collapsed;
            }

            private UIElement Tools()
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };

                void Add(UIElement button)
                {
                    if (button is FrameworkElement held)
                        held.Margin = new Thickness(0, 0, 6, 0);

                    row.Children.Add(Ui.Loose(button));
                }

                Add(Parts.MenuButton("Start from", Glyphs.Read, Starting()));
                Add(Parts.MenuButton("Naming", Glyphs.Pencil, Naming()));
                Add(Parts.MenuButton("Out", Glyphs.Onward, Sending()));

                var build = Ui.Primary("Build the tests", Build);

                build.Margin = new Thickness(8, 0, 0, 0);

                row.Children.Add(Ui.Loose(build));

                return row;
            }

            private UIElement Body()
            {
                _grid.Padding = new Thickness(0);

                var scrolled = new ScrollViewer
                {
                    Content = Ui.Loose(_grid),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

                    // Sideways as well: a matrix of eighteen columns is wider than any pane, and
                    // a grid that shrank its columns to fit would put a symbol nobody can read in
                    // a cell nobody can click.
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                };

                // The legend, at the foot, as one block rather than a heading with prose under
                // it. It is a key to the grid above it, so it is read across in a glance and not
                // studied - and the sentence explaining what a symbol is belongs beside the
                // symbols, once, not under every draw of them.
                _key.Margin = new Thickness(0, 10, 0, 0);

                var key = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };

                var adding = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var another = Ui.Quiet("Add a symbol", AddSymbol);

                another.Margin = new Thickness(0, 0, 6, 0);
                adding.Children.Add(another);
                adding.Children.Add(Ui.Quiet("Cover every symbol in the grid", CoverSymbols));

                DockPanel.SetDock(adding, Dock.Right);
                key.Children.Add(adding);

                var titled = Ui.Eyebrow("The legend");

                titled.VerticalAlignment = VerticalAlignment.Center;
                key.Children.Add(titled);

                _key.Children.Add(key);

                DockPanel.SetDock(_key, Dock.Bottom);

                // The count sits with the grid it counts, over it, not below the legend where it
                // read as a footnote to the key.
                _count.Margin = new Thickness(0, 0, 0, 6);

                _armed.Padding = new Thickness(10, 6, 8, 6);
                _armed.Margin = new Thickness(0, 0, 0, 6);
                _armed.BorderThickness = new Thickness(1);
                _armed.CornerRadius = new CornerRadius(8);
                _armed.Visibility = Visibility.Collapsed;

                _armed.Tint(Border.BackgroundProperty, "AccentWash");
                _armed.Tint(Border.BorderBrushProperty, "Accent");

                _over.Children.Add(Ui.Loose(_armed));
                _over.Children.Add(Ui.Loose(_count.Wearing("Hint")));

                _key.Children.Add(Ui.Loose(_legendRows));

                var stack = new DockPanel { LastChildFill = true };

                var top = Head();

                DockPanel.SetDock(top, Dock.Top);
                stack.Children.Add(top);

                DockPanel.SetDock(_over, Dock.Top);
                stack.Children.Add(Ui.Loose(_over));

                stack.Children.Add(Ui.Loose(_key));
                stack.Children.Add(scrolled);

                return stack;
            }

            // ---------------------------------------------------------------------------
            // The grid
            // ---------------------------------------------------------------------------

            /// <summary>
            /// Cell sizes, fixed, so nothing measures itself out of existence.
            ///
            /// <b>A column is as wide as a symbol, not as wide as a name.</b> The names went
            /// across the top and were cut off anyway - "WSC_Process st..." - while every column
            /// paid a hundred pixels for a letter. Stood on their end they cost height once, and
            /// eighteen columns fit on a screen instead of six.
            /// </summary>
            private const double CellWide = 44;
            private const double CellHigh = 30;
            private const double HeaderWide = 250;
            private const double HeaderHigh = 168;

            /// <summary>How wide the index gutter down the left is.</summary>
            private const double IndexWide = 26;


            /// <summary>
            /// Build the grid again. Only for a change of shape - a row added, removed or moved.
            ///
            /// Painting a symbol into a cell does not come through here: that is one border,
            /// repainted where it stands.
            /// </summary>
            private void Redraw()
            {
                _cells.Clear();

                var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };

                // Column 0 is the index gutter, column 1 the row headers, then one per column.
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IndexWide) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderWide) });

                foreach (var _ in _board.Columns)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWide) });

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Row 0 is the letters, row 1 the column headers, then one per row.
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHigh) });

                foreach (var _ in _board.Rows)
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHigh) });

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Put(grid, Corner(), 0, 0);
                Grid.SetRowSpan(grid.Children[grid.Children.Count - 1], 2);
                Grid.SetColumnSpan(grid.Children[grid.Children.Count - 1], 2);

                for (var c = 0; c < _board.Columns.Count; c++)
                {
                    Put(grid, Marker(Letter(c), _board.Columns[c].Titled,
                                     _board.Columns[c], rows: false), 0, c + 2);
                    Put(grid, ColumnHead(_board.Columns[c], c), 1, c + 2);
                }

                Put(grid, Adding("Add a column", AddColumn), 1, _board.Columns.Count + 2);

                for (var r = 0; r < _board.Rows.Count; r++)
                {
                    var row = _board.Rows[r];

                    Put(grid, Marker(Number(r), row.Titled, row, rows: true), r + 2, 0);
                    Put(grid, RowHead(row, r), r + 2, 1);

                    for (var c = 0; c < _board.Columns.Count; c++)
                    {
                        var column = _board.Columns[c];
                        var cell = Cell(row, column);

                        _cells[(row, column)] = cell;
                        Put(grid, cell, r + 2, c + 2);
                    }
                }

                Put(grid, Adding("Add a row", AddRow), _board.Rows.Count + 2, 1);

                var bare = _board.Rows.Count == 0 && _board.Columns.Count == 0;

                _grid.Child = bare ? Empty() : (UIElement)grid;

                // Nothing drawn yet is a screen about starting, so the count of an empty grid
                // and a key to symbols nothing is using are two lines of furniture in the way of
                // it. They arrive with the matrix.
                _over.Visibility = bare ? Visibility.Collapsed : Visibility.Visible;
                _key.Visibility = bare ? Visibility.Collapsed : Visibility.Visible;

                RedrawArmed();
                RedrawLegend();
                Recount();
            }

            private static void Put(Grid grid, UIElement child, int row, int column)
            {
                Grid.SetRow(child, row);
                Grid.SetColumn(child, column);
                grid.Children.Add(child);
            }

            /// <summary>
            /// The screen before there is a matrix: the four ways in, as things to press.
            ///
            /// <b>What it replaced.</b> A sentence in the top corner and three buttons, over an
            /// empty white screen the height of the window. The question this screen has to
            /// answer is "where does a matrix come from", and the answer is four different
            /// places - so it says all four, says what each one gives you, and leaves the one
            /// that costs nothing and finds the most until last.
            /// </summary>
            private UIElement Empty()
            {
                var ways = new StackPanel { MaxWidth = 560 };

                ways.Children.Add(Bench.Ready("Draw the coordination matrix",
                    "The disciplines down one side and across the top, with a symbol where two "
                    + "of them have to be checked against each other. Every symbol in the grid "
                    + "becomes a clash test."));

                var sets = Sets().Count;

                ways.Children.Add(Ui.Row(
                    "The model's saved sets",
                    sets == 0
                        ? "This model has no saved selection or search sets yet."
                        : Ui.Count(sets, "set") + " to choose from. Tick the ones that are the "
                          + "disciplines and each becomes a row, named after the set and already "
                          + "pointed at it.",
                    sets == 0 ? null : "Pick the sets",
                    sets == 0 ? (Action)(() => Warned(Ui.Problem(
                        "This model has no saved selection or search sets. Make them in "
                        + "Navisworks - or draw the matrix by hand and point the rows at "
                        + "something else."))) : FromSets));

                ways.Children.Add(Ui.Row(
                    "The tests this model already has",
                    "Every clash test says what it is between, so the matrix is already there - "
                    + "read it back and see it whole.",
                    "Read the tests",
                    FromModel));

                ways.Children.Add(Ui.Row(
                    "A spreadsheet",
                    "The client's own workbook, with its own symbols and a key in the corner. "
                    + "The legend is built from what it uses.",
                    "Open a workbook",
                    FromSheet));

                ways.Children.Add(Ui.Row(
                    "By hand",
                    "One row at a time, pointed at whatever each one is: a set, whole models, "
                    + "what is selected, a branch of the tree.",
                    "Add the first row",
                    AddRow));

                return ways;
            }

            /// <summary>
            /// One row per saved set, named after it, numbered, and already pointing at it.
            ///
            /// <b>The four presses a row used to cost.</b> Add a row, name it, number it, point
            /// it at something - each its own dialogue, times twenty disciplines. But the
            /// disciplines are already in the document, as the saved sets somebody made to run
            /// the tests with: WSC_Pipes, CIV_C_Concrete, FGT_Ducts. Ticking them is the whole
            /// job, and the columns follow from the rows.
            ///
            /// <b>Nothing is overwritten.</b> A set already on the matrix is left where it is,
            /// so this can be pressed again after a set is added without disturbing the grid
            /// that has been filled in.
            /// </summary>
            private void FromSets()
            {
                var sets = Sets();

                if (sets.Count == 0)
                {
                    Warned(Ui.Problem("This model has no saved selection or search sets."));
                    return;
                }

                var already = new HashSet<string>(
                    _board.Rows.Where(r => r.Source.IsSet).Select(r => r.Source.Name),
                    StringComparer.OrdinalIgnoreCase);

                Parts.Search(_grid, "Which sets are the disciplines?",
                    sets.Select(s => new KeyValuePair<string, int>(s.Name, s.Members)).ToList(),
                    already, null,
                    picked =>
                    {
                        var wanted = new HashSet<string>(picked, StringComparer.OrdinalIgnoreCase);
                        var made = 0;

                        // In the order the model lists them, not the order they were ticked, so
                        // the matrix comes out alphabetical like the picker was.
                        foreach (var set in sets)
                        {
                            if (!wanted.Contains(set.Name) || already.Contains(set.Name)) continue;

                            _board.AddRow(new MatrixLine(
                                set.Name,
                                Next(_board.Rows),
                                set.IsSearch
                                    ? MatrixSource.SearchSet(set.Name)
                                    : MatrixSource.SelectionSet(set.Name)));

                            made++;
                        }

                        if (made == 0)
                        {
                            Said(Ui.Note("Every set you ticked is already on the matrix."));
                            return;
                        }

                        // A matrix of disciplines is square: everything against everything, and
                        // the half nobody fills in is the mirror of the half they do.
                        _board.SquareOffRows();

                        Seed();
                        Redraw();

                        Said(Ui.Done(Ui.Count(made, "row") + " added, and the columns to match. "
                                     + "Put a symbol in the cells that have to be checked - or "
                                     + "arm one in the legend and drag across them."));
                    });
            }

            /// <summary>
            /// Two symbols to begin with, so the first cell somebody clicks has somewhere to go.
            ///
            /// A new matrix had an empty legend, and the first click on a cell answered "the
            /// legend is empty, make one first" - a dead end on the first gesture anybody makes.
            /// A hard clash and a clearance are what a matrix is nearly always made of; both are
            /// editable like any other symbol, and this never touches a legend that already has
            /// something in it.
            /// </summary>
            private void Seed()
            {
                if (!_legend.IsEmpty) return;

                _legend = new MatrixLegend(new[]
                {
                    new MatrixSymbol("A", MatrixTest.Hard, 0.010, true),
                    new MatrixSymbol("B", MatrixTest.Clearance, 0.050, true),
                });
            }

            private UIElement Corner()
            {
                var corner = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 6, 8, 6),
                };

                corner.Tint(Border.BorderBrushProperty, "Bd");
                corner.Tint(Border.BackgroundProperty, "Pane");

                corner.Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Children =
                    {
                        Ui.Eyebrow("Rows 1, 2, 3 are side A"),
                        Ui.Small("Columns A, B, C are side B"),
                    },
                };

                return corner;
            }

            /// <summary>
            /// A row's number or a column's letter, in the gutter beside it.
            ///
            /// <b>A matrix of twenty by twenty needs coordinates.</b> Without them the only way
            /// to say which cell is wrong is to read two long names off two edges of the screen
            /// and hope the eye tracked the right line across - which on a grid this size it does
            /// not. Numbers down and letters across is what every spreadsheet does and what a
            /// coordination meeting already says out loud: "C7 should be a clearance".
            /// </summary>
            /// <param name="mark">The number or letter.</param>
            /// <param name="what">What it stands for, for the tip.</param>
            /// <param name="line">The row or column it indexes.</param>
            /// <param name="rows">True when it indexes a row.</param>
            private UIElement Marker(string mark, string what, MatrixLine line, bool rows)
            {
                var cell = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = mark + " — " + what
                              + (_brush == null
                                  ? string.Empty
                                  : ". Click to put " + Quoted(_brush.Symbol) + " down the whole "
                                    + (rows ? "row." : "column.")),
                    Child = new TextBlock
                    {
                        Text = mark,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }.Wearing("Hint"),
                };

                cell.Tint(Border.BorderBrushProperty, "Bd");
                cell.Tint(Border.BackgroundProperty, "Pane");

                cell.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;
                    Sweep(line, rows);
                });

                return cell;
            }

            /// <summary>Where a cell is, as a spreadsheet would say it: the column, then the row.</summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The column.</param>
            private string Where(MatrixLine row, MatrixLine column)
            {
                var down = At(_board.Rows, row);
                var across = At(_board.Columns, column);

                return down < 0 || across < 0
                    ? string.Empty
                    : Letter(across) + Number(down);
            }

            /// <summary>Where one line sits in its list, or -1.</summary>
            /// <param name="lines">The rows or the columns.</param>
            /// <param name="line">The one being looked for.</param>
            private static int At(IReadOnlyList<MatrixLine> lines, MatrixLine line)
            {
                for (var i = 0; i < lines.Count; i++)
                    if (ReferenceEquals(lines[i], line))
                        return i;

                return -1;
            }

            /// <summary>A row's number, one-based.</summary>
            /// <param name="at">Which row.</param>
            private static string Number(int at) =>
                (at + 1).ToString(CultureInfo.InvariantCulture);

            /// <summary>
            /// A column's letter: A to Z, then AA, AB, and so on, the way a spreadsheet counts.
            /// </summary>
            /// <param name="at">Which column.</param>
            private static string Letter(int at)
            {
                var name = string.Empty;

                for (var left = at; left >= 0; left = (left / 26) - 1)
                {
                    name = (char)('A' + (left % 26)) + name;
                }

                return name;
            }

            /// <summary>One row's header: what it is, where it points, and its own menu.</summary>
            private UIElement RowHead(MatrixLine line, int at)
            {
                var head = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 3, 6, 3),
                    Cursor = Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ToolTip = "Row " + Number(at) + ": " + line.Titled + " — "
                              + line.Source.Kinded() + ", " + line.Source.Describe()
                              + ". Click for what can be done to this row.",
                };

                head.Tint(Border.BorderBrushProperty, "Bd");
                head.Tint(Border.BackgroundProperty, "Pane");

                var words = new DockPanel { LastChildFill = true };

                var mark = Ui.Glyph(line.IsUnbacked ? Glyphs.Warn : Glyphs.Layers, 12,
                                    line.IsUnbacked ? "RedTx" : "Muted", new Thickness(0, 0, 6, 0));

                words.Children.Add(mark);

                var stacked = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                stacked.Children.Add(new TextBlock
                {
                    Text = line.Titled,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }.Wearing("Body"));

                stacked.Children.Add(new TextBlock
                {
                    Text = line.Source.Describe(),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }.Wearing("Hint"));

                words.Children.Add(stacked);
                head.Child = words;

                Draggable(head, line, rows: true);
                Opens(head, () => LineMenu(line, at, rows: true));

                return head;
            }

            /// <summary>One column's header, which is a row header stood on its end.</summary>
            private UIElement ColumnHead(MatrixLine line, int at)
            {
                var head = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(2, 6, 2, 6),
                    Cursor = Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ToolTip = "Column " + Letter(at) + ": " + line.Titled + " — "
                              + line.Source.Kinded() + ", " + line.Source.Describe()
                              + ". Click for what can be done to this column.",
                };

                head.Tint(Border.BorderBrushProperty, "Bd");
                head.Tint(Border.BackgroundProperty, "Pane");

                // Stood on its end, reading upwards.
                //
                // <b>Wrapping did not work.</b> A column had to be a hundred pixels wide to hold
                // two words of a name, six columns filled the screen, and the names were cut off
                // anyway - "WSC_Process st...". Turned through ninety degrees the whole name
                // fits, a column costs what a symbol costs, and three times as many of them are
                // on screen at once. It is what every coordination matrix ever drawn does.
                //
                // A LayoutTransform, not a RenderTransform: the header has to be measured
                // rotated, or the grid gives it the width of the unturned text and the row is
                // one line tall with the names hanging out of it.
                var name = new TextBlock
                {
                    Text = line.Titled,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,

                    // As long as the header is tall, so a very long name ellipsises rather than
                    // pushing the whole top of the grid down.
                    MaxWidth = HeaderHigh - 26,
                    LayoutTransform = new System.Windows.Media.RotateTransform(-90),
                };

                var stacked = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };

                if (line.IsUnbacked)
                    stacked.Children.Add(Ui.Glyph(Glyphs.Warn, 10, "RedTx", new Thickness(0, 0, 0, 4)));

                stacked.Children.Add(name.Wearing("Body"));

                head.Child = stacked;

                Draggable(head, line, rows: false);
                Opens(head, () => LineMenu(line, at, rows: false));

                return head;
            }

            /// <summary>One cell: the symbol in it, and a click that changes it.</summary>
            private Border Cell(MatrixLine row, MatrixLine column)
            {
                var cell = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent,
                };

                cell.Tint(Border.BorderBrushProperty, "Bd");

                Paint(cell, row, column);

                // Painting takes the press; the menu takes the release, and only when nothing is
                // armed. Two gestures on one cell, and which one is live is said by the strip
                // over the grid rather than left to be discovered.
                cell.MouseLeftButtonDown += (s, e) => Ui.Safely(() =>
                {
                    if (_brush == null) return;

                    e.Handled = true;
                    Daub(row, column);
                });

                cell.MouseEnter += (s, e) => Ui.Safely(() =>
                {
                    if (_brush == null
                        || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                        return;

                    Daub(row, column);
                });

                cell.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;

                    if (_brush == null) Parts.PopMenu(cell, CellMenu(row, column));
                });

                // The whole menu is still one right-click away while a brush is armed.
                cell.ContextMenu = Parts.Context(() => CellMenu(row, column), null);

                return cell;
            }

            /// <summary>Put the armed symbol in one cell, if it is not already there.</summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The column.</param>
            private void Daub(MatrixLine row, MatrixLine column)
            {
                var brush = _brush;

                if (brush == null) return;

                // Dragging crosses the same cell many times; only a change is worth a repaint.
                if (string.Equals(_board.At(row, column), brush.Symbol, StringComparison.Ordinal))
                    return;

                _board.Set(row, column, brush.Symbol);
                Repaint(row, column);
            }

            /// <summary>Put the armed symbol down the whole of one row or across one column.</summary>
            /// <param name="line">The row or the column.</param>
            /// <param name="rows">True for a row, false for a column.</param>
            private void Sweep(MatrixLine line, bool rows)
            {
                if (_brush == null) return;

                if (rows) foreach (var column in _board.Columns) Daub(line, column);
                else foreach (var row in _board.Rows) Daub(row, line);
            }

            /// <summary>Arm a symbol, or put the one already armed down.</summary>
            /// <param name="symbol">The symbol, or null for none.</param>
            private void Arm(MatrixSymbol? symbol)
            {
                _brush = symbol != null && _brush != null
                         && string.Equals(_brush.Symbol, symbol.Symbol, StringComparison.Ordinal)
                    ? null
                    : symbol;

                RedrawArmed();
                RedrawLegend();
            }

            /// <summary>Say what is armed, over the grid, with the way to put it down.</summary>
            private void RedrawArmed()
            {
                if (_brush == null)
                {
                    _armed.Visibility = Visibility.Collapsed;
                    _armed.Child = null;
                    return;
                }

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                row.Children.Add(Ui.Glyph(Glyphs.Pencil, 12, "Accent", new Thickness(0, 0, 8, 0)));

                row.Children.Add(new TextBlock
                {
                    Text = Quoted(_brush.Symbol) + " is on the brush",
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body"));

                row.Children.Add(new TextBlock
                {
                    Text = "Click a cell or drag across a run of them. "
                           + "Click a row's number or a column's letter for the whole line.",
                    Margin = new Thickness(10, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Hint"));

                var down = Ui.Quiet("Put it down", () => Arm(null));

                down.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(down);

                _armed.Child = row;
                _armed.Visibility = Visibility.Visible;
            }

            /// <summary>
            /// Draw what is in one cell. The only thing a symbol change touches.
            ///
            /// <b>The mirror half is drawn quieter.</b> A clash matrix is mirrored about its
            /// diagonal — A against B is the same test as B against A — so the grid holds each
            /// pair twice and the job must not. The half below the diagonal carries the same
            /// symbol, faintly, and says in its tip which cell it is the other side of; putting a
            /// symbol in either half still writes both, because somebody filling in the triangle
            /// they were handed should not have to know which one this drawing kept.
            /// </summary>
            private void Paint(Border cell, MatrixLine row, MatrixLine column)
            {
                var symbol = _board.At(row, column);
                var meaning = symbol == null ? null : _legend.Meaning(symbol);
                var mirrored = !_board.IsKeptHalf(row, column);

                // The cell's coordinates first, so a tip answers "which one is this" before it
                // answers "what is in it" - which is the question somebody has when they are
                // reading a wrong symbol off a grid twenty wide.
                var at = Where(row, column);

                cell.Opacity = mirrored ? 0.45 : 1.0;

                var text = new TextBlock
                {
                    Text = symbol ?? string.Empty,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                text.Wearing("Body");

                if (symbol == null)
                {
                    cell.Background = System.Windows.Media.Brushes.Transparent;
                    cell.ToolTip = at + " · " + row.Titled + " against " + column.Titled
                                   + " — not tested. Click to put a symbol in it.";
                }
                else if (meaning == null)
                {
                    cell.Tint(Border.BackgroundProperty, "RedBg");
                    text.Tint(TextBlock.ForegroundProperty, "RedTx");
                    cell.ToolTip = at + " · the legend does not say what " + Quoted(symbol)
                                   + " means, so this cell makes no test.";
                }
                else
                {
                    cell.Tint(Border.BackgroundProperty, meaning.Enabled ? "AccentWash" : "Nav");
                    cell.ToolTip = at + " · " + row.Titled + " against " + column.Titled + " — "
                                   + MatrixNaming.Worded(meaning.Test) + " at "
                                   + Millimetres(meaning.Tolerance)
                                   + (meaning.Enabled ? string.Empty : ", turned off");
                }

                if (mirrored)
                {
                    var mirror = _board.MirrorOf(row, column);

                    if (mirror != null)
                        cell.ToolTip = "The same test as " + mirror.Value.Row.Titled + " against "
                                       + mirror.Value.Column.Titled
                                       + ", which is the cell above the diagonal. A matrix is"
                                       + " mirrored, so this pair is only ever run once.";
                }

                cell.Child = text;
            }

            /// <summary>Repaint one cell where it stands, and nothing else.</summary>
            private void Repaint(MatrixLine row, MatrixLine column)
            {
                if (_cells.TryGetValue((row, column), out var cell)) Paint(cell, row, column);

                // And the cell on the other side of the diagonal, which the board just wrote the
                // same symbol into. Leaving it alone showed one pair saying two different things.
                var mirror = _board.MirrorOf(row, column);

                if (mirror != null
                    && _cells.TryGetValue((mirror.Value.Row, mirror.Value.Column), out var other))
                    Paint(other, mirror.Value.Row, mirror.Value.Column);

                Recount();
            }

            /// <summary>Repaint every cell, for a legend change that touches many at once.</summary>
            private void RepaintAll()
            {
                foreach (var cell in _cells) Paint(cell.Value, cell.Key.Row, cell.Key.Column);

                Recount();
            }

            private void Recount()
            {
                var plan = MatrixPlanner.Plan(_board, _legend, _pattern);

                _count.Text = _board.Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows, "
                    + _board.Columns.Count.ToString(CultureInfo.InvariantCulture) + " columns, "
                    + plan.Explain()
                    + (plan.Tests.Count == 0
                        ? string.Empty
                        : ". First one: " + Quoted(plan.Tests[0].Name));
            }

            /// <summary>
            /// Where a press started, so a drag can be told from a click.
            ///
            /// One field rather than one per header: only one pointer is ever down.
            /// </summary>
            private Point _from;
            private bool _dragging;

            private static void Opens(Border on, Func<List<Parts.Entry>> menu)
            {
                on.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    Ui.Safely(() => Parts.PopMenu(on, menu()));
                };
            }

            /// <summary>
            /// Let a header be dragged into a new place in its own axis.
            ///
            /// <b>A drag has to be told from a click</b>, because the same header opens a menu.
            /// So a press only records where it started; it becomes a drag once the pointer has
            /// moved past the system's own threshold, and the flag it sets suppresses the menu
            /// that the release would otherwise open. Getting this wrong gives a header that
            /// opens a menu every time somebody tries to move it, which is worse than not being
            /// draggable at all.
            ///
            /// <b>Rows drop on rows and columns on columns.</b> A row dragged onto a column
            /// header would have to mean something, and there is nothing sensible for it to mean.
            /// </summary>
            /// <param name="head">The header.</param>
            /// <param name="line">The row or column it stands for.</param>
            /// <param name="rows">True for a row header, false for a column.</param>
            private void Draggable(Border head, MatrixLine line, bool rows)
            {
                var carried = rows ? RowCarried : ColumnCarried;

                head.AllowDrop = true;

                head.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    _from = e.GetPosition(null);
                    _dragging = false;
                };

                head.MouseMove += (s, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || _dragging) return;

                    var now = e.GetPosition(null);

                    if (Math.Abs(now.X - _from.X) < SystemParameters.MinimumHorizontalDragDistance
                        && Math.Abs(now.Y - _from.Y) < SystemParameters.MinimumVerticalDragDistance)
                        return;

                    _dragging = true;

                    Ui.Safely(() => DragDrop.DoDragDrop(head, new DataObject(carried, line),
                                                        DragDropEffects.Move));
                };

                head.DragOver += (s, e) =>
                {
                    var taking = e.Data.GetDataPresent(carried);

                    e.Effects = taking ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;

                    // A line down the edge it would land on, so it is obvious where it goes
                    // before the button comes up.
                    head.BorderThickness = taking
                        ? (rows ? new Thickness(0, 2, 1, 1) : new Thickness(2, 0, 1, 1))
                        : new Thickness(0, 0, 1, 1);
                };

                head.DragLeave += (s, e) => head.BorderThickness = new Thickness(0, 0, 1, 1);

                head.Drop += (s, e) =>
                {
                    head.BorderThickness = new Thickness(0, 0, 1, 1);

                    if (!(e.Data.GetData(carried) is MatrixLine moved) || ReferenceEquals(moved, line))
                        return;

                    e.Handled = true;

                    Ui.Safely(() =>
                    {
                        var lines = rows ? _board.Rows : _board.Columns;

                        var from = lines.ToList().FindIndex(l => ReferenceEquals(l, moved));
                        var to = lines.ToList().FindIndex(l => ReferenceEquals(l, line));

                        if (from < 0 || to < 0) return;

                        if (rows) _board.MoveRow(from, to); else _board.MoveColumn(from, to);

                        Redraw();
                    });
                };

                // The release that ends a drag must not also open the menu.
                head.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    if (!_dragging) return;

                    _dragging = false;
                    e.Handled = true;
                };
            }

            /// <summary>What a dragged row and a dragged column are carried as.</summary>
            private const string RowCarried = "CamelWorks.MatrixRow";
            private const string ColumnCarried = "CamelWorks.MatrixColumn";


            // ---------------------------------------------------------------------------
            // Rows, columns and cells
            // ---------------------------------------------------------------------------

            private void AddRow()
            {
                _board.AddRow(new MatrixLine("New row", Next(_board.Rows)));

                Seed();
                Redraw();
            }

            private void AddColumn()
            {
                _board.AddColumn(new MatrixLine("New column", Next(_board.Columns)));

                Seed();
                Redraw();
            }

            /// <summary>The next number in a list, when the list is numbered at all.</summary>
            private static string Next(IReadOnlyList<MatrixLine> lines)
            {
                var highest = 0;

                foreach (var line in lines)
                    if (int.TryParse(line.Number, NumberStyles.None, CultureInfo.InvariantCulture,
                                     out var held) && held > highest)
                        highest = held;

                return (highest + 1).ToString("00", CultureInfo.InvariantCulture);
            }

            /// <summary>Everything that can be done to one row or column.</summary>
            private List<Parts.Entry> LineMenu(MatrixLine line, int at, bool rows)
            {
                var lines = rows ? _board.Rows : _board.Columns;
                var what = rows ? "row" : "column";

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading(line.Titled),

                    Parts.Entry.Act("Name it", Glyphs.Pencil,
                        () => Parts.Ask(_grid, "What is this " + what + " called?", line.Name,
                                        "Rename", named =>
                                        {
                                            line.Name = named;
                                            Redraw();
                                        })),

                    Parts.Entry.Act("Number it", Glyphs.Number,
                        () => Parts.Ask(_grid, "The " + what + "'s number", line.Number,
                                        "Set", number =>
                                        {
                                            line.Number = number;
                                            Redraw();
                                        })),

                    Parts.Entry.Act("Code it", Glyphs.Tune,
                        () => Parts.Ask(_grid, "A discipline code, such as ST", line.Code,
                                        "Set", code =>
                                        {
                                            line.Code = code;
                                            Redraw();
                                        })),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Point it at: " + line.Source.Kinded()),
                };

                items.AddRange(SourceMenu(line));

                items.Add(Parts.Entry.Rule());

                if (at > 0)
                    items.Add(Parts.Entry.Act("Move it earlier", Glyphs.Up, () =>
                    {
                        if (rows) _board.MoveRow(at, at - 1); else _board.MoveColumn(at, at - 1);
                        Redraw();
                    }));

                if (at < lines.Count - 1)
                    items.Add(Parts.Entry.Act("Move it later", Glyphs.Down, () =>
                    {
                        if (rows) _board.MoveRow(at, at + 1); else _board.MoveColumn(at, at + 1);
                        Redraw();
                    }));

                items.Add(Parts.Entry.Act(rows ? "Add a row under it" : "Add a column after it",
                    Glyphs.Add, () =>
                    {
                        if (rows) AddRow(); else AddColumn();
                    }));

                items.Add(Parts.Entry.Danger("Take it out", Glyphs.Bin, () =>
                {
                    if (rows) _board.RemoveRow(line); else _board.RemoveColumn(line);
                    Redraw();
                }));

                return items;
            }

            /// <summary>
            /// The five ways to point a row at something, each offered as a list rather than a box.
            ///
            /// Nobody types the name of a search set. The document knows what it has, so the
            /// choice is what it has - and a name typed by hand is a name that is right until
            /// somebody renames the set, at which point the matrix silently tests nothing.
            /// </summary>
            private List<Parts.Entry> SourceMenu(MatrixLine line)
            {
                var items = new List<Parts.Entry>();

                var sets = Sets();

                items.Add(sets.Count == 0
                    ? Parts.Entry.Barred("A saved set", Glyphs.Folder,
                        "This model has no saved selection or search sets.")
                    : Parts.Entry.Act("A saved set  ·  " + Ui.Count(sets.Count, "of them"),
                        Glyphs.Folder,
                        () => Parts.Search(_grid, "Which set is " + line.Name + "?",
                            sets.Select(s => new KeyValuePair<string, int>(s.Name, s.Members))
                                .ToList(),
                            line.Source.IsSet ? new[] { line.Source.Name } : null, null,
                            picked =>
                            {
                                var name = picked.FirstOrDefault();

                                if (name == null) return;

                                var found = sets.FirstOrDefault(s => string.Equals(
                                    s.Name, name, StringComparison.OrdinalIgnoreCase));

                                line.Source = found != null && found.IsSearch
                                    ? MatrixSource.SearchSet(name)
                                    : MatrixSource.SelectionSet(name);

                                Redraw();
                            })));

                var models = Models();

                items.Add(models.Count == 0
                    ? Parts.Entry.Barred("Whole models", Glyphs.Model, "Nothing is loaded.")
                    : Parts.Entry.Act("Whole models  ·  " + Ui.Count(models.Count, "loaded"),
                        Glyphs.Model,
                        () => Parts.Search(_grid, "Which models are " + line.Name + "?",
                            models.Select(m => new KeyValuePair<string, int>(m, 0)).ToList(),
                            line.Source.Kind == MatrixSourceKind.Models ? line.Source.Scopes : null,
                            null,
                            picked =>
                            {
                                if (picked.Count == 0) return;

                                line.Source = MatrixSource.Models(picked.ToList());
                                Redraw();
                            })));

                items.Add(Parts.Entry.Saying("What is selected now", Glyphs.Focus,
                    "Captured as a list of elements, so nothing modelled later joins it",
                    () => Capture(line, MatrixSourceKind.CurrentSelection)));

                items.Add(Parts.Entry.Saying("A pick from the selection tree", Glyphs.Layers,
                    "Pick the branches in Navisworks' tree, then choose this",
                    () => Capture(line, MatrixSourceKind.TreePick)));

                // The one source on this screen that costs a pass of the model, which is why it
                // says so and runs under a job with a Stop. Everything else here is the document
                // answering about itself.
                items.Add(Parts.Entry.Saying("What is visible now", Glyphs.Eye,
                    "Walks the model once to find what is not hidden, then holds that list",
                    () => CaptureVisible(line)));

                if (!line.Source.IsEmpty)
                    items.Add(Parts.Entry.Act("Point it at nothing", Glyphs.Close, () =>
                    {
                        line.Source = MatrixSource.Nothing;
                        Redraw();
                    }));

                return items;
            }

            /// <summary>Hold whatever is selected in the host right now.</summary>
            private void Capture(MatrixLine line, MatrixSourceKind kind)
            {
                try
                {
                    var keys = _session.Model.Traverse(TraversalScope.CurrentSelection)
                                             .Select(i => i.Key)
                                             .Where(k => !k.IsEmpty)
                                             .Distinct()
                                             .ToList();

                    if (keys.Count == 0)
                    {
                        Warned(Ui.Problem("Nothing is selected. Pick it in Navisworks first, "
                                          + "then choose this again."));
                        return;
                    }

                    Held(line, kind, keys);
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("That could not be captured: " + Ui.Describe(e)));
                }
            }

            /// <summary>
            /// Hold everything that is not hidden, which means walking the model once.
            ///
            /// Under a job with a Stop, because it is the only thing on this screen that does.
            /// </summary>
            private void CaptureVisible(MatrixLine line)
            {
                var keys = new List<ElementKey>();

                Ui.Working("Reading what is visible...", job =>
                {
                    var seen = 0;

                    foreach (var item in _session.Model.Traverse(TraversalScope.WholeDocument))
                    {
                        if (!job.Step(++seen, "elements read")) return;

                        if (item.IsHidden || !item.HasGeometry || item.Key.IsEmpty) continue;

                        keys.Add(item.Key);
                    }
                });

                if (keys.Count == 0)
                {
                    Warned(Ui.Problem("Nothing visible came back. Either the model is all "
                                      + "hidden, or the read was stopped."));
                    return;
                }

                Held(line, MatrixSourceKind.VisibleElements, keys.Distinct().ToList());
            }

            private void Held(MatrixLine line, MatrixSourceKind kind, IReadOnlyList<ElementKey> keys)
            {
                line.Source = MatrixSource.Captured(kind, keys);

                Redraw();

                Said(Ui.Done(line.Name + " now holds " + Ui.Count(keys.Count, "element")
                             + ", as they are today. Anything modelled later is not in it."));
            }

            /// <summary>What goes in one cell.</summary>
            private List<Parts.Entry> CellMenu(MatrixLine row, MatrixLine column)
            {
                var here = _board.At(row, column);

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading(Where(row, column) + "  ·  "
                                        + row.Titled + " × " + column.Titled),
                };

                if (_legend.IsEmpty)
                    items.Add(Parts.Entry.Saying("The legend is empty", Glyphs.Add,
                        "A cell carries a symbol, and a symbol is a setting. Make one first.",
                        AddSymbol));

                foreach (var symbol in _legend.Symbols)
                {
                    var one = symbol;

                    items.Add(Parts.Entry.Saying(
                        one.Symbol + (string.Equals(one.Symbol, here, StringComparison.OrdinalIgnoreCase)
                            ? "  ·  in this cell now" : string.Empty),
                        Glyphs.Tick,
                        MatrixNaming.Worded(one.Test) + " at " + Millimetres(one.Tolerance)
                        + (one.Enabled ? string.Empty : ", turned off"),
                        () =>
                        {
                            _board.Set(row, column, one.Symbol);
                            Repaint(row, column);
                        }));
                }

                if (here != null)
                {
                    items.Add(Parts.Entry.Rule());

                    items.Add(Parts.Entry.Act("Leave it untested", Glyphs.Close, () =>
                    {
                        _board.Set(row, column, null);
                        Repaint(row, column);
                    }));
                }

                items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Act("Fill the whole grid with this", Glyphs.Table, () =>
                {
                    if (here == null) return;

                    _board.Fill(here);
                    RepaintAll();
                }));

                return items;
            }

            // ---------------------------------------------------------------------------
            // The legend
            // ---------------------------------------------------------------------------

            private void RedrawLegend()
            {
                _legendRows.Children.Clear();

                if (_legend.IsEmpty)
                {
                    _legendRows.Children.Add(Ui.Small("No symbols yet. A symbol is a kind of test "
                        + "and a tolerance — A for a hard clash at 10 mm, B for a clearance at "
                        + "50 mm — and a cell carrying it becomes a test with those settings."));

                    return;
                }

                _legendRows.Children.Add(Ui.Small(
                    "Press a symbol to put it on the brush, then click or drag across the cells "
                    + "it belongs in. Changing a symbol here changes every cell that uses it."));

                foreach (var symbol in _legend.Symbols)
                {
                    var one = symbol;

                    // A grid of fixed columns, not a wrap panel.
                    //
                    // A wrap panel lays its children out at their own sizes and tops them out on
                    // each line, so the symbol, the kind, the tolerance, the switch and the bin
                    // all sat at different heights - and the tolerance sat lower still, because
                    // it was in a field, which puts a label over the box. One row, one line, one
                    // set of columns every row shares.
                    var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };

                    foreach (var width in new[] { 26.0, 160.0, 76.0, 30.0, 60.0, 30.0 })
                        row.ColumnDefinitions.Add(new ColumnDefinition
                        {
                            Width = new GridLength(width),
                        });

                    void At(UIElement cell, int column, double right = 8)
                    {
                        if (cell is FrameworkElement held)
                        {
                            held.VerticalAlignment = VerticalAlignment.Center;
                            held.HorizontalAlignment = HorizontalAlignment.Stretch;
                            held.Margin = new Thickness(0, 0, right, 0);
                        }

                        Grid.SetColumn(cell, column);
                        row.Children.Add(cell);
                    }

                    var on = _brush != null
                             && string.Equals(_brush.Symbol, one.Symbol, StringComparison.Ordinal);

                    // The swatch is the brush. Pressing it arms the symbol, pressing it again
                    // puts it down, and while it is armed it says so rather than looking like
                    // every other swatch in the list.
                    var mark = new Border
                    {
                        Height = 24,
                        CornerRadius = new CornerRadius(5),
                        BorderThickness = new Thickness(on ? 2 : 0),
                        Cursor = Cursors.Hand,
                        ToolTip = on
                            ? Quoted(one.Symbol) + " is on the brush. Press to put it down."
                            : "Press to put " + Quoted(one.Symbol) + " on the brush, then click "
                              + "or drag across the cells it belongs in.",
                        Child = new TextBlock
                        {
                            Text = one.Symbol,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    };

                    mark.Tint(Border.BackgroundProperty, one.Enabled ? "AccentWash" : "Nav");
                    mark.Tint(Border.BorderBrushProperty, "Accent");

                    mark.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                    {
                        e.Handled = true;
                        Arm(one);
                    });

                    At(mark, 0);

                    At(Ui.Choice(
                        new[] { "Hard", "Hard conservative", "Clearance", "Duplicate" },
                        MatrixNaming.Worded(one.Test),
                        picked => Reset(one, Kinded(picked), one.Tolerance, one.Enabled), 90), 1);

                    var tolerance = Ui.Text(Millimetres(one.Tolerance).Replace(" mm", string.Empty),
                        typed =>
                        {
                            if (double.TryParse(typed, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out var mm) && mm >= 0)
                                Reset(one, one.Test, mm / 1000, one.Enabled);
                        }, 60);

                    At(tolerance, 2, right: 6);

                    // The unit beside the box rather than a label over it, so the row stays one
                    // line high and the number is read with what it is measured in.
                    At(new TextBlock { Text = "mm" }.Wearing("Hint"), 3);

                    At(Ui.Check("On", one.Enabled,
                        on => Reset(one, one.Test, one.Tolerance, on)), 4);

                    At(Ui.Icon(Glyphs.Bin, "Take this symbol out of the legend",
                        () => Reset(one, one.Test, one.Tolerance, one.Enabled, drop: true)), 5,
                       right: 0);

                    _legendRows.Children.Add(row);
                }
            }

            private static MatrixTest Kinded(string said) =>
                said == "Clearance" ? MatrixTest.Clearance
                : said == "Duplicate" ? MatrixTest.Duplicate
                : said == "Hard conservative" ? MatrixTest.HardConservative
                : MatrixTest.Hard;

            /// <summary>
            /// Change one symbol's meaning, keeping every other symbol as it was.
            ///
            /// The legend is immutable, so an edit is a new legend built from the old one. Every
            /// cell carrying that symbol changes meaning at once, which is the point of a legend
            /// and is why the cells are repainted rather than the grid rebuilt.
            /// </summary>
            private void Reset(MatrixSymbol symbol, MatrixTest test, double tolerance,
                               bool enabled, bool drop = false)
            {
                var kept = _legend.Symbols
                    .Where(s => !string.Equals(s.Symbol, symbol.Symbol,
                                               StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!drop) kept.Add(new MatrixSymbol(symbol.Symbol, test, tolerance, enabled));

                _legend = new MatrixLegend(kept);

                RedrawLegend();
                RepaintAll();
            }

            private void AddSymbol() =>
                Parts.Ask(_grid, "What does the sheet call it? A letter, a mark, a word.",
                    Suggested(), "Add", symbol =>
                    {
                        var text = (symbol ?? string.Empty).Trim();

                        if (text.Length == 0) return;

                        _legend = new MatrixLegend(_legend.Symbols
                            .Where(s => !string.Equals(s.Symbol, text, StringComparison.OrdinalIgnoreCase))
                            .Concat(new[] { new MatrixSymbol(text) }));

                        RedrawLegend();
                        RepaintAll();
                    });

            /// <summary>The next unused letter, which is what a clash matrix's key uses.</summary>
            private string Suggested()
            {
                var taken = new HashSet<string>(_legend.Symbols.Select(s => s.Symbol),
                                                StringComparer.OrdinalIgnoreCase);

                for (var at = 0; at < 26; at++)
                {
                    var letter = ((char)('A' + at)).ToString();

                    if (!taken.Contains(letter)) return letter;
                }

                return string.Empty;
            }

            private void CoverSymbols()
            {
                var missing = _legend.Unexplained(_board);

                if (missing.Count == 0)
                {
                    Said(Ui.Done("Every symbol in the grid is already in the legend."));
                    return;
                }

                _legend = _legend.Covering(_board);

                RedrawLegend();
                RepaintAll();

                Said(Ui.Done(Ui.Count(missing.Count, "symbol")
                             + " added as an ordinary hard clash. Correct the ones that are not."));
            }


            // ---------------------------------------------------------------------------
            // Where a matrix comes from, and where it goes
            // ---------------------------------------------------------------------------

            private List<Parts.Entry> Starting() => new List<Parts.Entry>
            {
                Parts.Entry.Saying("The model's saved sets", Glyphs.Folder,
                    "A row per set you tick, named after it and pointed at it",
                    FromSets),

                Parts.Entry.Saying("The model's own tests", Glyphs.Model,
                    "Every test says what it is between, so the matrix is already there",
                    FromModel),

                Parts.Entry.Saying("A spreadsheet", Glyphs.Table,
                    "The client's own workbook, with its own symbols and a key in the corner",
                    FromSheet),

                Parts.Entry.Saying("What was saved in this project", Glyphs.Folder,
                    "The matrix as it was left last time", () => { Load(); Redraw(); }),

                Parts.Entry.Rule(),

                Parts.Entry.Act("Make the columns match the rows", Glyphs.Table, () =>
                {
                    _board.SquareOffRows();
                    Redraw();
                }),

                Parts.Entry.Danger("Start again, empty", Glyphs.Bin, () =>
                    Parts.Sure(_grid,
                        "This clears the grid and the legend. Nothing in the document changes - "
                        + "the tests already built stay exactly as they are.",
                        "Clear it", () =>
                        {
                            _board = new MatrixBoard();
                            _legend = new MatrixLegend();
                            Redraw();
                        })),
            };

            private List<Parts.Entry> Naming() => new List<Parts.Entry>
            {
                Parts.Entry.Heading("Test names: " + _pattern),

                Parts.Entry.Act("Write the pattern", Glyphs.Pencil,
                    () => Parts.Ask(_grid,
                        "How every test is named. " + string.Join(", ",
                            MatrixNaming.Tokens.Select(t => t.Token)) + " are filled in.",
                        _pattern, "Use it", typed =>
                        {
                            _pattern = string.IsNullOrWhiteSpace(typed)
                                ? MatrixNaming.Default : typed.Trim();

                            Recount();
                        })),

                Parts.Entry.Act("Put a piece into it", Glyphs.Add,
                    () => Parts.Choose(_grid, "Add to the pattern",
                        MatrixNaming.Tokens.Select(t => t.Title + "  " + t.Token).ToList(),
                        null, null, null, picked =>
                        {
                            var token = MatrixNaming.Tokens.FirstOrDefault(
                                t => picked.EndsWith(t.Token, StringComparison.Ordinal));

                            if (token == null) return;

                            _pattern = (_pattern + " " + token.Token).Trim();
                            Recount();
                        })),

                Parts.Entry.Act("Back to the plain one", Glyphs.Close, () =>
                {
                    _pattern = MatrixNaming.Default;
                    Recount();
                }),

                Parts.Entry.Rule(),

                Parts.Entry.Act("Which folder the tests go in: "
                    + (_folder.Length == 0 ? "the root" : _folder), Glyphs.Folder,
                    () => Parts.Ask(_grid, "A clash folder, or nothing for the root", _folder,
                                    "Use it", typed =>
                                    {
                                        _folder = (typed ?? string.Empty).Trim();
                                        Recount();
                                    })),
            };

            private List<Parts.Entry> Sending() => new List<Parts.Entry>
            {
                Parts.Entry.Saying("Check it against the model", Glyphs.Find,
                    "Every source, against what this model actually has", Sanity),

                Parts.Entry.Saying("See what would change", Glyphs.Compare,
                    "The difference between this matrix and the tests already built", Difference),

                Parts.Entry.Rule(),

                Parts.Entry.Saying("Save it in this project", Glyphs.Save,
                    "So it is here next time this model is opened", Keep),

                Parts.Entry.Saying("As a spreadsheet", Glyphs.Table,
                    "The grid and its legend, to send to whoever asked", Sheet),
            };

            /// <summary>Read the matrix the document is already carrying in its tests.</summary>
            private void FromModel()
            {
                if (!_session.Clash.IsAvailable)
                {
                    Warned(Ui.Problem("This edition has no clash engine, so there are no tests "
                                      + "to read a matrix out of."));
                    return;
                }

                MatrixReading? reading = null;

                Ui.Working("Reading the document's tests...", job =>
                {
                    var tests = _session.Clash.Tests();
                    var read = new List<ClashTestRead>();
                    var seen = 0;

                    foreach (var test in tests)
                    {
                        if (!job.Step(++seen, "tests read")) return;

                        read.Add(new ClashTestRead(test, _session.Clash.Sides(test.Id),
                                                   Array.Empty<ClashResultInfo>()));
                    }

                    reading = MatrixFromModel.Read(read);
                });

                if (reading == null) return;

                if (!reading.IsUsable)
                {
                    Warned(Ui.Problem(reading.Explain()));
                    return;
                }

                _board = reading.Board;
                _legend = reading.Legend;

                Redraw();

                Said(Ui.Done(reading.Explain()));

                foreach (var warning in reading.Warnings.Take(5)) Said(Ui.Note(warning), hold: true);
            }

            /// <summary>Open a matrix out of the workbook it lives in.</summary>
            private void FromSheet()
            {
                var path = Ui.AskToOpen("Open the matrix", "Excel|*.xlsx");

                if (string.IsNullOrWhiteSpace(path)) return;

                try
                {
                    var book = XlsxReader.Read(System.IO.File.ReadAllBytes(path!));
                    var sheet = book.First;

                    if (sheet == null)
                    {
                        Warned(Ui.Problem("There is nothing in that workbook."));
                        return;
                    }

                    var read = SheetMatrix.Read(sheet, new SheetMapping());

                    if (!read.IsUsable)
                    {
                        Warned(Ui.Problem(read.Explain()));
                        return;
                    }

                    _board = read.Grid;

                    // The legend the sheet's own symbols ask for, kept where it already says
                    // something. What arrives unexplained arrives as an ordinary hard clash,
                    // visibly, so the reader corrects the two that are not rather than
                    // describing all of them.
                    _legend = _legend.Covering(_board);

                    Redraw();

                    Said(Ui.Done(read.Explain()));

                    var unexplained = _legend.Symbols.Count;

                    if (unexplained > 0)
                        Said(Ui.Note("Its symbols are in the legend below as hard clashes at the "
                            + "usual tolerance. Correct the ones that are not - a clearance, a "
                            + "different tolerance - and every cell using them follows."),
                            hold: true);

                    foreach (var warning in read.Warnings.Take(5)) Said(Ui.Note(warning), hold: true);
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("That workbook could not be read: " + Ui.Describe(e)));
                }
            }

            /// <summary>Check every source against what this model actually has.</summary>
            private void Sanity()
            {
                var faults = MatrixSanity.Check(_board, _legend, SetNames(), Models());

                Quiet();

                if (faults.Count == 0)
                {
                    Said(Ui.Done("Every row and column points at something this model has, and "
                                 + "every symbol in the grid is in the legend."));
                    return;
                }

                Said(Ui.Eyebrow("What is wrong with this matrix"), hold: true);

                foreach (var fault in faults.Take(20))
                    Said(fault.Stops
                            ? Ui.Problem(fault.Where + " — " + fault.What)
                            : Ui.Note(fault.Where + " — " + fault.What),
                         hold: true);

                if (faults.Count > 20)
                    Said(Ui.Small(Ui.Count(faults.Count - 20, "more") + " not listed."), hold: true);
            }

            /// <summary>What would change in the document if this matrix were built.</summary>
            private void Difference()
            {
                if (!_session.Clash.IsAvailable)
                {
                    Warned(Ui.Problem("This edition has no clash engine, so there is nothing to "
                                      + "compare the matrix against."));
                    return;
                }

                var lines = Compare();

                Quiet();

                Said(Ui.Eyebrow("What building this would do"), hold: true);

                Said(Ui.Line(
                    Ui.Count(MatrixDiff.Count(lines, MatrixChange.Added), "test") + " to make, "
                    + MatrixDiff.Count(lines, MatrixChange.Altered) + " to change, "
                    + MatrixDiff.Count(lines, MatrixChange.Unchanged) + " already right, "
                    + MatrixDiff.Count(lines, MatrixChange.Extra) + " in the document that no "
                    + "cell asks for."), hold: true);

                foreach (var line in lines.Where(l => l.Change != MatrixChange.Unchanged).Take(20))
                    Said(Ui.Small(Worded(line.Change) + ": " + line.Name + " — " + line.What),
                         hold: true);

                Said(Ui.Small("Nothing in the document has changed. Building applies only the "
                              + "first two of those; a test no cell asks for is left alone."),
                     hold: true);
            }

            private IReadOnlyList<MatrixLineChange> Compare()
            {
                var plan = MatrixPlanner.Plan(_board, _legend, _pattern);
                var read = new List<ClashTestRead>();

                try
                {
                    foreach (var test in _session.Clash.Tests())
                        read.Add(new ClashTestRead(test, _session.Clash.Sides(test.Id),
                                                   Array.Empty<ClashResultInfo>()));
                }
                catch (Exception)
                {
                    // A document that will not list its tests is a document with nothing to
                    // compare against, which reads as "everything is new" - and that is true.
                }

                return MatrixDiff.Between(plan, read);
            }

            private static string Worded(MatrixChange change) => change switch
            {
                MatrixChange.Added => "New",
                MatrixChange.Altered => "Changed",
                MatrixChange.Extra => "Only in the document",
                _ => "Already right",
            };

            /// <summary>
            /// Build the tests the matrix asks for - and only the ones that are not right already.
            ///
            /// <b>Why it is a difference and not a rebuild.</b> A matrix is edited constantly and
            /// the results are the work: statuses, assignments, comment trails, groups, a
            /// fortnight of review. Deleting every test and making them again to move one cell
            /// throws all of that away. So what is already right is left exactly as it is, and
            /// only what actually changed is written.
            /// </summary>
            private void Build()
            {
                if (!_session.Clash.IsAvailable)
                {
                    Warned(Ui.Problem("This Navisworks edition has no clash engine, so tests "
                        + "cannot be made here. The matrix is saved and exports as it is - build "
                        + "it on a seat that has one."));
                    return;
                }

                var faults = MatrixSanity.Check(_board, _legend, SetNames(), Models());
                var stopping = MatrixSanity.Stopping(faults);

                var lines = Compare();

                var making = lines.Where(l => l.Change == MatrixChange.Added
                                              || l.Change == MatrixChange.Altered).ToList();

                if (making.Count == 0)
                {
                    Said(Ui.Done("Nothing to do: the document already holds every test this "
                                 + "matrix asks for."));
                    return;
                }

                Parts.Sure(_grid,
                    Ui.Count(making.Count, "test") + " to write into this document — "
                    + MatrixDiff.Count(lines, MatrixChange.Added) + " new and "
                    + MatrixDiff.Count(lines, MatrixChange.Altered) + " changed. "
                    + MatrixDiff.Count(lines, MatrixChange.Unchanged) + " are already right and "
                    + "are left exactly as they are, with their results."
                    + (stopping == 0
                        ? string.Empty
                        : " " + Ui.Count(stopping, "row or column") + " points at something this "
                          + "model does not have, so its cells are skipped — check it first.")
                    + (_folder.Length == 0 ? string.Empty : " They go in " + Quoted(_folder) + "."),
                    "Build them",
                    () => Write(making));
            }

            private void Write(IReadOnlyList<MatrixLineChange> making)
            {
                var made = 0;
                var refused = new List<string>();

                Ui.Working("Making the tests...", job =>
                {
                    var at = 0;

                    foreach (var line in making)
                    {
                        if (!job.Step(++at, "tests")) return;

                        if (line.Plan == null) continue;

                        var spec = line.Plan.Spec(_folder.Length == 0 ? null : _folder);

                        if (_session.Clash.Create(spec) != null) made++;
                        else if (refused.Count < 3)
                            refused.Add(line.Name + ": "
                                        + (_session.Clash.LastProblem ?? "the host refused it"));
                    }
                });

                Quiet();

                if (made > 0)
                {
                    _session.Record(ActivityKind.Regroup,
                                    "built " + Ui.Count(made, "clash test") + " from the matrix");

                    Said(Ui.Done(Ui.Count(made, "test") + " written into the document. Open the "
                                 + "Clash Manager to run them."), hold: true);
                }

                foreach (var problem in refused) Said(Ui.Problem(problem), hold: true);

                if (made == 0 && refused.Count == 0)
                    Said(Ui.Problem("Nothing was written."), hold: true);
            }

            // ---------------------------------------------------------------------------
            // Keeping it
            // ---------------------------------------------------------------------------

            private void Keep()
            {
                var json = JsonValue.Object();

                json.Set("board", _board.ToJson());
                json.Set("pattern", _pattern);

                if (_folder.Length > 0) json.Set("folder", _folder);

                json.Set("legend", JsonValue.Array(_legend.Symbols.Select(s =>
                    JsonValue.Object()
                        .Set("symbol", s.Symbol)
                        .Set("test", (double)(int)s.Test)
                        .Set("tolerance", s.Tolerance)
                        .Set("on", s.Enabled))));

                try
                {
                    _session.Store.Section(ProjectStore.ClashSection).Set(Kept, json);

                    Said(_session.Store.Save()
                        ? Ui.Done("Saved. The matrix is here the next time this project opens.")
                        : Ui.Problem(_session.Store.LastSaveProblem
                                     ?? "The project file could not be written."));
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("It could not be saved: " + Ui.Describe(e)));
                }
            }

            private void Load()
            {
                try
                {
                    var json = _session.Store.Section(ProjectStore.ClashSection)[Kept];

                    if (json == null || json.Kind != JsonKind.Object) return;

                    _board = MatrixBoard.FromJson(json["board"]);

                    var pattern = json["pattern"].AsString();

                    if (!string.IsNullOrWhiteSpace(pattern)) _pattern = pattern!;

                    _folder = json["folder"].AsString() ?? string.Empty;

                    _legend = new MatrixLegend(json["legend"].Items.Select(held =>
                        new MatrixSymbol(held["symbol"].AsString() ?? string.Empty,
                                         (MatrixTest)(int)held["test"].AsDouble(),
                                         held["tolerance"].AsDouble(),
                                         held["on"].AsBool(true))));
                }
                catch (Exception)
                {
                    // A project that will not give up its matrix is an empty grid, which is what
                    // this would have been anyway.
                }
            }

            private void Sheet()
            {
                var path = Ui.AskToSave("Save the matrix", "Excel|*.xlsx", "clash-matrix.xlsx");

                if (string.IsNullOrWhiteSpace(path)) return;

                try
                {
                    System.IO.File.WriteAllBytes(path!,
                        XlsxWriter.Write(new[] { MatrixSanity.AsSheet(_board, _legend) }));

                    Said(Ui.Done("Saved. It carries the grid and the legend under it."));
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("It could not be written: " + Ui.Describe(e)));
                }
            }

            // ---------------------------------------------------------------------------
            // What the document has, asked for without walking anything
            // ---------------------------------------------------------------------------

            /// <summary>One saved set, as the picker needs it.</summary>
            private sealed class SetLine
            {
                internal SetLine(string name, bool search, int members)
                {
                    Name = name; IsSearch = search; Members = members;
                }

                internal string Name { get; }
                internal bool IsSearch { get; }
                internal int Members { get; }
            }

            private List<SetLine> Sets()
            {
                try
                {
                    // The document's own list, which it answers instantly. A search set is one
                    // the host holds as a query rather than as a fixed list of items, and the
                    // difference matters: a search set re-runs every time the test does.
                    return _session.Model.Sets()
                        .Select(set => new SetLine(
                            set.DisplayName ?? string.Empty,
                            !set.HasExplicitModelItems,
                            set.HasExplicitModelItems && set.ExplicitModelItems != null
                                ? set.ExplicitModelItems.Count
                                : 0))
                        .Where(s => s.Name.Length > 0)
                        .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(s => s.Name, StringComparer.CurrentCulture)
                        .ToList();
                }
                catch (Exception)
                {
                    return new List<SetLine>();
                }
            }

            private IReadOnlyCollection<string> SetNames() => Sets().Select(s => s.Name).ToList();

            private List<string> Models()
            {
                try
                {
                    return _session.Model.Models
                        .Select(m => m.DisplayName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.CurrentCulture)
                        .ToList();
                }
                catch (Exception)
                {
                    return new List<string>();
                }
            }

            /// <summary>Add a line to the notices, opening the strip if it was away.</summary>
            /// <param name="note">The line.</param>
            /// <param name="hold">True to keep the lines already there.</param>
            private void Said(UIElement note, bool hold = false)
            {
                if (!hold) _notes.Children.Clear();

                _notes.Children.Add(note);
                _said.Visibility = Visibility.Visible;
            }

            /// <summary>Say one thing that went wrong, in place of everything else.</summary>
            /// <param name="problem">What went wrong.</param>
            private void Warned(UIElement problem) => Said(problem);

            private UIElement Adding(string what, Action onAdd)
            {
                var add = Ui.Icon(Glyphs.Add, what, onAdd);

                add.Margin = new Thickness(6);
                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.VerticalAlignment = VerticalAlignment.Top;

                return add;
            }

            private static string Millimetres(double metres) =>
                (metres * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm";

            private static string Quoted(string said) => "“" + said + "”";
        }
    }
}
