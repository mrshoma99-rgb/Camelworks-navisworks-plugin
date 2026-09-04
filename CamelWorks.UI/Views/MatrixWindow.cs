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

            /// <summary>The strip over the grid: what is armed, where the cursor is, undo.</summary>
            private readonly StackPanel _strip =
                new StackPanel { Orientation = Orientation.Horizontal };

            private readonly Border _grid = new Border();

            /// <summary>
            /// The palette rail: the symbols, as keys to paint with.
            ///
            /// <b>A legend under the grid was the wrong thing in the wrong place.</b> It was a
            /// key - a thing you read - drawn as an editor: a dropdown, a number field, a tick
            /// and a bin per symbol, docked to the foot of the window. On a matrix of any size
            /// the grid pushed it off the screen entirely, so the palette you paint from was not
            /// on the screen you paint on. Beside the grid, as a column of keys, it is where the
            /// hand already is, it says which keystroke each symbol answers to, and editing one
            /// happens in the inspector like everything else.
            /// </summary>
            private readonly StackPanel _palette = Ui.Stack();

            /// <summary>What the last thing pressed has to say, down the right.</summary>
            private readonly StackPanel _inspector = Ui.Stack();

            /// <summary>How much of the matrix is decided, at the foot of the palette.</summary>
            private readonly StackPanel _progress = Ui.Stack();

            /// <summary>
            /// The frozen bands and the cells that slide under them.
            ///
            /// <b>Nothing else on this screen mattered as much.</b> The grid was one panel in one
            /// scroller: scroll right on a matrix of twenty-two and the row names go with it, so
            /// the cell under the pointer belongs to a row nobody can name any more; scroll down
            /// and the columns go the same way. Every symbol after the first screenful was a
            /// guess. So the headers are their own two bands, clipped and slid by the cells'
            /// own scroll, and they never leave.
            /// </summary>
            private readonly Grid _across = new Grid();

            private readonly Grid _down = new Grid();
            private readonly Grid _sheet = new Grid();
            private readonly ScrollViewer _scroll = new ScrollViewer();

            private readonly System.Windows.Media.TranslateTransform _slidX =
                new System.Windows.Media.TranslateTransform();

            private readonly System.Windows.Media.TranslateTransform _slidY =
                new System.Windows.Media.TranslateTransform();

            /// <summary>The ring round the cell the keyboard is on.</summary>
            private readonly Border _ring = new Border();

            /// <summary>The corner and the left band, whose width follows what the rows carry.</summary>
            private readonly Border _corner = new Border();

            private readonly Border _side = new Border();

            /// <summary>The band across the top, whose height follows what the columns carry.</summary>
            private readonly Border _band = new Border();

            /// <summary>
            /// The box under the last row that makes a row out of every name typed into it.
            ///
            /// A row by hand cost four dialogues - add it, name it, number it, point it - times
            /// however many disciplines there are. Typed here it costs a name and a Return, and
            /// the box stays open for the next one.
            /// </summary>
            private readonly TextBox _naming = new TextBox();

            /// <summary>The three-part frame, built once and kept across redraws.</summary>
            private UIElement? _frame;

            /// <summary>Where the keyboard is, or -1 for nowhere.</summary>
            private int _atRow = -1;
            private int _atColumn = -1;

            /// <summary>What the inspector is showing: a line, a symbol, or the cell.</summary>
            private MatrixLine? _picked;
            private bool _pickedRows;
            private MatrixSymbol? _pickedSymbol;

            /// <summary>
            /// What the matrix was before each act that changed it, and what was undone.
            ///
            /// Fill the grid, sweep a row, drag across twenty cells, start again: every one of
            /// them is destructive and none of them could be taken back. Both stacks hold the
            /// board and the legend together, because an edit to a symbol changes what every
            /// cell carrying it means.
            /// </summary>
            private readonly Stack<JsonValue> _past = new Stack<JsonValue>();

            private readonly Stack<JsonValue> _future = new Stack<JsonValue>();
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

            /// <summary>
            /// The screen: the notices, then the three parts that make a matrix.
            ///
            /// <b>Left, the palette; middle, the grid; right, what you last pressed.</b> All
            /// three are always there and none of them moves, so the two things the job is made
            /// of - choose a symbol, put it in a cell - are a press apart, and the third thing -
            /// say what a row or a symbol actually is - stopped being four dialogues.
            /// </summary>
            private UIElement Body()
            {
                _grid.Padding = new Thickness(0);

                var stack = new DockPanel { LastChildFill = true };

                var top = Head();

                DockPanel.SetDock(top, Dock.Top);
                stack.Children.Add(top);

                stack.Children.Add(Ui.Loose(_grid));

                return stack;
            }

            /// <summary>The three parts, built once: the rail, the sheet, the inspector.</summary>
            private UIElement Frame()
            {
                if (_frame != null) return _frame;

                var three = new Grid();

                three.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                three.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });
                three.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var rail = new Border
                {
                    Width = 190,
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Child = Rail(),
                };

                rail.Tint(Border.BackgroundProperty, "Pane");
                rail.Tint(Border.BorderBrushProperty, "Bd");

                Grid.SetColumn(rail, 0);
                three.Children.Add(rail);

                var middle = Middle();

                Grid.SetColumn(middle, 1);
                three.Children.Add(middle);

                var side = new Border
                {
                    Width = 252,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Child = new ScrollViewer
                    {
                        Content = Ui.Loose(_inspector),
                        Padding = new Thickness(10, 8, 10, 10),
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    },
                };

                side.Tint(Border.BackgroundProperty, "Pane");
                side.Tint(Border.BorderBrushProperty, "Bd");

                Grid.SetColumn(side, 2);
                three.Children.Add(side);

                _frame = three;

                return three;
            }

            /// <summary>The palette rail: what to paint with, and how much is left to do.</summary>
            private UIElement Rail()
            {
                var rail = new DockPanel { LastChildFill = true };

                var said = Ui.Stack();

                var titled = Ui.Eyebrow("Palette");

                titled.Margin = new Thickness(10, 9, 10, 3);
                said.Children.Add(titled);

                var how = Ui.Small("Press one, then click or drag over the cells. Or put the "
                                   + "cursor in the grid and type its key.");

                how.TextWrapping = TextWrapping.Wrap;
                how.Margin = new Thickness(10, 0, 10, 7);
                said.Children.Add(how);

                DockPanel.SetDock(said, Dock.Top);
                rail.Children.Add(said);

                var foot = new Border
                {
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(10, 8, 10, 9),
                    Child = Ui.Loose(_progress),
                };

                foot.Tint(Border.BorderBrushProperty, "Bd");

                DockPanel.SetDock(foot, Dock.Bottom);
                rail.Children.Add(foot);

                rail.Children.Add(new ScrollViewer
                {
                    Content = Ui.Loose(_palette),
                    Padding = new Thickness(6, 0, 6, 8),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });

                return rail;
            }

            /// <summary>The strip, and the sheet under it.</summary>
            private UIElement Middle()
            {
                var middle = new DockPanel { LastChildFill = true };

                var bar = new Border
                {
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10, 5, 8, 5),
                    Child = Ui.Loose(_strip),
                };

                bar.Tint(Border.BackgroundProperty, "Card2");
                bar.Tint(Border.BorderBrushProperty, "Bd");

                DockPanel.SetDock(bar, Dock.Top);
                middle.Children.Add(bar);

                middle.Children.Add(Frozen());

                return middle;
            }

            /// <summary>
            /// The sheet: a corner, two frozen bands, and the cells scrolling under them.
            ///
            /// The bands are drawn at their own size and clipped to the space that is left, and
            /// the cells' own scroll slides them - so the columns stay over the cells they name
            /// and the rows stay beside them, however far either goes.
            /// </summary>
            private UIElement Frozen()
            {
                var frame = new Grid();

                frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                frame.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star),
                });

                frame.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                frame.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                var corner = Corner();

                Grid.SetRow(corner, 0);
                Grid.SetColumn(corner, 0);
                frame.Children.Add(corner);

                _across.HorizontalAlignment = HorizontalAlignment.Left;
                _across.RenderTransform = _slidX;

                _band.Child = Ui.Loose(_across);
                _band.ClipToBounds = true;
                _band.Height = IndexHigh + HeaderHigh;
                _band.HorizontalAlignment = HorizontalAlignment.Stretch;

                _band.Tint(Border.BackgroundProperty, "Bg");

                Grid.SetRow(_band, 0);
                Grid.SetColumn(_band, 1);
                frame.Children.Add(Ui.Loose(_band));

                _down.VerticalAlignment = VerticalAlignment.Top;
                _down.RenderTransform = _slidY;

                _side.Child = Ui.Loose(_down);
                _side.ClipToBounds = true;
                _side.Tint(Border.BackgroundProperty, "Bg");
                _side.Width = IndexWide + HeaderWide;
                _side.VerticalAlignment = VerticalAlignment.Stretch;

                Grid.SetRow(_side, 1);
                Grid.SetColumn(_side, 0);
                frame.Children.Add(Ui.Loose(_side));

                _scroll.Content = Ui.Loose(_sheet);
                _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                _scroll.Focusable = true;
                _scroll.FocusVisualStyle = null;

                // <b>The white below the last row was the pane, not the sheet.</b> A grid of six
                // rows in a window that holds thirty left two thirds of the screen the same
                // colour as the cells, with no edge anywhere, so the matrix looked like a table
                // that had failed to finish drawing rather than a small matrix. The desk behind
                // it is the app's own background: the sheet now has an edge and stops where it
                // stops, and the space beneath it reads as space.
                _scroll.Tint(Control.BackgroundProperty, "Bg");

                _scroll.ScrollChanged += (s, e) =>
                {
                    _slidX.X = -_scroll.HorizontalOffset;
                    _slidY.Y = -_scroll.VerticalOffset;
                };

                _scroll.PreviewKeyDown += (s, e) => Ui.Safely(() => Typed(e));

                Grid.SetRow(_scroll, 1);
                Grid.SetColumn(_scroll, 1);
                frame.Children.Add(Ui.Loose(_scroll));

                _ring.BorderThickness = new Thickness(2);
                _ring.IsHitTestVisible = false;
                _ring.Visibility = Visibility.Collapsed;
                _ring.Tint(Border.BorderBrushProperty, "Accent");

                return frame;
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
            /// every column of a twenty-two-discipline matrix fits on a screen instead of six.
            /// </summary>
            private const double CellWide = 38;

            /// <summary>
            /// One line, not two. The source moved to the inspector, which has room for it.
            /// </summary>
            private const double CellHigh = 30;

            /// <summary>How wide the name itself gets, before the gutters beside it.</summary>
            private const double NameWide = 150;

            /// <summary>The number gutter, and the code gutter, when anything is using them.</summary>
            private const double NumberWide = 32;
            private const double CodeWide = 36;

            /// <summary>
            /// How wide the row headers are: the name, plus a gutter for whatever is in use.
            ///
            /// <b>The number used to be glued to the front of the name.</b> "04 ARC_Walls" is
            /// one string to a reader and two facts to the job, and run down twenty rows the
            /// names started at four different places because the numbers were not all the same
            /// width. Its own column lines them up and gives the name back its own left edge -
            /// and the code, which the test names are built out of, was not on the screen at
            /// all. A gutter nothing is using is not drawn, so a matrix without numbers or codes
            /// is as narrow as it was.
            /// </summary>
            private double HeaderWide =>
                NameWide + (Numbered(_board.Rows) ? NumberWide : 0)
                         + (Coded(_board.Rows) ? CodeWide : 0);

            /// <summary>Whether any of these lines carries a number of its own.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private static bool Numbered(IReadOnlyList<MatrixLine> lines)
            {
                foreach (var line in lines) if (line.Number.Length > 0) return true;

                return false;
            }

            /// <summary>Whether any of these lines carries a discipline code.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private static bool Coded(IReadOnlyList<MatrixLine> lines)
            {
                foreach (var line in lines) if (line.Code.Length > 0) return true;

                return false;
            }
            /// <summary>
            /// How tall the column band is: the turned name, plus what sits under it.
            ///
            /// The number and the code are drawn the right way up beneath each name, so the band
            /// has to be as tall as the tallest of those or the chip eats the name it belongs
            /// to. A matrix using neither pays for neither.
            /// </summary>
            private double HeaderHigh =>
                130 + (Numbered(_board.Columns) ? 15 : 0) + (Coded(_board.Columns) ? 17 : 0);

            /// <summary>How wide the index gutter down the left is, and how tall the one across.</summary>
            private const double IndexWide = 24;
            private const double IndexHigh = 20;

            /// <summary>
            /// Build the grid again. Only for a change of shape - a row added, removed or moved.
            ///
            /// Painting a symbol into a cell does not come through here: that is one border,
            /// repainted where it stands.
            /// </summary>
            private void Redraw()
            {
                _cells.Clear();

                var bare = _board.Rows.Count == 0 && _board.Columns.Count == 0;

                // Nothing drawn yet is a screen about starting, and a palette, a strip and an
                // inspector about an empty grid are three pieces of furniture in the way of it.
                _grid.Child = bare ? Empty() : Frame();

                if (!bare)
                {
                    RedrawAcross();
                    RedrawDown();
                    RedrawCells();
                }

                RedrawPalette();
                RedrawStrip();
                RedrawInspector();
                Recount();
            }

            /// <summary>The band across the top: the letters, and the names stood on their end.</summary>
            private void RedrawAcross()
            {
                _across.Children.Clear();
                _across.ColumnDefinitions.Clear();
                _across.RowDefinitions.Clear();

                _across.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IndexHigh) });
                _across.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHigh) });

                // The corner and the band it heads are as tall as the headers turned out to be.
                _corner.Height = IndexHigh + HeaderHigh;
                _band.Height = IndexHigh + HeaderHigh;

                for (var c = 0; c <= _board.Columns.Count; c++)
                    _across.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(CellWide),
                    });

                for (var c = 0; c < _board.Columns.Count; c++)
                {
                    Put(_across, Marker(Letter(c), _board.Columns[c].Titled,
                                        _board.Columns[c], rows: false), 0, c);
                    Put(_across, ColumnHead(_board.Columns[c], c), 1, c);
                }

                Put(_across, Adding("Add a column", AddColumn, column: true),
                    1, _board.Columns.Count);
            }

            /// <summary>The band down the left: the numbers, the names, and the box that adds one.</summary>
            private void RedrawDown()
            {
                _down.Children.Clear();
                _down.ColumnDefinitions.Clear();
                _down.RowDefinitions.Clear();

                _down.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IndexWide) });
                _down.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderWide) });

                // The corner and the band it heads are as wide as the headers turned out to be.
                _corner.Width = IndexWide + HeaderWide;
                _side.Width = IndexWide + HeaderWide;

                for (var r = 0; r <= _board.Rows.Count; r++)
                    _down.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHigh) });

                for (var r = 0; r < _board.Rows.Count; r++)
                {
                    var row = _board.Rows[r];

                    Put(_down, Marker(Number(r), row.Titled, row, rows: true), r, 0);
                    Put(_down, RowHead(row, r), r, 1);
                }

                var typing = Typing();

                Grid.SetRow(typing, _board.Rows.Count);
                Grid.SetColumnSpan(typing, 2);
                Grid.SetColumn(typing, 0);
                _down.Children.Add(typing);
            }

            /// <summary>The cells themselves, which are all that scrolls.</summary>
            private void RedrawCells()
            {
                _sheet.Children.Clear();
                _sheet.ColumnDefinitions.Clear();
                _sheet.RowDefinitions.Clear();

                _sheet.HorizontalAlignment = HorizontalAlignment.Left;
                _sheet.VerticalAlignment = VerticalAlignment.Top;

                for (var c = 0; c <= _board.Columns.Count; c++)
                    _sheet.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(CellWide),
                    });

                for (var r = 0; r <= _board.Rows.Count; r++)
                    _sheet.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHigh) });

                for (var r = 0; r < _board.Rows.Count; r++)
                {
                    var row = _board.Rows[r];

                    for (var c = 0; c < _board.Columns.Count; c++)
                    {
                        var column = _board.Columns[c];
                        var cell = Cell(row, column, r, c);

                        _cells[(row, column)] = cell;
                        Put(_sheet, cell, r, c);
                    }
                }

                _sheet.Children.Add(Ui.Loose(_ring));

                RedrawRing();
            }

            private static void Put(Grid grid, UIElement child, int row, int column)
            {
                Grid.SetRow(child, row);
                Grid.SetColumn(child, column);
                grid.Children.Add(child);
            }

            /// <summary>
            /// The box that makes a row out of every name typed into it.
            ///
            /// <b>Twenty rows used to be eighty presses.</b> Add a row, open its menu, name it,
            /// open it again, number it, open it again, point it - and again for the next one.
            /// A name and a Return each is the whole of it here, and the box keeps the focus so
            /// a list can be typed straight down without touching the mouse. Where each row
            /// points is still a question, and it is asked once, in the inspector, of whichever
            /// row you press.
            /// </summary>
            private UIElement Typing()
            {
                _naming.Tag = "Type a name, press Return";
                _naming.Margin = new Thickness(6, 3, 6, 3);
                _naming.VerticalAlignment = VerticalAlignment.Center;

                if (!_wired)
                {
                    _wired = true;

                    _naming.KeyDown += (s, e) => Ui.Safely(() =>
                    {
                        if (e.Key != Key.Return) return;

                        e.Handled = true;

                        var name = _naming.Text.Trim();

                        if (name.Length == 0) return;

                        Remember();

                        _board.AddRow(new MatrixLine(name, Next(_board.Rows)));

                        Seed();

                        _naming.Text = string.Empty;

                        Redraw();

                        // The box is rebuilt into the band on every redraw, so the focus has to
                        // be put back or a list of names is one name and a hunt for the box.
                        _naming.Focus();
                    });
                }

                var holder = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(2, 0, 2, 0),
                    Child = Ui.Loose(_naming),
                };

                holder.Tint(Border.BorderBrushProperty, "Bd");
                holder.Tint(Border.BackgroundProperty, "Card");

                return holder;
            }

            /// <summary>Whether the typing box has been given its Return handler yet.</summary>
            private bool _wired;

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
                        Remember();

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
                var corner = _corner;

                corner.BorderThickness = new Thickness(0, 0, 1, 1);
                corner.Padding = new Thickness(8, 6, 8, 6);
                corner.Width = IndexWide + HeaderWide;
                corner.Height = IndexHigh + HeaderHigh;

                corner.Tint(Border.BorderBrushProperty, "Bd");
                corner.Tint(Border.BackgroundProperty, "Pane");

                corner.Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Children =
                    {
                        Ui.Eyebrow("Side A  ↓"),
                        Ui.Small("Side B  →"),
                    },
                };

                return Ui.Loose(corner);
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

            /// <summary>
            /// One row's header: its name, and a press that puts it in the inspector.
            ///
            /// <b>It used to open a menu of fourteen lines</b>, four of which opened a dialogue
            /// of their own. The inspector says all of it at once and lets it be edited where it
            /// stands, so pressing a row is now how you read one as well as how you change it.
            /// The menu is still on the right button, where a menu belongs.
            /// </summary>
            private UIElement RowHead(MatrixLine line, int at)
            {
                var head = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ToolTip = "Row " + Number(at) + ": " + line.Titled + " — "
                              + line.Source.Kinded() + ", " + line.Source.Describe(),
                };

                head.Tint(Border.BorderBrushProperty, "Bd");
                head.Tint(Border.BackgroundProperty,
                          ReferenceEquals(_picked, line) && _pickedRows ? "NavActive" : "Pane");

                var words = new Grid();

                var numbered = Numbered(_board.Rows);
                var coded = Coded(_board.Rows);

                if (numbered)
                    words.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(NumberWide),
                    });

                if (coded)
                    words.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(CodeWide),
                    });

                words.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                var column = 0;

                if (numbered)
                {
                    var number = new TextBlock
                    {
                        Text = line.Number,
                        FontSize = 11,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    }.Wearing("Hint");

                    Grid.SetColumn(number, column++);
                    words.Children.Add(number);
                }

                if (coded)
                {
                    // A chip, not another word: the code is what the test names are built out
                    // of, and it wants to be told apart from the name at a glance.
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(2, 5, 6, 5),
                        Padding = new Thickness(0, 0, 0, 1),
                        Child = new TextBlock
                        {
                            Text = line.Code,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        }.Wearing("Lbl"),
                    };

                    if (line.Code.Length > 0) chip.Tint(Border.BackgroundProperty, "Nav");

                    Grid.SetColumn(chip, column++);
                    words.Children.Add(chip);
                }

                var named = new DockPanel
                {
                    LastChildFill = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(numbered || coded ? 0 : 8, 0, 6, 0),
                };

                var mark = Ui.Glyph(line.IsUnbacked ? Glyphs.Warn : Glyphs.Layers, 12,
                                    line.IsUnbacked ? "RedTx" : "Muted", new Thickness(0, 0, 6, 0));

                mark.VerticalAlignment = VerticalAlignment.Center;
                named.Children.Add(mark);

                named.Children.Add(new TextBlock
                {
                    Text = line.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                }.Wearing("Body"));

                Grid.SetColumn(named, column);
                words.Children.Add(named);

                head.Child = words;

                Draggable(head, line, rows: true);
                Picks(head, line, rows: true, at: at);

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
                              + line.Source.Kinded() + ", " + line.Source.Describe(),
                };

                head.Tint(Border.BorderBrushProperty, "Bd");
                head.Tint(Border.BackgroundProperty,
                          ReferenceEquals(_picked, line) && !_pickedRows ? "NavActive" : "Pane");

                // Stood on its end, reading upwards, in a box of its own size.
                //
                // <b>Wrapping did not work</b>, and neither did turning it and letting the
                // layout work the size out. A column had to be a hundred pixels wide to hold two
                // words of a name, and the names were cut off anyway - "WSC_Process st...".
                // Turned through ninety degrees with a LayoutTransform they came out cut off
                // after three letters instead: a rotated child is measured against the
                // inverse-transformed space it is given, and inside a stack in a cell forty
                // pixels wide that arithmetic ends with the name's length limited by the
                // column's width. Which is the constraint the rotation was there to escape.
                //
                // So the text is given the box it is to occupy - as long as the header is tall,
                // as thick as the column is wide - and turned about its own centre for the
                // drawing only.
                var name = new TextBlock
                {
                    Text = line.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,

                    Width = HeaderHigh - Under(line) - (line.IsUnbacked ? 14 : 0),
                    Height = CellWide - 6,

                    // Read up towards the grid, so every name ends where its column begins
                    // rather than each one starting at a different height.
                    TextAlignment = TextAlignment.Right,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new System.Windows.Media.RotateTransform(-90),
                };

                var stacked = new Grid();

                // The name is turned; the number and the code are not. Two characters read
                // perfectly well the right way up, and stood on their end beside a name they
                // were three more letters of the same vertical smear.
                stacked.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star),
                });

                stacked.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(Under(line)),
                });

                var turned = name.Wearing("Body");

                Grid.SetRow(turned, 0);
                stacked.Children.Add(turned);

                var beneath = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

                if (line.Number.Length > 0)
                    beneath.Children.Add(new TextBlock
                    {
                        Text = line.Number,
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    }.Wearing("Hint"));

                if (line.Code.Length > 0)
                {
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(3, 2, 3, 0),
                        Child = new TextBlock
                        {
                            Text = line.Code,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        }.Wearing("Lbl"),
                    };

                    chip.Tint(Border.BackgroundProperty, "Nav");
                    beneath.Children.Add(chip);
                }

                if (line.IsUnbacked)
                {
                    var warn = Ui.Glyph(Glyphs.Warn, 10, "RedTx", new Thickness(0, 2, 0, 0));

                    warn.HorizontalAlignment = HorizontalAlignment.Center;
                    beneath.Children.Add(warn);
                }

                Grid.SetRow(beneath, 1);
                stacked.Children.Add(beneath);

                head.Child = stacked;

                Draggable(head, line, rows: false);
                Picks(head, line, rows: false, at: at);

                return head;
            }

            /// <summary>How much of a column header is taken by what is drawn the right way up.</summary>
            /// <param name="line">The column.</param>
            private static double Under(MatrixLine line) =>
                (line.Number.Length > 0 ? 15.0 : 0)
                + (line.Code.Length > 0 ? 17.0 : 0)
                + (line.IsUnbacked ? 14.0 : 0);

            /// <summary>
            /// A header press puts its line in the inspector; the right button keeps the menu.
            /// </summary>
            /// <param name="head">The header.</param>
            /// <param name="line">What it stands for.</param>
            /// <param name="rows">True for a row.</param>
            /// <param name="at">Where it is in its axis.</param>
            private void Picks(Border head, MatrixLine line, bool rows, int at)
            {
                head.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    Ui.Safely(() => Pick(line, rows));
                };

                head.ContextMenu = Parts.Context(() => LineMenu(line, at, rows), null);
            }

            /// <summary>Show one row or column in the inspector.</summary>
            /// <param name="line">The row or column.</param>
            /// <param name="rows">True for a row.</param>
            private void Pick(MatrixLine? line, bool rows)
            {
                _picked = line;
                _pickedRows = rows;
                _pickedSymbol = null;

                RedrawAcross();
                RedrawDown();
                RedrawInspector();
            }

            /// <summary>One cell: the symbol in it, and every way of changing it.</summary>
            private Border Cell(MatrixLine row, MatrixLine column, int down, int across)
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
                    Land(down, across);

                    if (_brush == null) return;

                    e.Handled = true;

                    // One remembering per gesture, not one per cell: a drag across twenty cells
                    // is one thing somebody did and one press of undo should take it back.
                    Remember();
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

                    if (_brush == null) Land(down, across);
                });

                // The whole menu is still one right-click away while a brush is armed.
                cell.ContextMenu = Parts.Context(() => CellMenu(row, column), null);

                return cell;
            }

            /// <summary>Put the cursor on one cell, and the keyboard on the grid.</summary>
            /// <param name="down">Which row.</param>
            /// <param name="across">Which column.</param>
            private void Land(int down, int across)
            {
                _atRow = down;
                _atColumn = across;

                _scroll.Focus();

                RedrawRing();
                RedrawStrip();

                if (_picked == null && _pickedSymbol == null) RedrawInspector();
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

                Remember();

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

                RedrawStrip();
                RedrawPalette();
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

                // <b>The mirrored half carries no letter at all now.</b> Drawn at forty-five
                // per cent it was still four hundred and eighty letters on a screen that only
                // has two hundred and thirty-one decisions in it, and every one of them read as
                // something to check. Flat and empty, the dead triangle is one shape the eye
                // steps over - and it still takes a click, which writes the pair, because
                // somebody handed the lower triangle should not have to know which half this
                // drawing kept.
                if (mirrored)
                {
                    cell.Child = null;
                    cell.Tint(Border.BackgroundProperty, "Nav");
                    cell.Opacity = 0.5;

                    var pair = _board.MirrorOf(row, column);

                    cell.ToolTip = pair == null
                        ? at
                        : "The same test as " + pair.Value.Row.Titled + " against "
                          + pair.Value.Column.Titled + ", which is the cell above the diagonal. "
                          + "A matrix is mirrored, so this pair is only ever run once. Putting a "
                          + "symbol here still puts it in both.";

                    return;
                }

                cell.Opacity = 1.0;

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

                _count.Text = plan.Explain();
                _count.ToolTip = plan.Tests.Count == 0
                    ? null
                    : "First one: " + Quoted(plan.Tests[0].Name);

                RedrawProgress();
            }

            /// <summary>
            /// How many of the pairs that can be decided have been.
            ///
            /// <b>"231 tests" was not the number anybody needed.</b> A matrix is finished when
            /// every pair has been ruled on - tested this way, or deliberately not tested - and
            /// the question at every point in the afternoon is how many are left. So the rail
            /// counts the cells in the half that is live, says how many still carry nothing, and
            /// offers to take the cursor to the first of them.
            /// </summary>
            private void RedrawProgress()
            {
                _progress.Children.Clear();

                if (_board.Rows.Count == 0 || _board.Columns.Count == 0) return;

                var live = 0;
                var done = 0;

                foreach (var row in _board.Rows)
                foreach (var column in _board.Columns)
                {
                    if (!_board.IsKeptHalf(row, column)) continue;

                    live++;

                    if (_board.At(row, column) != null) done++;
                }

                _progress.Children.Add(Ui.Eyebrow("Decided"));

                var track = new Border
                {
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 5, 0, 4),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                track.Tint(Border.BackgroundProperty, "Nav");

                var filled = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = live == 0 ? 0 : Math.Max(0, 168.0 * done / live),
                };

                filled.Tint(Border.BackgroundProperty, "Accent");
                track.Child = filled;

                _progress.Children.Add(track);

                var left = live - done;

                _progress.Children.Add(Ui.Small(done.ToString(CultureInfo.InvariantCulture)
                    + " of " + live.ToString(CultureInfo.InvariantCulture) + " pairs"
                    + (left == 0 ? ". All of them." : ", " + Ui.Count(left, "still open"))));

                if (left > 0)
                {
                    var go = Ui.Quiet("Go to the first open one", Undecided);

                    go.HorizontalAlignment = HorizontalAlignment.Left;
                    go.Margin = new Thickness(0, 6, 0, 0);

                    _progress.Children.Add(go);
                }
            }

            /// <summary>Put the cursor on the first pair nobody has ruled on.</summary>
            private void Undecided()
            {
                for (var r = 0; r < _board.Rows.Count; r++)
                for (var c = 0; c < _board.Columns.Count; c++)
                {
                    if (!_board.IsKeptHalf(_board.Rows[r], _board.Columns[c])) continue;
                    if (_board.At(_board.Rows[r], _board.Columns[c]) != null) continue;

                    Land(r, c);
                    Bring();

                    return;
                }
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

                        Remember();

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
                Remember();

                _board.AddRow(new MatrixLine("New row", Next(_board.Rows)));

                Seed();
                Redraw();
            }

            private void AddColumn()
            {
                Remember();

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
                        Remember();

                        if (rows) _board.MoveRow(at, at - 1); else _board.MoveColumn(at, at - 1);
                        Redraw();
                    }));

                if (at < lines.Count - 1)
                    items.Add(Parts.Entry.Act("Move it later", Glyphs.Down, () =>
                    {
                        Remember();

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
                    Remember();

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

                                Remember();

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

                                Remember();

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
                        Remember();
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
                            Remember();
                            _board.Set(row, column, one.Symbol);
                            Repaint(row, column);
                        }));
                }

                if (here != null)
                {
                    items.Add(Parts.Entry.Rule());

                    items.Add(Parts.Entry.Act("Leave it untested", Glyphs.Close, () =>
                    {
                        Remember();
                        _board.Set(row, column, null);
                        Repaint(row, column);
                    }));
                }

                items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Act("Fill the whole grid with this", Glyphs.Table, () =>
                {
                    if (here == null) return;

                    Remember();
                    _board.Fill(here);
                    RepaintAll();
                }));

                return items;
            }

            // ---------------------------------------------------------------------------
            // The legend
            // ---------------------------------------------------------------------------

            /// <summary>
            /// The palette: one key per symbol, plus the one that empties a cell.
            ///
            /// Each key says what it means and which keystroke answers to it, so the same
            /// symbol can be put down with the mouse or with a finger on a number. Editing what
            /// a symbol means happens in the inspector: a palette that is also a form is neither.
            /// </summary>
            private void RedrawPalette()
            {
                _palette.Children.Clear();

                if (_legend.IsEmpty)
                {
                    var none = Ui.Small("No symbols yet. A symbol is a kind of test and a "
                        + "tolerance — A for a hard clash at 10 mm, B for a clearance at 50 mm.");

                    none.TextWrapping = TextWrapping.Wrap;
                    none.Margin = new Thickness(4, 4, 4, 6);

                    _palette.Children.Add(none);
                }

                for (var at = 0; at < _legend.Symbols.Count; at++)
                {
                    var one = _legend.Symbols[at];
                    var armed = _brush != null
                        && string.Equals(_brush.Symbol, one.Symbol, StringComparison.Ordinal);

                    _palette.Children.Add(Swatch(
                        one.Symbol,
                        MatrixNaming.Worded(one.Test),
                        Millimetres(one.Tolerance) + (one.Enabled ? string.Empty : " · off"),
                        at < 9 ? (at + 1).ToString(CultureInfo.InvariantCulture) : one.Symbol,
                        armed,
                        one.Enabled,
                        () => { Arm(one); Show(one); }));
                }

                _palette.Children.Add(Swatch("×", "Not tested", "clears the cell", "Del",
                                          false, false, () => { Arm(null); Show(null); }));

                var add = Ui.Quiet("Add a symbol", AddSymbol);

                add.HorizontalAlignment = HorizontalAlignment.Left;
                add.Margin = new Thickness(4, 6, 0, 0);

                _palette.Children.Add(add);

                var cover = Ui.Quiet("Cover every symbol in the grid", CoverSymbols);

                cover.HorizontalAlignment = HorizontalAlignment.Left;
                cover.Margin = new Thickness(4, 2, 0, 0);

                _palette.Children.Add(cover);
            }

            /// <summary>One key on the palette, or one choice in the inspector.</summary>
            /// <param name="mark">The symbol, or the cross that clears.</param>
            /// <param name="kind">What kind of test it is.</param>
            /// <param name="says">The tolerance, or what it does.</param>
            /// <param name="stroke">The key on the keyboard that does the same thing.</param>
            /// <param name="armed">True while it is the one on the brush.</param>
            /// <param name="live">False for a symbol that is turned off, and for the cross.</param>
            /// <param name="onPress">Arms it.</param>
            private UIElement Swatch(string mark, string kind, string says, string stroke,
                                     bool armed, bool live, Action onPress)
            {
                var key = new Border
                {
                    Padding = new Thickness(5, 4, 6, 4),
                    Margin = new Thickness(0, 0, 0, 2),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(armed ? 1 : 0),
                    Cursor = Cursors.Hand,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ToolTip = armed
                        ? Quoted(mark) + " is on the brush. Press to put it down."
                        : "Press to paint with " + Quoted(mark) + ", or type " + stroke
                          + " with the cursor in the grid.",
                };

                key.Tint(Border.BorderBrushProperty, "Accent");

                if (armed) key.Tint(Border.BackgroundProperty, "Card");

                var line = new DockPanel { LastChildFill = true };

                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = mark,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };

                swatch.Tint(Border.BackgroundProperty, live ? "AccentWash" : "Nav");

                DockPanel.SetDock(swatch, Dock.Left);
                line.Children.Add(swatch);

                var typed = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    Child = new TextBlock { Text = stroke, FontSize = 10 }.Wearing("Hint"),
                };

                typed.Tint(Border.BorderBrushProperty, "BdStrong");

                DockPanel.SetDock(typed, Dock.Right);
                line.Children.Add(typed);

                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                words.Children.Add(new TextBlock
                {
                    Text = kind,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                }.Wearing("Body"));

                words.Children.Add(new TextBlock
                {
                    Text = says,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                }.Wearing("Hint"));

                line.Children.Add(words);

                key.Child = line;

                if (!armed)
                {
                    key.MouseEnter += (s, e) => key.Tint(Border.BackgroundProperty, "Hover");
                    key.MouseLeave += (s, e) =>
                        key.Background = System.Windows.Media.Brushes.Transparent;
                }

                key.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;
                    onPress();
                });

                return key;
            }

            /// <summary>Put one symbol in the inspector, or clear it out.</summary>
            /// <param name="symbol">The symbol, or null.</param>
            private void Show(MatrixSymbol? symbol)
            {
                _pickedSymbol = symbol;

                if (symbol != null) _picked = null;

                RedrawInspector();
            }

            // ---------------------------------------------------------------------------
            // The strip, the inspector, the keyboard and taking it back
            // ---------------------------------------------------------------------------

            /// <summary>What is armed, where the cursor is, how big it is, and undo.</summary>
            private void RedrawStrip()
            {
                _strip.Children.Clear();

                if (_board.Rows.Count == 0 && _board.Columns.Count == 0) return;

                void Say(UIElement said, double left = 0)
                {
                    if (said is FrameworkElement held)
                    {
                        held.VerticalAlignment = VerticalAlignment.Center;
                        held.Margin = new Thickness(left, 0, 0, 0);
                    }

                    _strip.Children.Add(said);
                }

                if (_brush == null)
                {
                    Say(Ui.Small("Nothing on the brush — press a symbol on the left, or put the "
                                 + "cursor in a cell and type its key."));
                }
                else
                {
                    Say(Ui.Glyph(Glyphs.Pencil, 12, "Accent", new Thickness(0, 0, 8, 0)));

                    Say(new TextBlock
                    {
                        Text = Quoted(_brush.Symbol) + " on the brush",
                        FontWeight = FontWeights.SemiBold,
                    }.Wearing("Body"));

                    Say(Ui.Small("Click, drag, or click a row's number for the whole line."), 10);
                    Say(Ui.Quiet("Put it down", () => Arm(null)), 10);
                }

                if (_atRow >= 0 && _atRow < _board.Rows.Count
                    && _atColumn >= 0 && _atColumn < _board.Columns.Count)
                    Say(Ui.Small("Cursor " + Where(_board.Rows[_atRow], _board.Columns[_atColumn])), 16);

                Say(Ui.Loose(_count.Wearing("Hint")), 16);

                // The box that makes rows is under the last one, which on a matrix of any size
                // is off the bottom of the screen. This is how you get to it.
                Say(Ui.Quiet("Add rows", () =>
                {
                    _scroll.ScrollToBottom();
                    _naming.Focus();
                }), 16);

                var back = Ui.Quiet("Undo", Undo);

                back.IsEnabled = _past.Count > 0;
                Say(back, 16);

                var on = Ui.Quiet("Redo", Redo);

                on.IsEnabled = _future.Count > 0;
                Say(on, 4);
            }

            /// <summary>
            /// The inspector: everything about whatever was last pressed, editable in place.
            ///
            /// <b>This is where four dialogues went.</b> Naming a row, numbering it, coding it
            /// and pointing it were four lines of a menu that each opened a box of their own; a
            /// symbol's kind and tolerance were a dropdown and a field in a strip at the bottom
            /// of the window. All of it is one panel that is always on the screen, showing the
            /// row, the column, the symbol or the cell that was last touched.
            /// </summary>
            private void RedrawInspector()
            {
                _inspector.Children.Clear();

                if (_pickedSymbol != null) { SymbolPage(_pickedSymbol); return; }
                if (_picked != null) { LinePage(_picked, _pickedRows); return; }

                if (_atRow >= 0 && _atRow < _board.Rows.Count
                    && _atColumn >= 0 && _atColumn < _board.Columns.Count)
                {
                    CellPage(_board.Rows[_atRow], _board.Columns[_atColumn]);
                    return;
                }

                _inspector.Children.Add(Ui.Eyebrow("Nothing picked"));

                var idle = Ui.Small("Press a row, a column, a symbol or a cell, and what it is "
                                    + "and what can be done to it are here.");

                idle.TextWrapping = TextWrapping.Wrap;
                idle.Margin = new Thickness(0, 4, 0, 0);

                _inspector.Children.Add(idle);
            }

            /// <summary>A titled box in the inspector, label over control, tight.</summary>
            /// <param name="label">What it is.</param>
            /// <param name="control">The control.</param>
            private void Told(string label, UIElement control)
            {
                var said = Ui.Small(label);

                said.Margin = new Thickness(0, 0, 0, 3);
                _inspector.Children.Add(said);

                if (control is FrameworkElement held)
                {
                    held.HorizontalAlignment = HorizontalAlignment.Stretch;
                    held.Margin = new Thickness(0, 0, 0, 9);
                }

                _inspector.Children.Add(control);
            }

            /// <summary>One thing that can be done, as a line of the inspector.</summary>
            /// <param name="caption">What it does.</param>
            /// <param name="onPress">Does it.</param>
            /// <param name="danger">True for the ones that take something away.</param>
            private void Doable(string caption, Action onPress, bool danger = false)
            {
                var act = danger ? Ui.Danger(caption, onPress) : Ui.Quiet(caption, onPress);

                act.HorizontalAlignment = HorizontalAlignment.Left;
                act.Margin = new Thickness(0, 0, 0, 2);

                _inspector.Children.Add(act);
            }

            /// <summary>A row or a column: what it is called, and what it points at.</summary>
            /// <param name="line">The row or column.</param>
            /// <param name="rows">True for a row.</param>
            private void LinePage(MatrixLine line, bool rows)
            {
                var at = At(rows ? _board.Rows : _board.Columns, line);

                if (at < 0) { Pick(null, rows); return; }

                var what = rows ? "row" : "column";

                _inspector.Children.Add(Ui.Eyebrow(
                    (rows ? "Row " + Number(at) : "Column " + Letter(at))));

                var gap = new Border { Height = 8 };

                _inspector.Children.Add(gap);

                Told("Name", Ui.Text(line.Name, typed =>
                {
                    line.Name = typed;

                    if (rows) RedrawDown(); else RedrawAcross();

                    Recount();
                }, 100));

                var pair = new Grid();

                pair.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });

                pair.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                var numbered = Ui.Stack();

                numbered.Children.Add(Ui.Small("Number"));

                var number = Ui.Text(line.Number, typed =>
                {
                    line.Number = typed;

                    if (rows) RedrawDown(); else RedrawAcross();

                    Recount();
                }, 40);

                number.Margin = new Thickness(0, 3, 0, 0);
                numbered.Children.Add(number);

                Grid.SetColumn(numbered, 0);
                pair.Children.Add(numbered);

                var coded = Ui.Stack();

                coded.Children.Add(Ui.Small("Code"));

                var code = Ui.Text(line.Code, typed => { line.Code = typed; Recount(); }, 40);

                code.Margin = new Thickness(0, 3, 0, 0);
                coded.Children.Add(code);

                Grid.SetColumn(coded, 2);
                pair.Children.Add(coded);

                pair.Margin = new Thickness(0, 0, 0, 9);
                _inspector.Children.Add(pair);

                _inspector.Children.Add(Ui.Small("Points at"));

                var source = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 7),
                    Margin = new Thickness(0, 3, 0, 6),
                };

                source.Tint(Border.BorderBrushProperty, line.IsUnbacked ? "AmberBd" : "Bd");
                source.Tint(Border.BackgroundProperty, line.IsUnbacked ? "AmberBg" : "Card");

                var says = Ui.Stack();

                says.Children.Add(new TextBlock
                {
                    Text = line.Source.Kinded(),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                }.Wearing("Body"));

                var describes = Ui.Small(line.IsUnbacked
                    ? "Nothing, so this " + what + "'s cells make no tests."
                    : line.Source.Describe());

                describes.TextWrapping = TextWrapping.Wrap;
                says.Children.Add(describes);

                source.Child = says;
                _inspector.Children.Add(source);

                Doable("Point it somewhere else",
                       () => Parts.PopMenu(_inspector, SourceMenu(line)));

                var live = 0;
                var done = 0;

                foreach (var other in rows ? _board.Columns : _board.Rows)
                {
                    var row = rows ? line : other;
                    var column = rows ? other : line;

                    if (!_board.IsKeptHalf(row, column)) continue;

                    live++;

                    if (_board.At(row, column) != null) done++;
                }

                var count = Ui.Small(done.ToString(CultureInfo.InvariantCulture) + " of "
                    + live.ToString(CultureInfo.InvariantCulture) + " of this " + what
                    + "'s pairs decided");

                count.Margin = new Thickness(0, 10, 0, 5);
                _inspector.Children.Add(count);

                if (_brush != null)
                    Doable("Fill the rest with " + Quoted(_brush.Symbol), () =>
                    {
                        Remember();

                        foreach (var other in rows ? _board.Columns : _board.Rows)
                        {
                            var row = rows ? line : other;
                            var column = rows ? other : line;

                            if (_board.At(row, column) == null) Daub(row, column);
                        }
                    });

                Doable("Clear the whole " + what, () =>
                {
                    Remember();

                    foreach (var other in rows ? _board.Columns : _board.Rows)
                    {
                        var row = rows ? line : other;
                        var column = rows ? other : line;

                        _board.Set(row, column, null);
                        Repaint(row, column);
                    }
                });

                // <b>Twenty disciplines are rarely twenty different things.</b> Half a matrix is
                // the same package split by level or by area, and typing the whole row again to
                // change one word in it is the sort of work a tool is supposed to remove.
                Doable("Duplicate it, source and all", () =>
                {
                    Remember();

                    var copy = new MatrixLine(line.Name + " (copy)",
                                              Next(rows ? _board.Rows : _board.Columns),
                                              line.Source)
                    {
                        Code = line.Code,
                    };

                    if (rows)
                    {
                        _board.AddRow(copy);
                        _board.MoveRow(_board.Rows.Count - 1, at + 1);
                    }
                    else
                    {
                        _board.AddColumn(copy);
                        _board.MoveColumn(_board.Columns.Count - 1, at + 1);
                    }

                    Redraw();
                    Pick(copy, rows);
                });

                Doable("Take it out", () =>
                {
                    Remember();

                    if (rows) _board.RemoveRow(line); else _board.RemoveColumn(line);

                    _picked = null;

                    Redraw();
                }, danger: true);
            }

            /// <summary>One symbol: what it means, which every cell carrying it follows.</summary>
            /// <param name="symbol">The symbol.</param>
            private void SymbolPage(MatrixSymbol symbol)
            {
                _inspector.Children.Add(Ui.Eyebrow("Symbol " + symbol.Symbol));

                var says = Ui.Small("Every cell carrying it becomes a test with these settings.");

                says.TextWrapping = TextWrapping.Wrap;
                says.Margin = new Thickness(0, 4, 0, 9);
                _inspector.Children.Add(says);

                Told("Kind of test", Ui.Choice(
                    new[] { "Hard", "Hard conservative", "Clearance", "Duplicate" },
                    MatrixNaming.Worded(symbol.Test),
                    picked => Reset(symbol, Kinded(picked), symbol.Tolerance, symbol.Enabled),
                    100));

                Told("Tolerance, in millimetres",
                    Ui.Text(Millimetres(symbol.Tolerance).Replace(" mm", string.Empty), typed =>
                    {
                        if (double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture,
                                            out var mm) && mm >= 0)
                            Reset(symbol, symbol.Test, mm / 1000, symbol.Enabled);
                    }, 60));

                var live = Ui.Check("Build tests from this symbol", symbol.Enabled,
                    on => Reset(symbol, symbol.Test, symbol.Tolerance, on));

                live.Margin = new Thickness(0, 0, 0, 8);
                _inspector.Children.Add(live);

                var using_ = 0;

                foreach (var cell in _board.Cells())
                    if (string.Equals(cell.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase))
                        using_++;

                _inspector.Children.Add(Ui.Small(Ui.Count(using_, "cell") + " carrying it"));

                var gap = new Border { Height = 8 };

                _inspector.Children.Add(gap);

                Doable("Put it in every cell", () =>
                {
                    Remember();
                    _board.Fill(symbol.Symbol);
                    RepaintAll();
                });

                Doable("Take it out of the legend",
                       () => Reset(symbol, symbol.Test, symbol.Tolerance, symbol.Enabled,
                                   drop: true),
                       danger: true);
            }

            /// <summary>One cell, and the symbols that can go in it without a menu.</summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The column.</param>
            private void CellPage(MatrixLine row, MatrixLine column)
            {
                var here = _board.At(row, column);

                _inspector.Children.Add(Ui.Eyebrow("Cell " + Where(row, column)));

                var named = Ui.Small(row.Titled + "  ×  " + column.Titled);

                named.TextWrapping = TextWrapping.Wrap;
                named.Margin = new Thickness(0, 4, 0, 9);
                _inspector.Children.Add(named);

                if (!_board.IsKeptHalf(row, column))
                {
                    var mirrored = Ui.Small("The other side of the diagonal. A pair is only run "
                                            + "once, so what goes here goes in both.");

                    mirrored.TextWrapping = TextWrapping.Wrap;
                    mirrored.Margin = new Thickness(0, 0, 0, 9);
                    _inspector.Children.Add(mirrored);
                }

                _inspector.Children.Add(Ui.Small("What is in it"));

                var gap = new Border { Height = 5 };

                _inspector.Children.Add(gap);

                foreach (var symbol in _legend.Symbols)
                {
                    var one = symbol;

                    _inspector.Children.Add(Swatch(
                        one.Symbol,
                        MatrixNaming.Worded(one.Test),
                        Millimetres(one.Tolerance)
                        + (string.Equals(one.Symbol, here, StringComparison.OrdinalIgnoreCase)
                            ? " · in this cell" : string.Empty),
                        string.Empty,
                        string.Equals(one.Symbol, here, StringComparison.OrdinalIgnoreCase),
                        one.Enabled,
                        () =>
                        {
                            Remember();
                            _board.Set(row, column, one.Symbol);
                            Repaint(row, column);
                            RedrawInspector();
                        }));
                }

                _inspector.Children.Add(Swatch("×", "Not tested", "clears the cell", string.Empty,
                    here == null, false, () =>
                    {
                        Remember();
                        _board.Set(row, column, null);
                        Repaint(row, column);
                        RedrawInspector();
                    }));
            }

            // ---------------------------------------------------------------------------
            // The keyboard
            // ---------------------------------------------------------------------------

            /// <summary>Draw the ring where the cursor is.</summary>
            private void RedrawRing()
            {
                if (_atRow < 0 || _atColumn < 0
                    || _atRow >= _board.Rows.Count || _atColumn >= _board.Columns.Count)
                {
                    _ring.Visibility = Visibility.Collapsed;
                    return;
                }

                Grid.SetRow(_ring, _atRow);
                Grid.SetColumn(_ring, _atColumn);
                _ring.Visibility = Visibility.Visible;
            }

            /// <summary>Scroll until the cursor is on the screen.</summary>
            private void Bring()
            {
                if (_atRow < 0 || _atColumn < 0) return;

                // Before the first layout there is no viewport to scroll within, and the
                // arithmetic below would put the offset at a negative number and scroll away
                // from the cell it was asked to show.
                if (_scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0) return;

                var left = _atColumn * CellWide;
                var top = _atRow * CellHigh;

                if (left < _scroll.HorizontalOffset)
                    _scroll.ScrollToHorizontalOffset(left);
                else if (left + CellWide > _scroll.HorizontalOffset + _scroll.ViewportWidth)
                    _scroll.ScrollToHorizontalOffset(left + CellWide - _scroll.ViewportWidth);

                if (top < _scroll.VerticalOffset)
                    _scroll.ScrollToVerticalOffset(top);
                else if (top + CellHigh > _scroll.VerticalOffset + _scroll.ViewportHeight)
                    _scroll.ScrollToVerticalOffset(top + CellHigh - _scroll.ViewportHeight);
            }

            /// <summary>
            /// The keyboard over the grid, which is how a grid has been filled in since 1979.
            ///
            /// Arrows move; a number puts that symbol of the legend in the cell and steps right,
            /// so a row is typed rather than dragged; a letter does the same for the symbol that
            /// is called that; Delete empties one. Ctrl+Z takes back whatever the last act was.
            /// </summary>
            /// <param name="e">What was pressed.</param>
            private void Typed(KeyEventArgs e)
            {
                if (_board.Rows.Count == 0 || _board.Columns.Count == 0) return;

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    if (e.Key == Key.Z) { Undo(); e.Handled = true; }
                    else if (e.Key == Key.Y) { Redo(); e.Handled = true; }

                    return;
                }

                if (_atRow < 0 || _atColumn < 0) { _atRow = 0; _atColumn = 0; }

                switch (e.Key)
                {
                    case Key.Left: Step(0, -1); e.Handled = true; return;
                    case Key.Right: Step(0, 1); e.Handled = true; return;
                    case Key.Up: Step(-1, 0); e.Handled = true; return;
                    case Key.Down: Step(1, 0); e.Handled = true; return;

                    case Key.Home: _atColumn = 0; Settled(); e.Handled = true; return;
                    case Key.End:
                        _atColumn = _board.Columns.Count - 1;
                        Settled();
                        e.Handled = true;
                        return;

                    case Key.Delete:
                    case Key.Back:
                    case Key.D0:
                    case Key.NumPad0:
                        Lay(null);
                        e.Handled = true;
                        return;

                    case Key.Space:
                        if (_brush != null) Lay(_brush.Symbol);
                        e.Handled = true;
                        return;
                }

                var nth = Nth(e.Key);

                if (nth >= 0 && nth < _legend.Symbols.Count)
                {
                    Lay(_legend.Symbols[nth].Symbol);
                    e.Handled = true;
                    return;
                }

                var typed = Lettered(e.Key);

                if (typed == null) return;

                foreach (var symbol in _legend.Symbols)
                    if (string.Equals(symbol.Symbol, typed, StringComparison.OrdinalIgnoreCase))
                    {
                        Lay(symbol.Symbol);
                        e.Handled = true;
                        return;
                    }
            }

            /// <summary>Which symbol of the legend a number key stands for, or -1.</summary>
            /// <param name="key">The key.</param>
            private static int Nth(Key key)
            {
                if (key >= Key.D1 && key <= Key.D9) return key - Key.D1;
                if (key >= Key.NumPad1 && key <= Key.NumPad9) return key - Key.NumPad1;

                return -1;
            }

            /// <summary>The letter a key stands for, or null.</summary>
            /// <param name="key">The key.</param>
            private static string? Lettered(Key key) =>
                key >= Key.A && key <= Key.Z
                    ? ((char)('A' + (key - Key.A))).ToString()
                    : null;

            /// <summary>Move the cursor, staying on the grid.</summary>
            /// <param name="down">Rows to move.</param>
            /// <param name="across">Columns to move.</param>
            private void Step(int down, int across)
            {
                _atRow = Math.Max(0, Math.Min(_board.Rows.Count - 1, _atRow + down));
                _atColumn = Math.Max(0, Math.Min(_board.Columns.Count - 1, _atColumn + across));

                Settled();
            }

            /// <summary>The cursor has moved: draw it, scroll to it, and say where it is.</summary>
            private void Settled()
            {
                RedrawRing();
                Bring();
                RedrawStrip();

                if (_picked == null && _pickedSymbol == null) RedrawInspector();
            }

            /// <summary>Put a symbol in the cell under the cursor and step right, as a sheet does.</summary>
            /// <param name="symbol">The symbol, or null to empty it.</param>
            private void Lay(string? symbol)
            {
                if (_atRow < 0 || _atColumn < 0
                    || _atRow >= _board.Rows.Count || _atColumn >= _board.Columns.Count) return;

                var row = _board.Rows[_atRow];
                var column = _board.Columns[_atColumn];

                Remember();

                _board.Set(row, column, symbol);
                Repaint(row, column);

                Step(0, 1);
            }

            // ---------------------------------------------------------------------------
            // Taking it back
            // ---------------------------------------------------------------------------

            /// <summary>The board and the legend together, as they are now.</summary>
            private JsonValue Held()
            {
                var json = JsonValue.Object();

                json.Set("board", _board.ToJson());
                json.Set("legend", LegendJson());

                return json;
            }

            /// <summary>Put the board and the legend back to what one of those was.</summary>
            /// <param name="json">What they were.</param>
            private void Restore(JsonValue json)
            {
                _board = MatrixBoard.FromJson(json["board"]);
                _legend = LegendOf(json["legend"]);

                _picked = null;
                _pickedSymbol = null;
                _brush = null;

                Redraw();
            }

            /// <summary>Keep what the matrix is now, so the next act can be taken back.</summary>
            private void Remember()
            {
                _past.Push(Held());
                _future.Clear();

                // An afternoon of editing is not worth a hundred megabytes of undo. Forty acts
                // is more than anybody reaches for and it is bounded.
                if (_past.Count > 40)
                {
                    var kept = _past.ToArray();

                    _past.Clear();

                    for (var at = kept.Length - 2; at >= 0; at--) _past.Push(kept[at]);
                }

                RedrawStrip();
            }

            private void Undo()
            {
                if (_past.Count == 0) return;

                _future.Push(Held());
                Restore(_past.Pop());
            }

            private void Redo()
            {
                if (_future.Count == 0) return;

                _past.Push(Held());
                Restore(_future.Pop());
            }

            /// <summary>The legend, as the project file and the undo stack both hold it.</summary>
            private JsonValue LegendJson() =>
                JsonValue.Array(_legend.Symbols.Select(s =>
                    JsonValue.Object()
                        .Set("symbol", s.Symbol)
                        .Set("test", (double)(int)s.Test)
                        .Set("tolerance", s.Tolerance)
                        .Set("on", s.Enabled)));

            /// <summary>A legend read back out of that.</summary>
            /// <param name="json">What was written.</param>
            private static MatrixLegend LegendOf(JsonValue? json) =>
                new MatrixLegend((json?.Items ?? Enumerable.Empty<JsonValue>()).Select(held =>
                    new MatrixSymbol(held["symbol"].AsString() ?? string.Empty,
                                     (MatrixTest)(int)held["test"].AsDouble(),
                                     held["tolerance"].AsDouble(),
                                     held["on"].AsBool(true))));

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
                Remember();

                var kept = _legend.Symbols
                    .Where(s => !string.Equals(s.Symbol, symbol.Symbol,
                                               StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var made = drop ? null : new MatrixSymbol(symbol.Symbol, test, tolerance, enabled);

                if (made != null) kept.Add(made);

                _legend = new MatrixLegend(kept);

                // The symbol is a new object every edit, so the brush and the inspector have to
                // be pointed at the one that exists now or the next keystroke edits a ghost.
                if (_brush != null && string.Equals(_brush.Symbol, symbol.Symbol,
                                                    StringComparison.OrdinalIgnoreCase))
                    _brush = made;

                if (_pickedSymbol != null && string.Equals(_pickedSymbol.Symbol, symbol.Symbol,
                                                           StringComparison.OrdinalIgnoreCase))
                    _pickedSymbol = made;

                RedrawPalette();
                RedrawStrip();
                RedrawInspector();
                RepaintAll();
            }

            private void AddSymbol() =>
                Parts.Ask(_grid, "What does the sheet call it? A letter, a mark, a word.",
                    Suggested(), "Add", symbol =>
                    {
                        var text = (symbol ?? string.Empty).Trim();

                        if (text.Length == 0) return;

                        Remember();

                        _legend = new MatrixLegend(_legend.Symbols
                            .Where(s => !string.Equals(s.Symbol, text, StringComparison.OrdinalIgnoreCase))
                            .Concat(new[] { new MatrixSymbol(text) }));

                        RedrawPalette();
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

                Remember();

                _legend = _legend.Covering(_board);

                RedrawPalette();
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
                    Remember();
                    _board.SquareOffRows();
                    Redraw();
                }),

                Parts.Entry.Danger("Start again, empty", Glyphs.Bin, () =>
                    Parts.Sure(_grid,
                        "This clears the grid and the legend. Nothing in the document changes - "
                        + "the tests already built stay exactly as they are.",
                        "Clear it", () =>
                        {
                            Remember();

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

                Remember();

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

                    Remember();

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

                json.Set("legend", LegendJson());

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

                    _legend = LegendOf(json["legend"]);
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

            /// <summary>
            /// The last cell of the header band: one more column.
            ///
            /// <b>A symbol floating in the white beside a grid is not a button.</b> It was a
            /// plus at the top of a header a hundred and sixty pixels tall, level with nothing,
            /// against no background - and the only way to learn what it did was to hover it.
            /// Shaped like the headers it continues, it reads as the next place rather than as a
            /// mark somebody left behind. The row it used to have a twin for is a box you type
            /// into now, which is a better answer than a button.
            /// </summary>
            /// <param name="what">What it adds, which is also what its tip says.</param>
            /// <param name="onAdd">Adds it.</param>
            /// <param name="column">True for the one at the end of the columns.</param>
            private UIElement Adding(string what, Action onAdd, bool column)
            {
                var cell = new Border
                {
                    Child = Ui.Glyph(Glyphs.Add, 13, "Accent"),
                    Cursor = Cursors.Hand,
                    ToolTip = what,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Padding = new Thickness(0, 0, 0, 8),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = column ? CellWide : 0,
                };

                if (cell.Child is FrameworkElement glyph)
                {
                    glyph.HorizontalAlignment = HorizontalAlignment.Center;
                    glyph.VerticalAlignment = VerticalAlignment.Bottom;
                }

                cell.Tint(Border.BorderBrushProperty, "Bd");

                cell.MouseEnter += (s, e) => cell.Tint(Border.BackgroundProperty, "Hover");
                cell.MouseLeave += (s, e) =>
                    cell.Background = System.Windows.Media.Brushes.Transparent;

                cell.MouseLeftButtonUp += (s, e) => Ui.Safely(onAdd);

                return cell;
            }

            private static string Millimetres(double metres) =>
                (metres * 1000).ToString("0.###", CultureInfo.InvariantCulture) + " mm";

            private static string Quoted(string said) => "“" + said + "”";
        }
    }
}
