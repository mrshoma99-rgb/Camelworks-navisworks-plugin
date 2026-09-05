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
    /// The clash matrix: draw one, read one out of the model, or open one from a
    /// spreadsheet, and build the clash tests it asks for.
    ///
    /// The matrix is the record and the tests are derived from it. The grid is editable
    /// whatever it came from, it saves into the project, it exports as a spreadsheet, and
    /// rebuilding changes only what actually changed rather than discarding statuses and
    /// comments.
    ///
    /// Rows are side A, columns are side B. Both axes are independent lists; the square grid
    /// is the case where they hold the same items. This allows, for example, four packages
    /// against every discipline.
    ///
    /// Nothing here walks the model. Listing sets, listing loaded models and reading tests and
    /// their sides are all document queries. Capturing a selection reads the current selection.
    /// Only the build has a cost, and it is explicit.
    /// </summary>
    public static class MatrixWindow
    {
        // The matrix survives a tab switch, like every other bench in this app: an afternoon's
        // grid lost to a glance at another tab is an afternoon nobody offers this tool again.
        private static MatrixBoard _board = new MatrixBoard();

        /// <summary>
        /// Whether the seat's standing notice has been dismissed for this session.
        ///
        /// A seat with no clash engine showed the notice on every open with no way to dismiss
        /// it. Held in memory rather than persisted: the condition is a property of the
        /// Navisworks edition, and a seat that gains a clash engine is a different session.
        /// </summary>
        private static bool _hushed;
        private static MatrixLegend _legend = new MatrixLegend();
        private static string _pattern = MatrixNaming.Default;
        private static string _folder = string.Empty;

        /// <summary>
        /// The tab, on whatever seat it is running in.
        ///
        /// Takes a seat rather than a Session, which is what lets the render harness build this
        /// screen - the shipping screen, not a drawing of it - without Navisworks anywhere near
        /// the machine.
        /// </summary>
        /// <param name="seat">The host it is running in.</param>
        /// <summary>The matrix tab, arriving at the start screen.</summary>
        /// <param name="seat">The seat it is running in.</param>
        public static UIElement Surface(ISeat seat) => Surface(seat, null);

        /// <summary>
        /// The matrix tab, ready to be put in a window.
        /// </summary>
        /// <param name="seat">The seat it is running in.</param>
        /// <param name="drawn">
        /// A matrix already open, or null to arrive at the start screen.
        ///
        /// Supplied so the render harness can capture the populated grid; a seat with no
        /// document has no board, so otherwise only the start screen can be photographed. The
        /// board is the product's own type, the same object a saved matrix is read into.
        /// </param>
        public static UIElement Surface(ISeat seat, MatrixBoard? drawn) =>
            Surface(seat, drawn, Showing.Symbols);

        /// <summary>
        /// The matrix tab, opened on one of its three views.
        /// </summary>
        /// <param name="seat">The seat it is running in.</param>
        /// <param name="drawn">A matrix already open, or null to arrive at the start screen.</param>
        /// <param name="view">
        /// Which of the three the grid starts on.
        ///
        /// Here for the render harness, which cannot press the chooser: a view nobody can
        /// photograph is a view whose drawing faults are found by a coordinator on a licensed
        /// seat and described to me in words. The results view is not offered - it counts what
        /// the document holds, and a harness seat holds no document - so asking for it opens on
        /// the symbols.
        /// </param>
        public static UIElement Surface(ISeat seat, MatrixBoard? drawn, Showing view) =>
            Surface(seat, drawn, view, null);

        /// <summary>
        /// The matrix tab, opened on one of its three views, over tests that have already been
        /// read.
        /// </summary>
        /// <param name="seat">The seat it is running in.</param>
        /// <param name="drawn">A matrix already open, or null to arrive at the start screen.</param>
        /// <param name="view">Which of the three the grid starts on.</param>
        /// <param name="counted">
        /// Tests as a read of the document hands them back, or null.
        ///
        /// Supplied so the render harness can capture the results view, which otherwise has no
        /// document to count. The tests are counted through <see cref="MatrixTally"/> exactly as
        /// on a seat.
        /// </param>
        public static UIElement Surface(ISeat seat, MatrixBoard? drawn, Showing view,
                                        IReadOnlyList<ClashTestRead>? counted)
        {
            if (drawn != null) _board = drawn;

            return new Editor(seat, view, counted).View;
        }

        /// <summary>
        /// Which of the three values a cell shows.
        ///
        /// A pair of disciplines carries three independent facts: whether it is checked and how,
        /// its priority, and its open clash count. Combining them into one cell would need a
        /// legend entry per combination and would not be legible at cell size.
        ///
        /// Rows, columns, folders and ordering do not change between views; only the cell
        /// contents do. The strip and the chooser both report which view is active.
        /// </summary>
        public enum Showing
        {
            /// <summary>What is checked and how - the matrix that gets built.</summary>
            Symbols,

            /// <summary>How urgent each pair is, in the host's own four ranks.</summary>
            Priority,

            /// <summary>What came back: open clashes per pair.</summary>
            Results,
        }

        /// <summary>Which slot of the project file the matrix is kept in.</summary>
        private const string Slot = "clashmatrix";

        /// <summary>One editing session over the matrix.</summary>
        private sealed class Editor
        {
            private readonly ISeat _session;

            /// <summary>
            /// How this matrix is drawn: which half is worked in, what things are coloured, and
            /// which of a line's fields appear beside its name.
            ///
            /// Saved with the grid rather than with the seat, because a practice draws its
            /// matrices one way and that travels with the matrix it sends out.
            /// </summary>
            private MatrixStyle _style = new MatrixStyle();

            /// <summary>
            /// Where a matrix sits on a worksheet, as it was last answered.
            ///
            /// Held rather than made fresh per open, because a practice lays its matrices out one
            /// way: the second workbook of the afternoon is the same shape as the first, and the
            /// ten coordinates that were right for that one open ready for this one.
            /// </summary>
            private readonly SheetMapping _shape = new SheetMapping();

            /// <summary>
            /// The symbol on the brush, or null while there is none.
            ///
            /// Filling cells one at a time through a menu is prohibitive: twenty disciplines is
            /// 210 cells. Armed from the legend, a symbol is applied by clicking a cell, dragging
            /// across a run, or clicking a row number or column letter to fill the line.
            ///
            /// The menu remains on the right button, and on the left button whenever nothing is
            /// armed.
            /// </summary>
            private MatrixSymbol? _brush;

            /// <summary>Which symbol the cursor is currently carrying, so it is drawn once.</summary>
            private string _carrying = "\u0000";

            /// <summary>
            /// The band over the grid: current state on the left, controls against the right edge.
            ///
            /// Two panels rather than one, so that a change in the state text does not move the
            /// controls sideways under the pointer.
            ///
            /// One line, one font size, one baseline. Mixed type sizes centred in a row put their
            /// baselines in different places. The height is fixed and the content clips, ordered
            /// so the most important state survives; a wrapping strip would gain a line and push
            /// the grid down.
            /// </summary>
            private const double StripSize = 11;

            private const double StripHigh = 30;

            private const double StripGap = 16;

            private readonly StackPanel _strip = new StackPanel { Orientation = Orientation.Horizontal };

            private readonly StackPanel _verbs = new StackPanel { Orientation = Orientation.Horizontal };

            private readonly Border _grid = new Border();

            /// <summary>
            /// The build button, which reports what stands between the current matrix and a build.
            ///
            /// Undecided pairs and rows pointing at nothing do not prevent a build, but they
            /// change what gets built, so they are stated on the button.
            /// </summary>
            private readonly System.Windows.Controls.Button _build;

            /// <summary>
            /// The palette rail: the symbols, as keys to paint with.
            ///
            /// Previously a legend docked under the grid, which a matrix of any size pushed off
            /// screen. As a column beside the grid it stays in view, shows the keystroke for each
            /// symbol, and leaves editing to the inspector.
            /// </summary>
            private readonly StackPanel _palette = Ui.Stack();

            /// <summary>
            /// The palette's heading, which changes with the active view.
            ///
            /// The panel must arm whatever the grid is drawing. In the priority view it holds the
            /// ranks, not the symbol legend.
            /// </summary>
            private readonly TextBlock _paletteTitle = Ui.Eyebrow("Palette");

            /// <summary>Add, change and cover a symbol - which mean nothing but on the symbols.</summary>
            private UIElement? _legendActs;

            /// <summary>What the last thing pressed has to say, down the right.</summary>
            private readonly StackPanel _inspector = Ui.Stack();

            /// <summary>
            /// The pencil beside the plus, kept so it can go grey when there is nothing to change.
            ///
            /// It acts on the symbol that is armed or shown, which is what "the one you mean"
            /// means on this screen - so with nothing armed it is a button with no subject, and
            /// says so by being off rather than by opening a window that explains itself.
            /// </summary>
            private System.Windows.Controls.Button? _editing;

            /// <summary>
            /// Row and column actions, pinned above the palette at the foot of the panel.
            ///
            /// Pinned rather than following the end of the panel content, so their position does
            /// not vary with the length of what is above them.
            /// </summary>
            private readonly StackPanel _acts = Ui.Stack();

            private readonly Border _actsPane = new Border();

            /// <summary>How much of the matrix is decided, at the foot of the palette.</summary>
            private readonly StackPanel _progress = Ui.Stack();

            /// <summary>
            /// The frozen header bands and the cells that scroll under them.
            ///
            /// A single scrolling panel takes the row and column headers out of view as soon as
            /// the grid is scrolled, leaving cells unidentifiable. The headers are separate bands,
            /// clipped and translated by the cells' own scroll offset.
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

            /// <summary>
            /// The headers indexed by line, so a hovered cell can highlight both of its own.
            ///
            /// Identifies the pair under the pointer without requiring both long names to be read
            /// off opposite edges of the grid.
            /// </summary>
            private readonly Dictionary<MatrixLine, Border> _rowHeads =
                new Dictionary<MatrixLine, Border>();

            private readonly Dictionary<MatrixLine, Border> _columnHeads =
                new Dictionary<MatrixLine, Border>();

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
            /// <summary>The inspector's name box for whatever is picked, so the caret can be sent to it.</summary>
            private TextBox? _rename;

            /// <summary>The three-part frame, built once and kept across redraws.</summary>
            private UIElement? _frame;

            /// <summary>
            /// The options panel, which collapses on request.
            ///
            /// One panel rather than two, since the grid is the subject of the screen and needs
            /// the width. It does not collapse automatically on a narrow window: it holds the
            /// palette, and hiding it without being asked would remove the tools mid-task.
            /// </summary>
            private readonly Border _sidePane = new Border();

            /// <summary>Whether it is open.</summary>
            private bool _sideOpen = true;

            /// <summary>Where the keyboard is, or -1 for nowhere.</summary>
            private int _atRow = -1;
            private int _atColumn = -1;

            /// <summary>
            /// The other corner of the marked block, or -1 when the cursor is on its own.
            ///
            /// Decisions are usually made per block ("every MEP row against every structural
            /// column is a clearance"), and the only available scopes were one cell, one line and
            /// the whole grid. Shift with an arrow, or a shift-click, extends the block from the
            /// cursor to here; a symbol then fills all of it.
            /// </summary>
            private int _markRow = -1;
            private int _markColumn = -1;

            /// <summary>The wash over the block, drawn over the cells like the cursor's ring.</summary>
            private readonly Border _range = new Border();

            /// <summary>What the inspector is showing: a line, a symbol, or the cell.</summary>
            private MatrixLine? _picked;
            private bool _pickedRows;

            /// <summary>
            /// Every line selected, on one axis, with <see cref="_picked"/> as the anchor.
            ///
            /// Filing or removing several disciplines one at a time is one dialogue per line.
            /// The selection supports the acts that mean the same thing applied to each.
            ///
            /// The anchor remains the subject of the inspector panel, which is about one line.
            /// </summary>
            private readonly List<MatrixLine> _chosen = new List<MatrixLine>();

            /// <summary>Which axis the picked lines are on. The two are never mixed.</summary>
            private bool _chosenRows;

            /// <summary>Which of the three the grid is showing. The symbols, until asked.</summary>
            private Showing _shown = Showing.Symbols;

            /// <summary>
            /// The rank a press lays down while the priority view is on.
            ///
            /// The same shape as the symbol brush, and for the same reason: ranking a
            /// twenty-by-twenty matrix a cell at a time through a menu is four hundred menus,
            /// and the drag that fills a block of symbols is the drag that should fill a block
            /// of ranks.
            /// </summary>
            private int _rank = MatrixBoard.High;

            /// <summary>The slot the view chooser sits in, so its label can follow the view.</summary>
            private readonly ContentControl _viewPick = new ContentControl
            {
                VerticalAlignment = VerticalAlignment.Center,
            };

            /// <summary>What came back, when it has been read. Empty until somebody asks.</summary>
            private MatrixCounts _counts = MatrixCounts.None;
            private MatrixSymbol? _pickedSymbol;

            /// <summary>
            /// What the matrix was before each act that changed it, and what was undone.
            ///
            /// Fill the grid, sweep a row, drag across twenty cells, start again: every one of
            /// them is destructive and none of them could be taken back. Both stacks hold the
            /// board and the legend together, because an edit to a symbol changes what every
            /// cell carrying it means.
            /// </summary>
            private readonly Stack<Doing> _past = new Stack<Doing>();

            private readonly Stack<Doing> _future = new Stack<Doing>();

            /// <summary>
            /// One edit, and the matrix as it was before it.
            ///
            /// The description is recorded at the time of the edit, so undo can name what it
            /// reversed rather than leaving the user to find the changed cell.
            /// </summary>
            private sealed class Doing
            {
                internal Doing(JsonValue held, string what) { Held = held; What = what; }

                internal JsonValue Held { get; }

                internal string What { get; }
            }

            /// <summary>The run of edits the last remembering was for, or empty.</summary>
            private string _remembered = string.Empty;

            /// <summary>
            /// Autosave, a few seconds after the last edit.
            ///
            /// The only save was a menu item, with no indication of whether it had been used and
            /// no prompt on close, so unsaved work could be lost by closing the window.
            ///
            /// Silent in normal operation: the strip reports when the last save happened, and
            /// only a failure interrupts.
            /// </summary>
            private readonly System.Windows.Threading.DispatcherTimer _saver =
                new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3),
                };

            /// <summary>When the matrix was last written to the project file.</summary>
            private DateTime? _saved;

            /// <summary>Why the last save did not happen, or null.</summary>
            private string? _unsaved;
            /// <summary>
            /// Messages from the last action, in a strip that can be dismissed.
            ///
            /// A sanity check can produce twenty messages, a spreadsheet read five, a build one
            /// per test it could not write. As an open-ended stack these pushed the grid down
            /// with no way to close them. The strip has a fixed height, scrolls internally, and
            /// the close button dismisses it.
            /// </summary>
            private readonly StackPanel _notes = Ui.Stack();

            private readonly Border _said = new Border();
            private readonly TextBlock _count = new TextBlock();

            // Every painted cell, so setting a symbol repaints one border rather than the grid.
            // A matrix of twenty by twenty is four hundred cells, and rebuilding all of them to
            // change one letter is exactly the fault this product has been reported for.
            private readonly Dictionary<(MatrixLine Row, MatrixLine Column), Border> _cells =
                new Dictionary<(MatrixLine, MatrixLine), Border>();

            internal Editor(ISeat session, Showing view, IReadOnlyList<ClashTestRead>? counted)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));

                // Set before anything is drawn, because the chooser's label and every cell in the
                // grid are built from it. The results view needs counts before it can draw
                // anything, and a constructor is no place to go and read a document - so it opens
                // on results only when the tests have been handed in already, and counts them the
                // same way the button does.
                if (view == Showing.Results)
                {
                    _counts = MatrixTally.Of(counted);
                    _shown = _counts.IsEmpty ? Showing.Symbols : Showing.Results;
                }
                else _shown = view;

                _build = Ui.Primary("Build the tests", Build);

                Load();

                _saver.Tick += (s, e) => Ui.Safely(() =>
                {
                    _saver.Stop();
                    Kept(quiet: true);
                });

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

                var close = Ui.Icon(Glyphs.Close, "Dismiss", Quiet);

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
                _said.BorderThickness = new Thickness(1);
                _said.CornerRadius = new CornerRadius(8);

                // Floating over the grid rather than above it, so it carries its own edge.
                _said.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 2,
                    Opacity = 0.18,
                    Direction = 270,
                };
                _said.Child = inside;
                _said.Visibility = Visibility.Collapsed;

                _said.Tint(Border.BackgroundProperty, "Card");
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

                // A thin rule between the groups, so the grouping is on the screen rather than
                // only in this comment.
                void Apart()
                {
                    var line = new Border
                    {
                        Width = 1,
                        Margin = new Thickness(4, 5, 10, 5),
                        VerticalAlignment = VerticalAlignment.Stretch,
                    };

                    line.Tint(Border.BackgroundProperty, "Bd");

                    row.Children.Add(line);
                }

                // The strip reads in the order the work happens, in four groups: build the
                // matrix, view it, save and send it, then check and build the tests.
                //
                // It had grown to nine controls in an undifferentiated row. Earlier problems
                // fixed at the same time: "Start from" held both ways to create a matrix and ways
                // to rearrange one; "Save it in this project" was filed under "Out"; and the
                // settings were split across three menus, none named settings.
                //
                // First group: what the matrix is. Where it came from, and its order and folders.
                Add(Parts.MenuButton("Start from", Glyphs.Read, Starting()));
                Add(Parts.MenuButton("Arrange", Glyphs.Sort, Arranging()));

                Apart();

                // Second group: how it is being viewed. Which of the three matrices is in the
                // cells, and how the grid is drawn. Neither changes the board's contents.
                //
                // The view chooser was drawn as a menu and read as one, though it holds the
                // screen's current state. It is drawn as a mode indicator instead: prefixed, and
                // lit while the board is turned over. Its label is rebuilt on each change, having
                // previously been built once at start-up.
                RedrawView();

                Add(_viewPick);
                Add(Parts.MenuButton("Settings", Glyphs.Tune, Settings()));

                Apart();

                // Third group: output. Into the project, and out as a file.
                Add(Ui.Verb(Glyphs.Save, "Save",
                            "Keep it in this project, so it is here next time this model is "
                            + "opened. It also saves itself as you work.", Keep));

                // A menu because two things are exported: the spreadsheet, for review, and the
                // test file, for the seat that will run the tests. The latter lets a matrix drawn
                // on one machine be built on another.
                Add(Parts.MenuButton("Export", Glyphs.Export, Exports()));

                Apart();

                // Fourth group: the build and its two pre-flight checks. These were previously
                // the top two items of the export menu, which reads as output rather than
                // preparation.
                Add(Ui.Verb(Glyphs.Verify, "Check",
                            "Check it against the model — every row and column, against what "
                            + "this model actually has", Sanity));

                Add(Ui.Verb(Glyphs.Changes, "Changes",
                            "See what would change — this matrix against the tests already in "
                            + "the document", Difference));

                _build.Margin = new Thickness(2, 0, 0, 0);

                row.Children.Add(Ui.Loose(_build));

                return row;
            }

            /// <summary>
            /// The screen: the notices, then the three parts of the matrix.
            ///
            /// Palette on the left, grid in the middle, inspector on the right. All three are
            /// always present and do not move, so choosing a symbol and placing it are adjacent,
            /// and naming a row or a symbol happens in place rather than in a dialogue.
            /// </summary>
            private UIElement Body()
            {
                _grid.Padding = new Thickness(0);

                var stack = new DockPanel { LastChildFill = true };

                // Notices take a row above the grid rather than floating over it or being docked
                // into the grid's space.
                //
                // Docked into the grid, a notice took height from the grid and returned it on
                // dismissal, moving the target cell under the pointer. Floated over the top-right
                // it covered the text beneath. A notice here is a standing fact about the seat
                // (no clash engine, no document open) and appears on open rather than mid-edit,
                // so a row of its own is appropriate. The transient per-action confirmations that
                // made a docked notice unworkable have been removed.
                //
                // Dock.Top is set explicitly. DockPanel defaults to Dock.Left, which gave the
                // notice the full height down the left side and left the grid about 70px of an
                // 1100px window. This compiled, threw nothing and passed every test; only a
                // render showed it.
                stack.Children.Add(Ui.Loose(_grid));

                // Undo is bound at the screen level rather than to the cells' scroller, where it
                // did nothing while focus was in the inspector, the palette or the row-name box.
                // On the bubbling event rather than the preview one, so a focused text box keeps
                // its own undo.
                stack.KeyDown += (s, e) => Ui.Safely(() =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

                    if (e.Key == Key.Z) { Undo(); e.Handled = true; }
                    else if (e.Key == Key.Y) { Redo(); e.Handled = true; }
                });

                return stack;
            }

            /// <summary>The three parts, built once: the rail, the sheet, the inspector.</summary>
            private UIElement Frame()
            {
                if (_frame != null) return _frame;

                var both = new Grid();

                both.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                both.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var middle = Middle();

                Grid.SetColumn(middle, 0);
                both.Children.Add(middle);

                _sidePane.Width = 264;
                _sidePane.BorderThickness = new Thickness(1, 0, 0, 0);
                _sidePane.Child = Options();

                // The panel's visibility is set explicitly rather than left to the Border's
                // default, so the flag the strip reports always matches what is on screen.
                _sidePane.Visibility = _sideOpen ? Visibility.Visible : Visibility.Collapsed;

                _sidePane.Tint(Border.BackgroundProperty, "Pane");
                _sidePane.Tint(Border.BorderBrushProperty, "Bd");

                Grid.SetColumn(_sidePane, 1);
                both.Children.Add(Ui.Loose(_sidePane));

                _frame = both;

                return both;
            }

            /// <summary>
            /// One panel, down the right, holding everything that is not the grid.
            ///
            /// Previously two panels, one on each side, costing about 450px on a screen whose
            /// subject is a grid that needs the width. Choosing a symbol and describing a row are
            /// related acts, so they share one panel and the grid gets the other side back.
            ///
            /// Ordered: the last thing pressed at the top, where it changes; the palette below
            /// it, in a fixed position; and the remaining-decisions count at the foot.
            /// </summary>
            private UIElement Options()
            {
                var panel = new DockPanel { LastChildFill = true };

                var foot = new Border
                {
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(10, 8, 10, 9),
                    Child = Ui.Loose(_progress),
                };

                foot.Tint(Border.BorderBrushProperty, "Bd");

                DockPanel.SetDock(foot, Dock.Bottom);
                panel.Children.Add(foot);

                var keys = Ui.Stack();

                // No paragraph explaining how to use it. The strip over the grid already says
                // what is on the brush and what to do with it, in the place somebody is looking
                // while they do it - and three lines of the same thing here is three lines the
                // panel takes off whatever the inspector is showing.
                _paletteTitle.Margin = new Thickness(10, 9, 10, 5);
                keys.Children.Add(Ui.Loose(_paletteTitle));

                // Capped, and scrolling inside itself: a job with a dozen symbols must not push
                // whatever the inspector is showing off the top of the panel.
                keys.Children.Add(new ScrollViewer
                {
                    Content = Ui.Loose(_palette),
                    MaxHeight = 240,
                    Padding = new Thickness(6, 0, 6, 4),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });

                // Outside the capped list, not in it. A job with a dozen symbols scrolls the
                // symbols; the two things you do to the legend itself must not scroll away with
                // them, because "add a symbol" is what somebody reaches for when the one they
                // want is not in the list they are looking at.
                var changing = Doable(Glyphs.Pencil,
                    "Change the symbol on the brush — what it is called, its colour, its kind "
                    + "and its tolerance", EditSymbol);

                _editing = changing;

                // Nothing is armed the moment the panel is built, and the pencil has no subject
                // until something is.
                changing.IsEnabled = false;

                var legend = Ui.Across(
                    Doable(Glyphs.Add, "Add a symbol to the legend", AddSymbol),
                    changing,
                    Doable(Glyphs.Checklist,
                           "Give every symbol in the grid a meaning, if any of them has none",
                           CoverSymbols));

                legend.Margin = new Thickness(7, 0, 7, 4);

                _legendActs = legend;

                keys.Children.Add(legend);

                var band = new Border
                {
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Child = keys,
                };

                band.Tint(Border.BorderBrushProperty, "Bd");

                DockPanel.SetDock(band, Dock.Bottom);
                panel.Children.Add(band);

                _actsPane.BorderThickness = new Thickness(0, 1, 0, 0);
                _actsPane.Padding = new Thickness(10, 4, 10, 4);
                _actsPane.Child = Ui.Loose(_acts);

                _actsPane.Tint(Border.BorderBrushProperty, "Bd");

                DockPanel.SetDock(_actsPane, Dock.Bottom);
                panel.Children.Add(Ui.Loose(_actsPane));

                // Notices go in the right-hand panel, which is open throughout a session and is
                // not where the editing happens. Above the grid they took height from the matrix
                // and returned it on dismissal, moving the target cell; over the grid they
                // covered the content beneath. Here the grid does not move.
                var told = Head();

                DockPanel.SetDock(told, Dock.Top);
                panel.Children.Add(told);

                panel.Children.Add(new ScrollViewer
                {
                    Content = Ui.Loose(_inspector),
                    Padding = new Thickness(10, 8, 10, 10),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });

                return panel;
            }

            /// <summary>The strip, and the sheet under it.</summary>
            private UIElement Middle()
            {
                var middle = new DockPanel { LastChildFill = true };

                var bar = new Border
                {
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10, 5, 8, 5),
                    Child = Bar(),
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
            private UIElement Bar()
            {
                var bar = new DockPanel
                {
                    LastChildFill = true,

                    // One line, always. Anything that does not fit is cut rather than wrapped,
                    // because a wrapped strip is a taller strip and a taller strip has pushed
                    // the matrix down - which is the thing a notice in here must never do.
                    Height = StripHigh,
                    ClipToBounds = true,
                };

                _verbs.HorizontalAlignment = HorizontalAlignment.Right;
                _verbs.VerticalAlignment = VerticalAlignment.Center;
                _verbs.Margin = new Thickness(StripGap, 0, 0, 0);

                _strip.VerticalAlignment = VerticalAlignment.Center;

                DockPanel.SetDock(_verbs, Dock.Right);
                bar.Children.Add(Ui.Loose(_verbs));

                bar.Children.Add(Ui.Loose(_strip));

                return bar;
            }

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
                _side.VerticalAlignment = VerticalAlignment.Stretch;

                Grid.SetRow(_side, 1);
                Grid.SetColumn(_side, 0);
                frame.Children.Add(Ui.Loose(_side));

                _scroll.Content = Ui.Loose(_sheet);
                _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                _scroll.Focusable = true;
                _scroll.FocusVisualStyle = null;

                // The area below the last row is the pane background, not the sheet. Previously
                // it was the same colour as the cells with no edge, so a small matrix looked like
                // a table that had failed to finish drawing. The sheet now has an edge and ends
                // where it ends.
                _scroll.Tint(Control.BackgroundProperty, "Bg");

                // The header bands have no scrollbars and the cell area does. A vertical bar
                // takes 17px from the cells' viewport and nothing from the band's, so the band
                // showed a column whose cells were already clipped. The horizontal bar did the
                // same to the row names. Each band is inset by whatever the cells lose to a bar.
                _scroll.ScrollChanged += (s, e) =>
                {
                    _slidX.X = -_scroll.HorizontalOffset;
                    _slidY.Y = -_scroll.VerticalOffset;

                    _band.Margin = new Thickness(0, 0,
                        _scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
                            ? SystemParameters.VerticalScrollBarWidth : 0, 0);

                    _side.Margin = new Thickness(0, 0, 0,
                        _scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                            ? SystemParameters.HorizontalScrollBarHeight : 0);
                };

                _scroll.PreviewKeyDown += (s, e) => Ui.Safely(() => Typed(e));

                // The room changes when the window does, when the pane does, and when a tab is
                // switched back to. The cells follow it.
                _scroll.SizeChanged += (s, e) => Ui.Safely(() =>
                {
                    if (_board.Rows.Count == 0 || _board.Columns.Count == 0) return;

                    // A width change that does not move the cell size still moves the slack, and
                    // the strip past the plus is what would otherwise grow with the window.
                    if (Fits()) Relaid();
                    else Filling();
                });

                Grid.SetRow(_scroll, 1);
                Grid.SetColumn(_scroll, 1);
                frame.Children.Add(Ui.Loose(_scroll));

                _ring.BorderThickness = new Thickness(2);
                _ring.IsHitTestVisible = false;
                _ring.Visibility = Visibility.Collapsed;
                _ring.Tint(Border.BorderBrushProperty, "Accent");

                // The block highlight is drawn over the cells rather than by tinting each cell:
                // one translucent pane spanning the range is a single element, and the cells keep
                // their own fills.
                //
                // The block and the cursor are drawn differently. As two 2px accent rectangles
                // sharing an edge they read as one mark, so the end that shift and the arrows
                // move was not identifiable. The block is a dashed edge and a wash; the cursor
                // remains a solid ring and reads on top of it.
                _range.BorderThickness = new Thickness(1.5);
                _range.IsHitTestVisible = false;
                _range.Visibility = Visibility.Collapsed;
                _range.Tint(Border.BorderBrushProperty, "AccentPress");
                _range.Tint(Border.BackgroundProperty, "AccentWash");
                _range.Opacity = 0.45;

                return frame;
            }

            // ---------------------------------------------------------------------------
            // The grid
            // ---------------------------------------------------------------------------

            /// <summary>
            /// Fixed cell sizes.
            ///
            /// A column is sized for a symbol, not a name. Horizontal names were truncated
            /// anyway while costing about 100px per column. Rotated, they cost height once, and a
            /// 22-discipline matrix fits on one screen.
            /// </summary>
            private const double LeastWide = 38;

            /// <summary>
            /// The drawn cell size: the smallest legible size, grown to fill the available space.
            ///
            /// A grid that fits with room to spare is drawn larger: bigger click targets and more
            /// of each name visible. Once it no longer fits, the cells return to the smallest
            /// legible size and the sheet scrolls.
            /// </summary>
            private double CellWide => _wide;

            /// <summary>
            /// One line, not two. The source moved to the inspector, which has room for it.
            /// </summary>
            private const double LeastHigh = 30;

            /// <summary>
            /// The largest a cell is allowed to grow to.
            ///
            /// Generous, because the point is to leave no dead half-window under a small matrix,
            /// and a cell holding one letter reads at any size. A cell is never taller than it
            /// is wide, so six rows against twenty columns come out as a band of squares rather
            /// than a row of tall thin slots.
            /// </summary>
            private const double MostWide = 110;

            private const double MostHigh = 88;

            private double CellHigh => _high;

            /// <summary>
            /// The cell font size, which follows the cell size.
            ///
            /// Between the smallest and largest cell the letter scales from 13 to 20, so a grown
            /// grid is not a small grid stretched.
            /// </summary>
            private double Lettering
            {
                get
                {
                    var room = Math.Min(CellWide, CellHigh);

                    if (room <= LeastHigh) return 13;

                    var grown = 13 + ((room - LeastHigh) / (MostHigh - LeastHigh) * 7);

                    return Math.Round(Math.Max(13, Math.Min(20, grown)));
                }
            }

            private double _wide = LeastWide;
            private double _high = LeastHigh;

            /// <summary>
            /// Computes the cell size for the available space and reports whether it changed.
            ///
            /// Growth only happens when the smallest size already fits. Filling the viewport
            /// exactly would summon a scrollbar, which removes space, which shrinks the cells,
            /// which removes the scrollbar. Grown sizes are computed from a viewport already known
            /// to be large enough and rounded down, so the total always fits.
            /// </summary>
            private bool Fits()
            {
                var wide = LeastWide;
                var high = LeastHigh;

                var across = _board.Columns.Count + 1;
                var down = _board.Rows.Count + 1;

                var room = _scroll.ViewportWidth - 2;
                var deep = _scroll.ViewportHeight - 2;

                if (across > 0 && room > across * LeastWide)
                    wide = Math.Floor(Math.Min(MostWide, room / across));

                if (down > 0 && deep > down * LeastHigh)
                    high = Math.Floor(Math.Min(MostHigh, deep / down));

                high = Math.Max(LeastHigh, Math.Min(high, wide));

                if (Math.Abs(wide - _wide) < 0.5 && Math.Abs(high - _high) < 0.5) return false;

                _wide = wide;
                _high = high;

                return true;
            }

            /// <summary>Draw the three grids again at whatever size the cells are now.</summary>
            private void Relaid()
            {
                _cells.Clear();

                RedrawAcross();
                RedrawDown();
                RedrawCells();

                Filling();
            }

            /// <summary>
            /// Let the add-a-discipline slot take up whatever width is left over.
            ///
            /// Cells are a whole number of pixels wide and never wider than
            /// <see cref="MostWide"/>, so the columns rarely land exactly on the edge of the pane.
            /// What was left over was drawn as nothing: a dead strip between the plus and the
            /// inspector, which reads as the grid having failed to finish rather than as a grid
            /// that ends where it ends.
            ///
            /// The slack goes to the slot the plus already sits in, so the row of columns runs out
            /// to the edge and the plus stays where it was. A grid wider than the pane has no
            /// slack and is left alone.
            /// </summary>
            private void Filling()
            {
                var room = _scroll.ViewportWidth;
                var last = _board.Columns.Count;

                if (room <= 0 || last == 0) return;

                var used = 0.0;

                for (var c = 0; c < last; c++)
                    if (!Hidden(_board.Columns[c]))
                        used += CellWide;

                var slack = new GridLength(Math.Max(CellWide, room - used));

                // Both grids, and by the same number, because the band across the top is what
                // makes the columns readable and it has to end where they do.
                if (_sheet.ColumnDefinitions.Count > last)
                    _sheet.ColumnDefinitions[last].Width = slack;

                if (_across.ColumnDefinitions.Count > last)
                    _across.ColumnDefinitions[last].Width = slack;
            }

            /// <summary>
            /// The measured width of the row names, excluding the gutters beside them.
            ///
            /// Measured rather than fixed: a fixed 150px truncated "ARC_Ceilings and Soffits",
            /// and an unreadable discipline name makes the row unusable. Floored so short names
            /// still give a band worth pointing at, and capped so one long name cannot take a
            /// third of the window. The leading mark is included, since it is reserved for the
            /// whole axis whenever any row needs it.
            /// </summary>
            private double NameWide
            {
                get
                {
                    var widest = 0.0;

                    foreach (var line in _board.Rows)
                        widest = Math.Max(widest, Measured(line.Name, 12));

                    // The mark and the gap after it, and the room the name is inset by.
                    var mark = Unbacked(_board.Rows) ? 24.0 : 0;

                    return Math.Max(150, Math.Min(300, widest + mark + 14));
                }
            }

            /// <summary>
            /// The number and code gutters, each measured to its longest entry.
            ///
            /// A fixed 36px truncated "ARC". These are short strings of known length, so they are
            /// measured rather than estimated.
            /// </summary>
            private double NumberWide => Widest(_board.Rows, 11, false) + 12;

            private double CodeWide => Widest(_board.Rows, 10, true, code: true) + 18;

            /// <summary>
            /// The container a row header is built in: its edge, and the gutters it lays out.
            ///
            /// This is the only description of a row header's composition. <see cref="HeaderWide"/>
            /// measures one to size the band. Previously the composition was described here and
            /// again in a width formula, which is two descriptions of one box that a compiler
            /// cannot check against each other.
            ///
            /// The number and code gutters are each as wide as their longest entry, and an unused
            /// gutter is not drawn, so a matrix without numbers or codes is no wider than before.
            ///
            /// The number has its own column rather than being prefixed to the name. Numbers of
            /// differing width made the names start at different positions, and the code, which
            /// test names are built from, was not shown at all.
            /// </summary>
            private Border RowShell()
            {
                var words = new Grid();

                if (Numbered(_board.Rows))
                    words.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(NumberWide),
                    });

                if (Coded(_board.Rows))
                    words.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(CodeWide),
                    });

                words.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(NameWide),
                });

                return new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(0),
                    Child = words,
                };
            }

            /// <summary>
            /// How wide the row headers turned out to be, asked of one rather than worked out.
            ///
            /// Built empty and measured: no name is put in it, so nothing here depends on a font
            /// resolving or on which row happens to be first - only on the shell's own edge,
            /// padding and gutters, which is exactly what the band has to be wide enough to hold.
            /// </summary>
            private double HeaderWide => Asked(RowShell()).Width;

            /// <summary>
            /// Whether the numbers carry information beyond the index gutter beside them.
            ///
            /// Rows made from saved sets are numbered 01, 02, 03 in order, duplicating the index.
            /// The column is drawn as soon as one number differs from its ordinal, which happens
            /// when a row is removed or a package is renumbered.
            /// </summary>
            /// <param name="lines">The rows or the columns.</param>
            /// <summary>Whether the number gutter is drawn, which the style can override.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private bool Numbered(IReadOnlyList<MatrixLine> lines) =>
                MatrixStyle.Drawn(_style.Numbers, Numbering(lines));

            /// <summary>Whether the code gutter is drawn.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private bool Coded(IReadOnlyList<MatrixLine> lines) =>
                MatrixStyle.Drawn(_style.Codes, Coding(lines));

            /// <summary>Whether room is kept for the mark on a line that points at nothing.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private bool Unbacked(IReadOnlyList<MatrixLine> lines) =>
                MatrixStyle.Drawn(_style.Warnings, Unbacking(lines));

            /// <summary>Whether the numbers say anything the index gutter does not.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private static bool Numbering(IReadOnlyList<MatrixLine> lines)
            {
                for (var at = 0; at < lines.Count; at++)
                {
                    var said = lines[at].Number;

                    if (said.Length == 0) continue;

                    if (!int.TryParse(said, NumberStyles.None, CultureInfo.InvariantCulture,
                                      out var held) || held != at + 1)
                        return true;
                }

                // Every number there is agrees with the position it sits at, so the index gutter
                // beside it is already saying it.
                return false;
            }

            /// <summary>
            /// Whether the codes are worth a column of their own.
            ///
            /// A code is a discipline's short form and a set is nearly always named after it:
            /// "WSC" in front of "WSC_Pipes" is the same three letters twice on a row that has
            /// no width to spare. A code that is not already the front of its name is telling
            /// you something the name does not, and keeps its column.
            /// </summary>
            /// <param name="lines">The rows or the columns.</param>
            private static bool Coding(IReadOnlyList<MatrixLine> lines)
            {
                foreach (var line in lines)
                    if (line.Code.Length > 0
                        && !line.Name.StartsWith(line.Code, StringComparison.OrdinalIgnoreCase))
                        return true;

                return false;
            }
            /// <summary>
            /// The column band's height: the longest name in it, plus what sits under the names.
            ///
            /// Measured rather than fixed. The names are rotated and end at the grid, so a band
            /// taller than the longest name is empty across the full width of the screen. Floored
            /// so a short-named matrix still reads as a header, and capped so one long name cannot
            /// take a third of the window.
            /// </summary>
            private double NameRun
            {
                get
                {
                    // Upright, a name costs one line however long it is.
                    if (Upright) return NameTall;

                    var longest = 0.0;

                    foreach (var line in _board.Columns)
                        longest = Math.Max(longest, Measured(line.Name));

                    return Math.Max(64, Math.Min(200, longest + 14));
                }
            }

            /// <summary>
            /// The container a column header is built in: its padding, and the rows it stacks.
            ///
            /// This is the only description of a column header's composition; the band asks
            /// <see cref="HeaderHigh"/>, which measures the same container.
            ///
            /// Previously the composition was described here and again in a height formula that
            /// did not account for the 6px/4px padding, so the band was 10px shorter than its
            /// content and every column name was clipped by that amount. Two descriptions of one
            /// box are both valid arithmetic; only a render shows which is right.
            /// </summary>
            private Border ColumnShell()
            {
                var stacked = new Grid();

                if (Numbered(_board.Columns))
                    stacked.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(NumberTall),
                    });

                if (Coded(_board.Columns))
                    stacked.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(CodeTall),
                    });

                if (Unbacked(_board.Columns))
                    stacked.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(WarnTall),
                    });

                // The name's row is sized explicitly rather than with Star. A rotated name is a
                // TextBlock rotated for drawing only, so it reports one line of height and the
                // width of the text; its occupied height on screen is known only here. Stated
                // once and measured everywhere else.
                stacked.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(NameRun),
                });

                // The header's own padding, over the words and under them. It lives here and
                // nowhere else: it used to be a pair of constants the whole class could read,
                // and the formula that worked out how tall the band should be simply did not
                // read them - so the band was ten pixels shorter than the header standing in it
                // and every name in every column was cut off by exactly that. Whatever is put
                // here is measured, because the only thing that reports the height is the thing
                // that draws it.
                const double top = 6;
                const double foot = 4;

                return new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(2, top, 2, foot),
                    Child = stacked,
                };
            }

            /// <summary>
            /// The column band's height, measured from a header rather than fixed.
            ///
            /// A fixed height leaves empty space across the full width of the screen when the
            /// names are short. The floor and cap are in <see cref="NameRun"/>.
            /// </summary>
            private double HeaderHigh => Asked(ColumnShell()).Height;

            /// <summary>
            /// What a built part asks the layout for, measured against all the room in the world.
            ///
            /// The whole point of building the thing and asking it. A size worked out a second
            /// time, in a formula beside the one that builds it, is a size that can disagree with
            /// what is on the screen - and every way that has gone wrong here has gone wrong
            /// silently, because two plausible sums compile exactly as well as one.
            /// </summary>
            /// <param name="part">The part, which need not be in a window.</param>
            private static Size Asked(UIElement part)
            {
                part.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                return part.DesiredSize;
            }

            /// <summary>Whether every column name fits across its column without being turned.</summary>
            private bool Upright
            {
                get
                {
                    if (_board.Columns.Count == 0) return false;

                    // Compared with room to spare rather than to the pixel. A name that exactly
                    // fits is drawn hard against both cell edges, and a measurement a shade short
                    // of the drawn width clips the tail. Below this width names are rotated.
                    if (CellWide < 64) return false;

                    foreach (var line in _board.Columns)
                        if (Measured(line.Name) + 18 > CellWide) return false;

                    return true;
                }
            }

            // The rows above a column's name are reserved for the whole axis or for none of it.
            // Sized per line, a column without a code reclaimed its 15px and started its name
            // higher than its neighbours', producing the ragged edge the rotation was meant to
            // remove. ColumnShell therefore adds them for the whole board.
            //
            // A property that summed these three existed only to let a second formula recompute
            // the band height. Both are gone: the container declares the rows and the band
            // measures the container.

            /// <summary>
            /// How far down a column header the number, code and mark reach.
            ///
            /// One line of type each, since they are drawn upright. Measuring them to the longest
            /// number and code applied when they were rotated and their length ran down the
            /// column.
            /// </summary>
            private const double NumberTall = 16;

            private const double CodeTall = 18;

            private const double WarnTall = 16;

            /// <summary>The longest number, or code, in one axis - at the size it is drawn.</summary>
            /// <param name="lines">The rows or the columns.</param>
            /// <param name="size">The type size it is drawn at.</param>
            /// <param name="bold">Whether it is drawn semibold.</param>
            /// <param name="code">True for the codes, false for the numbers.</param>
            private static double Widest(IReadOnlyList<MatrixLine> lines, double size, bool bold,
                                         bool code = false)
            {
                var widest = 0.0;

                foreach (var line in lines)
                    widest = Math.Max(widest, Measured(code ? line.Code : line.Number, size, bold));

                return Math.Max(12, Math.Min(80, widest));
            }

            /// <summary>
            /// A text block kept aside to measure a name with, at the size the headers use.
            ///
            /// Measuring the real one is no good: it is given the box it has to fit and reports
            /// that box back. This one is never drawn and is asked for its natural width.
            /// </summary>
            private static readonly TextBlock Ruler = new TextBlock();

            /// <summary>How wide one piece of text is, laid out in a line at the size it is drawn.</summary>
            /// <param name="text">The text.</param>
            /// <param name="size">The type size.</param>
            /// <param name="bold">Whether it is semibold.</param>
            private static double Measured(string text, double size = 11, bool bold = true)
            {
                Ruler.FontSize = size;
                Ruler.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
                Ruler.Text = text ?? string.Empty;

                // A block that has been measured once reports the same answer to the same
                // constraint however its text changed, so it is asked again from scratch.
                Ruler.InvalidateMeasure();
                Ruler.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                return Ruler.DesiredSize.Width;
            }

            /// <summary>
            /// The height of one line of type at the size a header draws it.
            ///
            /// Measured rather than constant: the previous constant was 4px short, so upright
            /// names were drawn into a space too small and lost their descenders.
            /// </summary>
            /// <param name="size">The type size.</param>
            /// <param name="bold">Whether it is drawn semibold.</param>
            private static double LineHigh(double size = 11, bool bold = true)
            {
                Ruler.FontSize = size;
                Ruler.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;

                // Ascender and descender both, so a band sized off this holds a name with a
                // "g" in it as well as one without.
                Ruler.Text = "Hg";

                Ruler.InvalidateMeasure();
                Ruler.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

                return Math.Ceiling(Ruler.DesiredSize.Height);
            }

            /// <summary>The gap between an upright name and the grid under it.</summary>
            private const double NameGap = 5;

            /// <summary>How much height one upright name needs, gap included.</summary>
            private static double NameTall => LineHigh() + NameGap;


            /// <summary>Whether any of these lines points at nothing.</summary>
            /// <param name="lines">The rows or the columns.</param>
            private static bool Unbacking(IReadOnlyList<MatrixLine> lines)
            {
                foreach (var line in lines) if (line.IsUnbacked) return true;

                return false;
            }

            /// <summary>
            /// The width of the index gutter down the left, and the height of the one across.
            ///
            /// A click in the gutter writes a whole line, so it needs a usable target: at 20px it
            /// was the smallest target on the screen with the largest consequence, flush against
            /// the 200px header that only selects. It is 24px and drawn on its own ground so the
            /// boundary between the two bands is visible.
            /// </summary>
            private const double IndexWide = 26;
            private const double IndexHigh = 24;

            /// <summary>
            /// The width or height each band gives to folder labels, when any folder is in use.
            ///
            /// Perpendicular to the names it groups: a row's name reads across, so its folder
            /// label reads down the band beside it, spanning exactly the rows it holds. A column's
            /// folder label reads across the top of its columns.
            ///
            /// Zero when nothing is filed, so an unfoldered matrix costs no width.
            /// </summary>
            private double FolderWide =>
                _board.Folders(rows: true).Count == 0 ? 0 : LineHigh(10, bold: true) + 10;

            private double FolderHigh =>
                _board.Folders(rows: false).Count == 0 ? 0 : LineHigh(10, bold: true) + 8;

            // The frozen frame's size is taken from layout rather than computed. The corner, the
            // top band and the side band must agree to the pixel, or the headers stop sitting over
            // the cells they name. Four places used to compute that sum; adding the folder strip
            // updated two of them and left the two built once holding the old value, so the frame
            // disagreed with itself by exactly the band width.
            //
            // The frame is now a Grid whose first row and column are Auto, so it takes its size
            // from what the bands actually contain.

            /// <summary>The folders shut, by name. Both axes at once, because a folder is one thing.</summary>
            private readonly HashSet<string> _shut =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Whether a line is inside a folder somebody has shut.</summary>
            /// <param name="line">The row or column.</param>
            private bool Hidden(MatrixLine line) =>
                line.Folder.Length > 0 && _shut.Contains(line.Folder) && !Folded(line);

            /// <summary>
            /// The single line a collapsed folder occupies, and the line that represents it.
            ///
            /// Giving every member zero height leaves the folder with no height and therefore no
            /// target to reopen it. The first member keeps its place and carries the folder label
            /// instead.
            ///
            /// Down the side the label reads across the row at row-header width; a rotated label
            /// in a 24 x 30px band shows about three characters. Across the top it stays rotated,
            /// where a column header has the height for it.
            /// </summary>
            /// <param name="line">The row or column.</param>
            private bool Folded(MatrixLine line)
            {
                if (line.Folder.Length == 0 || !_shut.Contains(line.Folder)) return false;

                var lines = _board.Rows.Contains(line)
                    ? (IReadOnlyList<MatrixLine>)_board.Rows
                    : _board.Columns;

                foreach (var one in lines)
                    if (string.Equals(one.Folder, line.Folder, StringComparison.OrdinalIgnoreCase))
                        return ReferenceEquals(one, line);

                return false;
            }

            /// <summary>Open a folder, or shut it, on both axes at once.</summary>
            /// <param name="folder">The folder.</param>
            private void Shut(string folder)
            {
                if (folder.Length == 0) return;

                if (!_shut.Remove(folder)) _shut.Add(folder);

                // Collapsing is a drawing state, not a change to the matrix. The lines remain in
                // place with their symbols; they are given zero height and width, so indexes,
                // cells and counts are unaffected and nothing reading them needs to know about
                // folders. Only the keyboard needs telling, so the cursor steps over hidden
                // lines.
                Redraw();
            }

            /// <summary>
            /// Build the grid again. Only for a change of shape - a row added, removed or moved.
            ///
            /// Painting a symbol into a cell does not come through here: that is one border,
            /// repainted where it stands.
            /// </summary>
            private void Redraw()
            {
                // Fill rules are re-applied here, once, before drawing. The alternative is to
                // re-apply at each of the ~15 call sites that change the board's shape, where a
                // sixteenth is easily missed and the matrix silently stops matching its rules.
                //
                // Rule-filled cells are derived state. The cost is one pass over the grid, and
                // only when rules exist. Every path in (menu, import, undo) passes through here.
                if (_board.Rules.Count > 0) _board.ApplyRules();

                _cells.Clear();

                var bare = _board.Rows.Count == 0 && _board.Columns.Count == 0;

                // Rows with no columns produce no grid: no cells, no keyboard, no progress. The
                // typing box creates rows only, so a hand-typed matrix arrives in this state. The
                // action that fixes it is offered here, in place of the grid, rather than as the
                // fifth item of the "Start from" menu.
                var half = !bare && _board.Columns.Count == 0;

                // Nothing drawn yet is a screen about starting, and a palette, a strip and an
                // inspector about an empty grid are three pieces of furniture in the way of it.
                _grid.Child = bare ? Empty() : half ? Sideways() : Frame();

                if (!bare && !half)
                {
                    Fits();
                    Relaid();
                }

                RedrawPalette();
                RedrawStrip();
                RedrawInspector();
                Pointing();
                Recount();
            }

            /// <summary>The band across the top: the letters, and the names stood on their end.</summary>
            private void RedrawAcross()
            {
                _columnHeads.Clear();
                _across.Children.Clear();
                _across.ColumnDefinitions.Clear();
                _across.RowDefinitions.Clear();

                var band = FolderHigh;

                if (band > 0)
                    _across.RowDefinitions.Add(new RowDefinition { Height = new GridLength(band) });

                _across.RowDefinitions.Add(new RowDefinition { Height = new GridLength(IndexHigh) });
                _across.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHigh) });

                for (var c = 0; c <= _board.Columns.Count; c++)
                    _across.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        // A column inside a shut folder is given no width. It is still there,
                        // still in order, still holding its symbols - it is simply not drawn.
                        Width = new GridLength(c < _board.Columns.Count
                                               && Hidden(_board.Columns[c]) ? 0 : CellWide),
                    });

                var top = band > 0 ? 1 : 0;

                for (var c = 0; c < _board.Columns.Count; c++)
                {
                    var column = _board.Columns[c];

                    if (Hidden(column)) continue;

                    if (Folded(column))
                    {
                        var drawer = Drawer(column.Folder, rows: false);

                        Grid.SetColumn(drawer, c);
                        Grid.SetRow(drawer, top);
                        Grid.SetRowSpan(drawer, 2);
                        _across.Children.Add(drawer);

                        continue;
                    }

                    Put(_across, Marker(Letter(c), column.Titled, column, rows: false), top, c);
                    Put(_across, ColumnHead(column, c), top + 1, c);
                }

                if (band > 0) Bands(_across, rows: false);

                // One control for one action at both ends. Previously a plus past the last column
                // and a name box under the last row: two controls for the same act, and on a
                // mirrored board one was a detour to the other. Both are now the plus, shaped like
                // the cells it continues.
                //
                // The name is typed in the right-hand panel, where everything else about a line is
                // set.
                Put(_across, Adding("Add a discipline", AddColumn, column: true),
                    top + 1, _board.Columns.Count);
            }

            /// <summary>The band down the left: the numbers, the names, and the box that adds one.</summary>
            private void RedrawDown()
            {
                _rowHeads.Clear();
                _down.Children.Clear();
                _down.ColumnDefinitions.Clear();
                _down.RowDefinitions.Clear();

                var band = FolderWide;

                if (band > 0)
                    _down.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(band),
                    });

                _down.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IndexWide) });
                _down.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderWide) });

                for (var r = 0; r <= _board.Rows.Count; r++)
                    _down.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(r < _board.Rows.Count && Hidden(_board.Rows[r])
                                                ? 0 : CellHigh),
                    });

                var left = band > 0 ? 1 : 0;

                for (var r = 0; r < _board.Rows.Count; r++)
                {
                    var row = _board.Rows[r];

                    if (Hidden(row)) continue;

                    if (Folded(row))
                    {
                        var drawer = Drawer(row.Folder, rows: true);

                        Grid.SetRow(drawer, r);
                        Grid.SetColumn(drawer, left);
                        Grid.SetColumnSpan(drawer, 2);
                        _down.Children.Add(drawer);

                        continue;
                    }

                    Put(_down, Marker(Number(r), row.Titled, row, rows: true), r, left);
                    Put(_down, RowHead(row, r), r, left + 1);
                }

                if (band > 0) Bands(_down, rows: true);

                // The same cell the columns end in, on the same terms. A box you typed into sat
                // here instead: a text field in the frame of a grid, against a plus at the other
                // end of the same act, so adding a discipline looked like two different jobs
                // depending on which edge you happened to be near.
                Put(_down, Adding("Add a discipline", AddRow, column: false),
                    _board.Rows.Count, left + 1);
            }

            /// <summary>
            /// The folder labels, laid over the band they gather.
            ///
            /// One block per run of lines answering to the same name, spanning exactly those
            /// lines - so a folder whose members have been separated shows as two blocks, which
            /// is a truthful drawing of a matrix somebody has pulled apart, and the tidy on the
            /// menu is what puts it back.
            /// </summary>
            /// <param name="into">The band grid.</param>
            /// <param name="rows">True for the rows.</param>
            private void Bands(Grid into, bool rows)
            {
                var lines = rows ? (IReadOnlyList<MatrixLine>)_board.Rows : _board.Columns;
                var at = 0;

                while (at < lines.Count)
                {
                    var folder = lines[at].Folder;

                    if (folder.Length == 0) { at++; continue; }

                    var run = at;

                    while (run + 1 < lines.Count
                           && string.Equals(lines[run + 1].Folder, folder,
                                            StringComparison.OrdinalIgnoreCase))
                        run++;

                    var block = Banded(folder, run - at + 1, rows);

                    if (rows)
                    {
                        Grid.SetColumn(block, 0);
                        Grid.SetRow(block, at);
                        Grid.SetRowSpan(block, run - at + 1);
                    }
                    else
                    {
                        Grid.SetRow(block, 0);
                        Grid.SetColumn(block, at);
                        Grid.SetColumnSpan(block, run - at + 1);
                    }

                    into.Children.Add(block);

                    at = run + 1;
                }
            }

            /// <summary>One folder's label, drawn along the lines it holds.</summary>
            /// <param name="folder">Its name.</param>
            /// <param name="held">How many lines it holds here.</param>
            /// <param name="rows">True down the side, false across the top.</param>
            private UIElement Banded(string folder, int held, bool rows)
            {
                var shut = _shut.Contains(folder);

                var block = new Border
                {
                    BorderThickness = rows
                        ? new Thickness(0, 0, 1, 1)
                        : new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand,
                    ToolTip = folder + " — " + Ui.Count(held, rows ? "row" : "column")
                              + ". Click to " + (shut ? "open it" : "shut it")
                              + ", right-click for what else it can do.",
                };

                block.Tint(Border.BorderBrushProperty, "Bd");

                // The band colour is what groups a run of rows into one package. Without it,
                // 22 disciplines in five packages read as 22 disciplines. An uncoloured folder
                // keeps the skin's own ground.
                var chosen = _board.ColourOf(folder);

                if (chosen.Length > 0) block.Background = Painted(chosen, "Card2");
                else block.Tint(Border.BackgroundProperty, shut ? "Nav" : "Card2");

                // Shut, it is a mark rather than a label, and a coloured folder says so by going
                // quieter rather than by changing colour - the colour is what identifies it.
                if (chosen.Length > 0 && shut) block.Opacity = 0.55;

                // Along the band, which is down it for the rows and across it for the columns.
                // The room a name has is the length of its own block, not the width of the strip.
                var room = Math.Max(16, (rows ? CellHigh : CellWide) * held - 10);

                // While it is shut the name is on the folder's own line, at the size that line
                // gives it - so the band beside it is a mark, not the same words a second time
                // squeezed into twenty-four pixels by thirty.
                UIElement said = shut
                    ? new TextBlock
                    {
                        Text = "\u25b8",
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                    : rows
                    ? Turned(Shown(folder, shut), room, 10, FontWeights.SemiBold,
                             at: TextAlignment.Center)
                    : new TextBlock
                    {
                        Text = Shown(folder, shut),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        MaxWidth = room,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                if (said is FrameworkElement inked) inked.Wearing("Body");

                block.Child = said;

                block.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;
                    Shut(folder);
                });

                block.ContextMenu = Parts.Context(() => FolderMenu(folder), null);

                // Dropping a line on a folder band files it there. The bands already accept a line
                // being moved within the order, and a folder is a position in that order. Applies
                // to either axis, since a folder spans both.
                block.AllowDrop = true;

                block.DragOver += (s, e) =>
                {
                    var taking = e.Data.GetDataPresent(RowCarried)
                                 || e.Data.GetDataPresent(ColumnCarried);

                    e.Effects = taking ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;

                    block.BorderThickness = taking
                        ? new Thickness(2, 2, 2, 2)
                        : new Thickness(0, 0, 1, 1);
                };

                block.DragLeave += (s, e) => block.BorderThickness = new Thickness(0, 0, 1, 1);

                block.Drop += (s, e) => Ui.Safely(() =>
                {
                    block.BorderThickness = new Thickness(0, 0, 1, 1);
                    e.Handled = true;

                    var carried = e.Data.GetData(RowCarried) as MatrixLine
                                  ?? e.Data.GetData(ColumnCarried) as MatrixLine;

                    if (carried == null) return;

                    // The band takes what the drag is carrying, which is the picked set when the
                    // drag started inside one - the same rule the reorder follows, so dragging
                    // eight rows onto a folder files eight rows.
                    var set = Moving(carried, e.Data.GetDataPresent(RowCarried))
                        .Where(one => !string.Equals(one.Folder, folder,
                                                     StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (set.Count == 0) return;

                    Remember(what: set.Count > 1
                                 ? "filing " + Ui.Count(set.Count, "line") + " in " + Quoted(folder)
                                 : "filing " + Quoted(set[0].Titled) + " in " + Quoted(folder));

                    foreach (var one in set) _board.File(one, folder);

                    Redraw();
                });

                Reachable(block,
                          "Folder " + folder + ", " + Ui.Count(held, rows ? "row" : "column")
                          + (shut ? ", shut." : ", open."),
                          () => Shut(folder));

                return block;
            }

            /// <summary>
            /// A shut folder, on the one line it collapses to.
            ///
            /// Down the side it is a row, so the name reads across it at the width a row header
            /// has. Across the top it is a column, so the name stays turned and uses the height a
            /// column header has. Either way it is the folder's own line, not a band beside it -
            /// which is what makes it big enough to read and big enough to press.
            /// </summary>
            /// <param name="folder">The folder.</param>
            /// <param name="rows">True down the side, false across the top.</param>
            private UIElement Drawer(string folder, bool rows)
            {
                var held = _board.InFolder(rows, folder).Count;
                var says = folder + "  ·  " + Ui.Count(held, "shut");

                var drawer = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Cursor = Cursors.Hand,
                    Padding = rows ? new Thickness(8, 0, 6, 0) : new Thickness(2, 6, 2, 5),
                    ToolTip = says + ". Click to open it, right-click for what else it can do.",
                };

                drawer.Tint(Border.BorderBrushProperty, "Bd");
                drawer.Tint(Border.BackgroundProperty, "Nav");

                if (rows)
                {
                    var line = new DockPanel { LastChildFill = true };

                    var mark = new TextBlock
                    {
                        Text = "\u25b8",
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 7, 0),
                    }.Wearing("Body");

                    DockPanel.SetDock(mark, Dock.Left);
                    line.Children.Add(mark);

                    line.Children.Add(new TextBlock
                    {
                        Text = says,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Center,
                    }.Wearing("Body"));

                    drawer.Child = line;
                }
                else
                {
                    // As long as the names it stands beside, less the room its own mark takes.
                    // Taken from the band's height instead, this had to know the padding and the
                    // gutters above the names to get back to the same number - three facts to
                    // keep in step with a header it is only standing next to.
                    drawer.Child = Turned(says, Math.Max(24, NameRun - 12), 11,
                                          FontWeights.SemiBold, at: TextAlignment.Left)
                                   .Wearing("Body");
                }

                drawer.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;
                    Shut(folder);
                });

                drawer.ContextMenu = Parts.Context(() => FolderMenu(folder), null);

                Reachable(drawer, says + ". Press to open it.", () => Shut(folder));

                return drawer;
            }

            /// <summary>A folder's name, with a mark on it while it is shut.</summary>
            /// <param name="folder">The name.</param>
            /// <param name="shut">Whether it is shut.</param>
            private static string Shown(string folder, bool shut) =>
                shut ? folder + "  \u25b8" : folder;

            /// <summary>
            /// Whether the frozen bands are drawn for the board the cells are about to be drawn
            /// for.
            ///
            /// One definition per line plus the add-a-discipline slot, in both, so a mismatch is
            /// exactly a band built against a different number of lines.
            /// </summary>
            private bool Banded() =>
                _down.RowDefinitions.Count == _board.Rows.Count + 1
                && _across.ColumnDefinitions.Count == _board.Columns.Count + 1;

            /// <summary>The cells themselves, which are all that scrolls.</summary>
            private void RedrawCells()
            {
                // The bands and the cells are one drawing, and the cells are the only part with
                // a path of its own: switching what the grid shows redraws them alone. A board
                // that grew between the last band draw and this one leaves rows of symbols with
                // no name beside them and columns with no heading over them — the grid still
                // reads as a grid, so nothing about it says which rows those are.
                //
                // Checked here rather than fixed at each call site, because the call sites are
                // where it was missed. A band that already matches costs one comparison.
                if (!Banded())
                {
                    RedrawDown();
                    RedrawAcross();
                }

                _sheet.Children.Clear();
                _sheet.ColumnDefinitions.Clear();
                _sheet.RowDefinitions.Clear();

                _sheet.HorizontalAlignment = HorizontalAlignment.Left;
                _sheet.VerticalAlignment = VerticalAlignment.Top;

                // A scrollbar's worth of extra scroll range past the last row.
                //
                // The horizontal bar is drawn inside the viewport and takes 17px off the bottom,
                // so content ending exactly at the last row cannot be scrolled clear of it: fully
                // wound down, the last row sits under the bar. On a matrix of any size that row is
                // the add-line control.
                //
                // The cells and the band beside them use the same offset, so row names and the
                // add control track the cells. Kept even when no bar is showing: a grid wants some
                // space after its last row, and a margin that appears with the bar can trigger a
                // further layout pass.
                _sheet.Margin = new Thickness(0, 0, 0, SystemParameters.HorizontalScrollBarHeight);

                for (var c = 0; c <= _board.Columns.Count; c++)
                    _sheet.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(c < _board.Columns.Count
                                               && Hidden(_board.Columns[c]) ? 0 : CellWide),
                    });

                for (var r = 0; r <= _board.Rows.Count; r++)
                    _sheet.RowDefinitions.Add(new RowDefinition
                    {
                        Height = new GridLength(r < _board.Rows.Count && Hidden(_board.Rows[r])
                                                ? 0 : CellHigh),
                    });

                for (var r = 0; r < _board.Rows.Count; r++)
                {
                    var row = _board.Rows[r];

                    if (Hidden(row)) continue;

                    // A collapsed folder's line represents the folder, not its first member.
                    // Drawing that member's cells there would attribute one discipline's decisions
                    // to a line labelled with the folder's name, so the line is drawn as a closed
                    // strip carrying no symbols.
                    if (Folded(row))
                    {
                        var strip = new Border();

                        strip.Tint(Border.BackgroundProperty, "Nav");

                        Grid.SetRow(strip, r);
                        Grid.SetColumn(strip, 0);
                        Grid.SetColumnSpan(strip, Math.Max(1, _board.Columns.Count));
                        _sheet.Children.Add(strip);

                        continue;
                    }

                    for (var c = 0; c < _board.Columns.Count; c++)
                    {
                        if (Hidden(_board.Columns[c])) continue;

                        if (Folded(_board.Columns[c]))
                        {
                            var down = new Border();

                            down.Tint(Border.BackgroundProperty, "Nav");

                            Grid.SetColumn(down, c);
                            Grid.SetRow(down, r);
                            _sheet.Children.Add(down);

                            continue;
                        }

                        var column = _board.Columns[c];
                        var cell = Cell(row, column, r, c);

                        _cells[(row, column)] = cell;
                        Put(_sheet, cell, r, c);
                    }
                }

                _sheet.Children.Add(Ui.Loose(_range));
                _sheet.Children.Add(Ui.Loose(_ring));

                RedrawRing();
            }

            private static void Put(Grid grid, UIElement child, int row, int column)
            {
                Grid.SetRow(child, row);
                Grid.SetColumn(child, column);
                grid.Children.Add(child);
            }

            /// <summary>The find box in the corner, and whether it has been wired yet.</summary>
            private readonly TextBox _finder = Ui.Text(string.Empty, null, 60);
            private bool _finding;

            /// <summary>
            /// Take the cursor to a row whose name, number or code holds what was typed.
            ///
            /// Never a filter. A matrix with rows hidden is a different matrix, and the one thing
            /// somebody wants from a search on a grid is to be looking at the row they named with
            /// everything around it still in place.
            /// </summary>
            /// <param name="text">What was typed. Nothing at all puts the search away.</param>
            /// <param name="onward">True to start from the row after the one already found.</param>
            private void Found(string? text, bool onward = false)
            {
                var wanted = (text ?? string.Empty).Trim();

                if (wanted.Length == 0 || _board.Rows.Count == 0) return;

                var from = onward && _atRow >= 0 ? _atRow + 1 : Math.Max(0, _atRow);

                for (var step = 0; step < _board.Rows.Count; step++)
                {
                    var at = ((from + step) % _board.Rows.Count + _board.Rows.Count)
                             % _board.Rows.Count;

                    var row = _board.Rows[at];

                    if (row.Name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0
                        && row.Number.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0
                        && row.Code.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    Go(at, Math.Max(0, _atColumn));
                    Pick(row, rows: true);

                    return;
                }
            }

            /// <summary>
            /// Rows typed, nothing across the top yet: the one press that makes it a matrix.
            ///
            /// The names go down the side because that is the list somebody has in their head.
            /// What goes across the top is the same list, and saying so is a press - but until it
            /// is pressed there is no grid, no keyboard and no progress, and nothing on the
            /// screen said which press it was.
            /// </summary>
            private UIElement Sideways()
            {
                var ways = new StackPanel { MaxWidth = 560 };

                ways.Children.Add(Bench.Ready(
                    Ui.Count(_board.Rows.Count, "row") + " down the side, and nothing across the top",
                    "A coordination matrix is the same disciplines both ways, so the columns are "
                    + "the rows. Until there are some, there are no pairs to decide."));

                ways.Children.Add(Ui.Row(
                    "The same disciplines across the top",
                    "One column per row, named, numbered and pointed at the same thing. This is "
                    + "what nearly every matrix is, and it can be pressed again later after rows "
                    + "are added or renamed.",
                    "Copy rows to columns",
                    () =>
                    {
                        Remember(what: "squaring the columns off to the rows");
                        _board.SquareOffRows();
                        Redraw();

                        Did(Ui.Done(Ui.Count(_board.Columns.Count, "column")
                                     + " to match the rows. Put a symbol in the cells that have "
                                     + "to be checked."));
                    }));

                ways.Children.Add(Ui.Row(
                    "Something else across the top",
                    "A grid of packages down the side against disciplines across the top is a "
                    + "real matrix too, and it has no diagonal and no mirror. Add the columns one "
                    + "at a time and name them as you go.",
                    "Add the first discipline",
                    AddColumn));

                return ways;
            }

            /// <summary>
            /// The screen shown before a matrix exists: the four ways to create one.
            ///
            /// Replaces a sentence and three buttons on an empty screen. Each option states what
            /// it produces; the one that costs nothing and finds the most is listed last.
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
                    "From saved sets",
                    sets == 0
                        ? "This model has no saved selection or search sets yet."
                        : Ui.Count(sets, "set") + " to choose from. Tick the ones that are the "
                          + "disciplines and each becomes a row, named after the set and already "
                          + "pointed at it.",
                    sets == 0 ? null : "Choose the sets",
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
                    "From a spreadsheet",
                    "The client's own workbook, with its own symbols and a key in the corner. "
                    + "You say which tab it is on and where on the sheet it starts; the legend is "
                    + "built from what it uses.",
                    "Open a workbook",
                    FromSheet));

                ways.Children.Add(Ui.Row(
                    "Start from scratch",
                    "One row at a time, pointed at whatever each one is: a set, whole models, "
                    + "what is selected, a branch of the tree.",
                    "Add the first discipline",
                    AddRow));

                return ways;
            }

            /// <summary>
            /// One row per saved set, named and numbered after it and already pointing at it.
            ///
            /// The disciplines are already in the document as the saved sets used to run the
            /// tests, so selecting them replaces adding, naming, numbering and pointing each row
            /// individually. The columns follow from the rows.
            ///
            /// Sets already on the matrix are left in place, so this can be run again after a set
            /// is added without disturbing the existing grid.
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
                    _board.Rows.Where(r => r.Source.IsSet)
                               .Select(r => r.Source.Folder.Length == 0
                                   ? r.Source.Name
                                   : r.Source.Folder + "/" + r.Source.Name),
                    StringComparer.OrdinalIgnoreCase);

                Parts.Search(_grid, "Which sets are the disciplines?",
                    sets.Select(s => new KeyValuePair<string, int>(Labelled(s), s.Members)).ToList(),
                    sets.Where(s => already.Contains(s.Trail)).Select(Labelled).ToList(), null,
                    picked =>
                    {
                        var wanted = new HashSet<string>(picked, StringComparer.OrdinalIgnoreCase);
                        var made = 0;

                        // In the order the model lists them, not the order they were ticked, so
                        // the matrix comes out alphabetical like the picker was.
                        Remember(what: "adding rows from the saved sets");

                        foreach (var set in sets)
                        {
                            if (!wanted.Contains(Labelled(set)) || already.Contains(set.Trail))
                                continue;

                            _board.AddPair(new MatrixLine(
                                set.Name,
                                Next(_board.Rows),
                                set.IsSearch
                                    ? MatrixSource.SearchSet(set.Name, set.Folder)
                                    : MatrixSource.SelectionSet(set.Name, set.Folder)));

                            made++;
                        }

                        if (made == 0)
                        {
                            Said(Ui.Note("Every set you ticked is already on the matrix."));
                            return;
                        }

                        // Each set brought its own column as it went on, so there is nothing
                        // left to square off - unless somebody has deliberately taken the two
                        // axes apart, and then squaring them would be overruling them.
                        Seed();
                        Redraw();

                        Did(Ui.Done(Ui.Count(made, "row") + " added, and the columns to match. "
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

                // The corner takes its size from the bands rather than setting it. It sits in the
                // same Auto row and column as the two bands, so the largest of the three sets the
                // size; that must be the headers, which have to fit over the cells they name. The
                // corner's contents go in a panel that requests no size, so a search box or legend
                // cannot make the top band taller than any header in it.
                corner.ClipToBounds = true;

                corner.Tint(Border.BorderBrushProperty, "Bd");
                corner.Tint(Border.BackgroundProperty, "Pane");

                var inside = new DockPanel { LastChildFill = false };

                // The corner is about 190 x 230px and previously held two lines of text. A matrix
                // of forty rows needs a way to reach a row by name, which is placed here.
                var find = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };

                var glass = Ui.Glyph(Glyphs.Find, 12, "Muted", new Thickness(0, 0, 5, 0));

                glass.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(glass, Dock.Left);
                find.Children.Add(glass);

                _finder.Tag = "Find a row";
                _finder.VerticalAlignment = VerticalAlignment.Center;
                _finder.FontSize = 11;

                if (!_finding)
                {
                    _finding = true;

                    _finder.TextChanged += (s, e) => Ui.Safely(() => Found(_finder.Text));

                    // Return walks on to the next row that matches, so a name shared by four
                    // rows is four presses rather than a search that always lands on the first.
                    _finder.KeyDown += (s, e) => Ui.Safely(() =>
                    {
                        if (e.Key != Key.Return) return;

                        e.Handled = true;
                        Found(_finder.Text, onward: true);
                    });
                }

                find.Children.Add(Ui.Loose(_finder));

                DockPanel.SetDock(find, Dock.Top);
                inside.Children.Add(find);

                // The two axes, in one weight rather than two: they are one fact said twice, and
                // an eyebrow over a small line read as a heading over a caption.
                var axes = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };

                axes.Children.Add(Ui.Small("Side A  ↓"));
                axes.Children.Add(Ui.Small("Side B  →"));

                DockPanel.SetDock(axes, Dock.Bottom);
                inside.Children.Add(axes);

                var follows = new Follows();

                follows.Children.Add(inside);

                corner.Child = follows;

                return Ui.Loose(corner);
            }

            /// <summary>
            /// A row's number or a column's letter, in the gutter beside it.
            ///
            /// Coordinates make a cell nameable. Without them the only way to identify a cell is
            /// to read two long names off opposite edges of the grid. Numbers down and letters
            /// across matches spreadsheet convention and how a coordination meeting refers to a
            /// cell.
            /// </summary>
            /// <param name="mark">The number or letter.</param>
            /// <param name="what">What it stands for, for the tooltip.</param>
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
                                  : Erasing
                                      ? ". Click to take every decision off the "
                                        + (rows ? "row." : "column.")
                                      : ". Click to put " + Quoted(_brush.Symbol) + " in every "
                                        + "undecided cell of the " + (rows ? "row." : "column.")),
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
                cell.Tint(Border.BackgroundProperty, "Nav");

                cell.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;
                    Sweep(line, rows);
                });

                Reachable(cell, (rows ? "Row " : "Column ") + mark + ", " + what
                                + ". Fills its undecided cells with the armed symbol.",
                          () => Sweep(line, rows));

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
            /// Previously opened a 14-item menu, four items of which opened further dialogues.
            /// The inspector shows all of it at once and allows editing in place. The menu remains
            /// on the right button.
            /// </summary>
            private UIElement RowHead(MatrixLine line, int at)
            {
                // The shell says what a row header is: its edge, and the gutters across it. The
                // band down the side measures the same shell, so the two cannot disagree.
                var head = RowShell();

                head.Cursor = Cursors.Hand;
                head.Background = System.Windows.Media.Brushes.Transparent;
                head.ToolTip = "Row " + Number(at) + ": " + line.Titled + " — "
                               + line.Source.Kinded() + ", " + line.Source.Describe();

                head.Tint(Border.BorderBrushProperty, "Bd");
                head.Tint(Border.BackgroundProperty, Chosen(line, rows: true) ? "NavActive" : "Pane");

                Grouped(head, line, Chosen(line, rows: true));

                var words = (Grid)head.Child;

                var numbered = Numbered(_board.Rows);
                var coded = Coded(_board.Rows);

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

                // The warning mark is drawn only on rows that need it. Drawn on every row it
                // marked nothing, and the case that matters (a row pointing at nothing) was a
                // shape change within a column of identical shapes. The space is reserved for the
                // whole axis whenever any row needs it, so the names stay aligned.
                if (Unbacked(_board.Rows))
                {
                    UIElement mark;

                    if (line.IsUnbacked)
                    {
                        var warn = Ui.Glyph(Glyphs.Warn, 12, "RedTx", new Thickness(0, 0, 6, 0));

                        warn.VerticalAlignment = VerticalAlignment.Center;
                        mark = warn;
                    }
                    else
                    {
                        mark = new Border { Width = 18 };
                    }

                    DockPanel.SetDock(mark, Dock.Left);
                    named.Children.Add(mark);
                }

                // Left, and staying left. Twenty-two names centred share no edge to scan down,
                // which is the whole of what a column of names in a table is for.
                var held = Custom(line);

                if (held != null)
                {
                    DockPanel.SetDock(held, Dock.Right);
                    named.Children.Add(held);
                }

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

                _rowHeads[line] = head;

                Draggable(head, line, rows: true);
                Picks(head, line, rows: true, at: at);

                return head;
            }

            /// <summary>Whether a field is filled by the scheme rather than typed.</summary>
            /// <param name="key">The field's key.</param>
            private bool Counted(string key) =>
                _board.Scheme.Field(key)?.Fill is MatrixFill.Series or MatrixFill.Same;

            /// <summary>
            /// The way back into the scheme for a line that has left it.
            ///
            /// Offered on the line rather than only in the fields window, because the line is
            /// where somebody notices the mark and asks what it is. Pressing it rewrites the
            /// line's counted fields from its position, so a row taken out by a typo is put back
            /// without anybody working out what its number should have been.
            /// </summary>
            /// <param name="line">The row or column.</param>
            /// <param name="at">Its position, for the undo note.</param>
            /// <param name="rows">Whether it is a row.</param>
            private UIElement Released(MatrixLine line, int at, bool rows)
            {
                var named = line.Pinned
                    .Select(one => _board.Scheme.Field(one)?.Title ?? one)
                    .OrderBy(one => one, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var says = Ui.Small("Held by hand: " + string.Join(", ", named) + ".");

                says.TextWrapping = TextWrapping.Wrap;

                var back = Ui.Quiet("Follow the fields again", () =>
                {
                    Remember("fields " + at + rows);

                    _board.Release(line);

                    RedrawDown();
                    RedrawAcross();
                    Recount();
                });

                back.HorizontalAlignment = HorizontalAlignment.Left;
                back.Margin = new Thickness(0, 2, 0, 0);

                var held = new Border
                {
                    Padding = new Thickness(8, 6, 8, 7),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 9),
                    Child = Ui.Stack(says, back),
                };

                held.Tint(Border.BackgroundProperty, "AccentWash");

                return held;
            }

            /// <summary>
            /// The mark that says a line has left the scheme, or null when it has not.
            ///
            /// A field counted down the axis is a promise that every line reads the same way, so
            /// the line that does not has to say so where the line is — not in the window where
            /// the promise was made. The tip names which fields are held, because "custom" on its
            /// own sends somebody opening the line to find out what about it is.
            /// </summary>
            /// <param name="line">The row or column.</param>
            private UIElement? Custom(MatrixLine line)
            {
                if (line == null || !line.IsCustom) return null;

                var named = line.Pinned
                    .Select(one => _board.Scheme.Field(one)?.Title ?? one)
                    .OrderBy(one => one, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var mark = Ui.Glyph(Glyphs.Pencil, 11, "Accent", new Thickness(4, 0, 0, 0));

                mark.VerticalAlignment = VerticalAlignment.Center;
                mark.ToolTip = "Held by hand, outside the fields: "
                               + string.Join(", ", named)
                               + ". The rest of this line still follows the fields.";

                return mark;
            }

            /// <summary>One column's header, which is a row header stood on its end.</summary>
            private UIElement ColumnHead(MatrixLine line, int at)
            {
                // The shell says what a column header is: its padding, and the rows it stacks.
                // The band across the top measures the same shell, so the two cannot disagree -
                // which is the whole of why the names used to come out ten pixels short.
                var head = ColumnShell();

                head.Cursor = Cursors.Hand;
                head.Background = System.Windows.Media.Brushes.Transparent;
                head.ToolTip = "Column " + Letter(at) + ": " + line.Titled + " — "
                               + line.Source.Kinded() + ", " + line.Source.Describe();

                head.Tint(Border.BorderBrushProperty, "Bd");
                head.Tint(Border.BackgroundProperty, Chosen(line, rows: false) ? "NavActive" : "Pane");

                Grouped(head, line, Chosen(line, rows: false));

                // A column header is a rotated row header and rotates the same way. Everything in
                // it is rotated: a rotated name over an upright number is two writing directions
                // in a 40px cell. The parts are in the same order as the row's, so both read
                // number, code, name: across in the row, down in the column.
                var stacked = (Grid)head.Child;

                var row = 0;

                // Two- and three-character values are not rotated. The name is, which is what
                // makes 22 columns fit; but "10" and "ME" fit upright in a 38px column, as the
                // row header demonstrates. Rotating them costs legibility for a symmetry that is
                // not perceived.
                if (Numbered(_board.Columns))
                {
                    if (line.Number.Length > 0)
                    {
                        var number = new TextBlock
                        {
                            Text = line.Number,
                            FontSize = 11,
                            TextAlignment = TextAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextWrapping = TextWrapping.NoWrap,
                        }.Wearing("Hint");

                        Grid.SetRow(number, row);
                        stacked.Children.Add(number);
                    }

                    row++;
                }

                if (Coded(_board.Columns))
                {
                    if (line.Code.Length > 0)
                    {
                        // Upright too, and the pill is now simply the size of the words in it
                        // rather than the size a turned box happened to occupy.
                        var chip = new Border
                        {
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(4, 0, 4, 1),
                            MaxWidth = Math.Max(16, CellWide - 6),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock
                            {
                                Text = line.Code,
                                FontSize = 10,
                                FontWeight = FontWeights.SemiBold,
                                TextAlignment = TextAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis,
                                TextWrapping = TextWrapping.NoWrap,
                            }.Wearing("Lbl"),
                        };

                        chip.Tint(Border.BackgroundProperty, "Nav");

                        Grid.SetRow(chip, row);
                        stacked.Children.Add(chip);
                    }

                    row++;
                }

                if (Unbacked(_board.Columns))
                {
                    if (line.IsUnbacked)
                    {
                        var warn = Ui.Glyph(Glyphs.Warn, 13, "RedTx");

                        warn.HorizontalAlignment = HorizontalAlignment.Center;
                        warn.VerticalAlignment = VerticalAlignment.Center;

                        Grid.SetRow(warn, row);
                        stacked.Children.Add(warn);
                    }

                    row++;
                }

                // Centred across the column, flush along it.
                //
                // Across the column, everything in a header is centred. Along the column, a
                // centred name floats: names of differing length start and end at differing
                // positions and the top edge of the grid stops reading as an edge. Flushed to the
                // end that meets the grid they align, and the ellipsis falls at the far end.
                //
                // Rotation solves a width problem, so it is applied only when there is one. At
                // six disciplines a column is 110px and the longest name fits upright, so the band
                // would be spending 177px of height for nothing. Upright when the names fit,
                // rotated when they do not.
                UIElement named;

                if (Upright)
                {
                    named = new TextBlock
                    {
                        Text = line.Name,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 11,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,

                        // Said outright rather than left to the layout. A centred block asks for
                        // the width its own words want, and what it is then given depends on what
                        // is around it - so the one thing that must be true, that the words stop
                        // at the column's edge, was the one thing nothing was stating.
                        MaxWidth = Math.Max(16, CellWide - 8),

                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(2, 0, 2, NameGap),
                    }.Wearing("Body");
                }
                else
                {
                    // Exactly the run the shell reserved for it, because they are the same
                    // property. Worked out separately - the band's height less the padding less
                    // everything above - the name ran ten pixels past the end of its own header
                    // and the tail was clipped by the border.
                    named = Turned(line.Name, NameRun, 11, FontWeights.SemiBold,
                                   at: TextAlignment.Left, flush: true).Wearing("Body");
                }

                Grid.SetRow(named, row);
                stacked.Children.Add(named);

                _columnHeads[line] = head;

                Draggable(head, line, rows: false);
                Picks(head, line, rows: false, at: at);

                return head;
            }

            /// <summary>
            /// One part of a column header, rotated to read up the column.
            ///
            /// Neither wrapping nor rotating with layout-driven sizing works here. A column would
            /// have to be 100px wide to hold two words, and the names were truncated anyway. With
            /// a plain LayoutTransform a rotated child is measured against the inverse-transformed
            /// space it is given, so inside a 40px cell the name's length is limited by the
            /// column's width, which is the constraint the rotation exists to escape.
            ///
            /// The text is therefore given the box it is to occupy explicitly (as long up the
            /// column as permitted, as thick as the column is wide) and rotated about its centre
            /// for drawing.
            /// </summary>
            /// <param name="text">What it says.</param>
            /// <param name="along">How far up the column it may run.</param>
            /// <param name="size">The type size.</param>
            /// <param name="weight">The weight.</param>
            /// <param name="at">Which end of its run the words sit at.</param>
            /// <param name="flush">
            /// True to sit against the end that meets the grid, false to sit in the middle
            /// of whatever it labels.
            /// </param>
            private static TextBlock Turned(string text, double along, double size,
                                            FontWeight weight,
                                            TextAlignment at = TextAlignment.Center,
                                            bool flush = false) =>
                new TextBlock
                {
                    Text = text,
                    FontWeight = weight,
                    FontSize = size,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,

                    // MaxWidth, not Width. As a Width the block requested a box longer than the
                    // column is wide and WPF clipped it to the slot, so a name reserved 160px of
                    // band and drew five characters in the middle of it. As a maximum it caps the
                    // run without overflowing.
                    MaxWidth = Math.Max(0, along),

                    TextAlignment = at,

                    // Across the column, always centred: the letters are thin in this direction
                    // and a header is read as a column of marks in one line.
                    HorizontalAlignment = HorizontalAlignment.Center,

                    // Down its length it sits either against the grid or in the middle of what
                    // it labels. A name flushed to the grid ends where every other name ends,
                    // which is the edge a reader scans down; a folder's label is centred on the
                    // run it covers, because that is what says which rows are in it.
                    VerticalAlignment = flush ? VerticalAlignment.Bottom : VerticalAlignment.Center,

                    // LayoutTransform, not RenderTransform. A RenderTransform moves pixels
                    // without affecting layout, so the block still requested a box as long as its
                    // text and one line thick, lying across a 38px column. WPF clips a child to
                    // its slot, so the rotated result was a name already truncated to the column
                    // width: about five characters per heading.
                    //
                    // A LayoutTransform is measured through: a 38 x 160px slot is offered to the
                    // block as 160px to write in and 38px to be thick in, which is how it is
                    // drawn, so nothing is clipped.
                    //
                    // Counter-clockwise, reading upwards, matching spreadsheet convention.
                    // Clockwise is legible but inverted relative to other tools. This way round
                    // the box's left edge lands at the bottom, so a left-aligned name starts at
                    // the grid and the ellipsis falls at the far end.
                    LayoutTransform = new System.Windows.Media.RotateTransform(-90),
                };

            /// <summary>
            /// Makes one part of the grid keyboard-reachable and nameable by a screen reader.
            ///
            /// Headers, gutter marks and palette keys are Borders with mouse handlers. A Border is
            /// not focusable, takes no part in the tab order and reports no name, so keyboard
            /// navigation stopped at the edge of the cells and a screen reader had only the
            /// tooltips, which require a hover.
            /// </summary>
            /// <param name="on">The part.</param>
            /// <param name="name">What it is, as a screen reader would read it.</param>
            /// <param name="onPress">What Space or Return does, which is what a click does.</param>
            private static void Reachable(UIElement on, string name, Action? onPress)
            {
                System.Windows.Automation.AutomationProperties.SetName(on, name);

                if (onPress == null) return;

                on.Focusable = true;

                // A Border has no theme style and therefore no focus visual, so parts made
                // keyboard-reachable drew nothing to indicate focus.
                if (on is Border edged)
                {
                    var was = edged.BorderThickness;

                    edged.GotKeyboardFocus += (s, e) =>
                    {
                        edged.BorderThickness = new Thickness(2);
                        edged.Tint(Border.BorderBrushProperty, "Accent");
                    };

                    edged.LostKeyboardFocus += (s, e) =>
                    {
                        edged.BorderThickness = was;
                        edged.Tint(Border.BorderBrushProperty, "Bd");
                    };
                }

                on.KeyDown += (s, e) => Ui.Safely(() =>
                {
                    if (e.Key != Key.Space && e.Key != Key.Return && e.Key != Key.Enter) return;

                    e.Handled = true;
                    onPress();
                });
            }

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

                    Ui.Safely(() =>
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                            Upto(line, rows);
                        else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                            Also(line, rows);
                        else Pick(line, rows);
                    });
                };

                // Renaming is available from the header as well as the inspector. With the
                // inspector closed, or absent, the inspector was the only way to name a row, so a
                // new matrix's first row stayed "New row" with nothing to press. Matches how a
                // test folder is renamed elsewhere.
                head.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount != 2) return;

                    e.Handled = true;

                    Ui.Safely(() => Parts.Ask(
                        head, rows ? "Name this row" : "Name this column", line.Name, "Rename",
                        typed =>
                        {
                            if (string.IsNullOrWhiteSpace(typed)) return;

                            Remember(what: "renaming a " + (rows ? "row" : "column"));

                            // The same full redraw the inspector's own box does: a name is part
                            // of the grid's shape, and the twin across the diagonal follows it.
                            _board.Rename(line, typed.Trim());

                            Redraw();
                            Pick(line, rows);
                        }));
                };

                head.ContextMenu = Parts.Context(() => LineMenu(line, at, rows), null);

                Reachable(head,
                          (rows ? "Row " + Number(at) : "Column " + Letter(at)) + ", "
                          + line.Titled + ". " + line.Source.Kinded()
                          + ". Double-click to rename it. Alt and an arrow moves it.",
                          () => Pick(line, rows));
            }

            /// <summary>Show one row or column in the inspector.</summary>
            /// <param name="line">The row or column.</param>
            /// <param name="rows">True for a row.</param>
            private void Pick(MatrixLine? line, bool rows)
            {
                _picked = line;
                _pickedRows = rows;
                _pickedSymbol = null;

                _chosen.Clear();
                _chosenRows = rows;

                if (line != null) _chosen.Add(line);

                Lighting(null);
                Editable();

                RedrawAcross();
                RedrawDown();
                RedrawInspector();
            }

            /// <summary>Whether a line is one of the picked ones, on the axis they were picked on.</summary>
            /// <param name="line">The row or column.</param>
            /// <param name="rows">True when it is being drawn as a row.</param>
            private bool Chosen(MatrixLine line, bool rows) =>
                _chosenRows == rows && _chosen.Contains(line);

            /// <summary>
            /// Add one line to the picked ones, or take it out again.
            ///
            /// Control and a press, which is what it is everywhere else. Taking out the anchor
            /// hands the anchor to whatever is left, so the panel on the right keeps describing
            /// something that is actually picked.
            /// </summary>
            /// <param name="line">The row or column pressed.</param>
            /// <param name="rows">True for a row.</param>
            private void Also(MatrixLine line, bool rows)
            {
                // The two axes are never mixed: a set holding three rows and two columns has no
                // act that means anything, and every act below reads one axis.
                if (_chosenRows != rows) { Pick(line, rows); return; }

                if (_chosen.Contains(line))
                {
                    _chosen.Remove(line);

                    if (ReferenceEquals(_picked, line)) _picked = _chosen.LastOrDefault();
                }
                else
                {
                    _chosen.Add(line);
                    _picked = line;
                }

                _pickedRows = rows;
                _pickedSymbol = null;

                Editable();
                RedrawAcross();
                RedrawDown();
                RedrawInspector();
            }

            /// <summary>
            /// Pick every line from the anchor to this one.
            ///
            /// Shift and a press, the run between what was last touched and this - the gesture
            /// every sheet shares, and the fastest way to say "these eight".
            /// </summary>
            /// <param name="line">The row or column pressed.</param>
            /// <param name="rows">True for a row.</param>
            private void Upto(MatrixLine line, bool rows)
            {
                var lines = rows ? (IReadOnlyList<MatrixLine>)_board.Rows : _board.Columns;
                var here = At(lines, line);
                var from = _chosenRows == rows && _picked != null ? At(lines, _picked) : -1;

                if (here < 0 || from < 0) { Pick(line, rows); return; }

                _chosen.Clear();
                _chosenRows = rows;

                for (var i = Math.Min(from, here); i <= Math.Max(from, here); i++)
                    if (!Hidden(lines[i]))
                        _chosen.Add(lines[i]);

                _picked = line;
                _pickedRows = rows;
                _pickedSymbol = null;

                Editable();
                RedrawAcross();
                RedrawDown();
                RedrawInspector();
            }

            /// <summary>One cell: the symbol in it, and every way of changing it.</summary>
            private Border Cell(MatrixLine row, MatrixLine column, int down, int across)
            {
                // Cells set no cursor of their own. The pointer over a cell depends on what is
                // armed, which changes without the cells being rebuilt, so it is set once on the
                // containing sheet and forced from there. Set per cell it would be a thousand
                // assignments to revisit on every palette press.
                var cell = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = System.Windows.Media.Brushes.Transparent,
                };

                cell.Tint(Border.BorderBrushProperty, "Bd");

                Paint(cell, row, column);

                // Painting takes the press; the menu takes the release, and only when nothing is
                // armed. Two gestures on one cell, and which one is live is said by the strip
                // over the grid rather than left to be discovered.
                cell.MouseLeftButtonDown += (s, e) => Ui.Safely(() =>
                {
                    // Shift takes the block from wherever the cursor already is to here, which
                    // is the one gesture every sheet shares and the fastest way to say "these
                    // thirty pairs".
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    {
                        e.Handled = true;
                        _scroll.Focus();
                        Reach(down, across);
                        return;
                    }

                    Land(down, across);

                    if (_brush == null) return;

                    e.Handled = true;

                    // One remembering per gesture, not one per cell: a drag across twenty cells
                    // is one thing somebody did and one press of undo should take it back.
                    Remember(what: "painting cells");
                    Daub(row, column);
                });

                cell.MouseEnter += (s, e) => Ui.Safely(() =>
                {
                    Crossed(row, column);

                    if (_brush == null
                        || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                        return;

                    // A drag is a bulk gesture and leaves the diagonal alone, like every other
                    // one. It was the only path that wrote it, so dragging across a rectangle
                    // and taking the same rectangle with shift and the arrows gave two different
                    // answers on the one cell the tool is otherwise careful about. Clicking it
                    // still writes it, because that is somebody deciding.
                    if (_board.IsSelfPair(row, column)) return;

                    Daub(row, column);
                });

                cell.MouseLeave += (s, e) => Ui.Safely(() => Crossed(null, null));

                cell.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;

                    if (_brush == null) Land(down, across);
                });

                // The whole menu is still one right-click away while a brush is armed.
                cell.ContextMenu = Parts.Context(() => CellMenu(row, column), null);

                return cell;
            }

            /// <summary>
            /// Light the two headers a cell belongs to, and put the last pair out.
            /// </summary>
            /// <param name="row">The row, or null to put them all out.</param>
            /// <param name="column">The column, or null.</param>
            private void Crossed(MatrixLine? row, MatrixLine? column)
            {
                if (ReferenceEquals(row, _crossRow) && ReferenceEquals(column, _crossColumn))
                    return;

                Lit(_crossRow, _rowHeads, false, _pickedRows);
                Lit(_crossColumn, _columnHeads, false, !_pickedRows);

                _crossRow = row;
                _crossColumn = column;

                Lit(row, _rowHeads, true, _pickedRows);
                Lit(column, _columnHeads, true, !_pickedRows);
            }

            /// <summary>Tint one header, unless it is the one the inspector is already showing.</summary>
            /// <param name="line">The row or column, or null.</param>
            /// <param name="heads">Where its header is kept.</param>
            /// <param name="lit">True to light it.</param>
            /// <param name="axis">True when the inspector's pick is on this axis.</param>
            private void Lit(MatrixLine? line, Dictionary<MatrixLine, Border> heads, bool lit,
                             bool axis)
            {
                if (line == null || !heads.TryGetValue(line, out var head)) return;

                if (axis && ReferenceEquals(_picked, line)) return;

                head.Tint(Border.BackgroundProperty, lit ? "Hover" : "Pane");
            }

            /// <summary>Which pair is lit at the moment.</summary>
            private MatrixLine? _crossRow;
            private MatrixLine? _crossColumn;

            /// <summary>
            /// Puts the cursor on one cell, focus on the grid, and the cell in the inspector.
            ///
            /// The inspector shows the last thing touched, and a cell is a touch. Previously it
            /// kept a row or symbol once one had been pressed, so pressing a row header stopped it
            /// answering about cells for the rest of the session.
            /// </summary>
            /// <param name="down">Which row.</param>
            /// <param name="across">Which column.</param>
            private void Land(int down, int across)
            {
                _atRow = down;
                _atColumn = across;

                var held = _picked != null;

                _picked = null;
                _pickedSymbol = null;
                _markRow = -1;
                _markColumn = -1;

                Lighting(null);

                _scroll.Focus();

                // Only when a header was tinted, because this runs on every press of every cell
                // and a drag across twenty of them must not rebuild two bands twenty times.
                if (held)
                {
                    RedrawAcross();
                    RedrawDown();
                }

                RedrawRing();
                RedrawStrip();
                RedrawInspector();
            }

            /// <summary>Put the armed symbol in one cell, if it is not already there.</summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The column.</param>
            private void Daub(MatrixLine row, MatrixLine column)
            {
                // Nothing is painted over a count read from the document. In the results view a
                // cell is a clash count, not an editable decision, and a brush stroke would write
                // symbols the view does not show, leaving the board different from what was
                // saved.
                if (_shown == Showing.Results) return;

                // The priority view applies ranks with the same gestures: a press or a drag lays
                // the armed rank down once per cell and changes nothing that already holds it.
                if (_shown == Showing.Priority)
                {
                    // A rank is a thing said about a test, so a cell with nothing in it has
                    // nothing to say it about. Ranking an undecided pair would put a priority on
                    // a test that is never going to be built.
                    if (_board.At(row, column) == null) return;
                    if (_board.RankAt(row, column) == _rank) return;

                    _board.Rank(row, column, _rank);
                    Repaint(row, column);
                    return;
                }

                if (_brush == null) return;

                var laying = Laying;

                // Dragging crosses the same cell many times; only a change is worth a repaint.
                if (string.Equals(_board.At(row, column), laying, StringComparison.Ordinal)) return;

                _board.Set(row, column, laying);
                Repaint(row, column);
            }

            /// <summary>
            /// Applies the armed symbol to the empty cells of one row or column.
            ///
            /// Empty cells only, with a count reported afterwards. Previously one click on a
            /// gutter overwrote every decision in the line, silently.
            ///
            /// With nothing armed it selects the line in the inspector, as pressing a header does
            /// elsewhere.
            /// </summary>
            /// <param name="line">The row or the column.</param>
            /// <param name="rows">True for a row, false for a column.</param>
            private void Sweep(MatrixLine line, bool rows)
            {
                // The gutter acts on the selection when the pressed line is part of it. Pressing
                // the gutter of a line outside the selection acts on that line alone and leaves
                // the selection unchanged.
                var many = Chosen(line, rows) && _chosen.Count > 1
                    ? _chosen.Where(l => !Hidden(l)).ToList()
                    : new List<MatrixLine> { line };

                // Whatever the grid is showing is what a fill lays down. The gesture is the same
                // one - the mark beside a row, or the same act off the menu - and it would be a
                // strange screen where it wrote symbols under a drawing of priorities.
                if (_shown == Showing.Priority)
                {
                    RankSweep(many, rows);
                    return;
                }

                // There is nothing to lay down over a count that came out of the document.
                if (_shown == Showing.Results) return;

                if (_brush == null)
                {
                    Pick(line, rows);
                    return;
                }

                Remember(what: many.Count > 1
                             ? "filling " + Ui.Count(many.Count, rows ? "row" : "column")
                             : "filling that " + (rows ? "row" : "column"));

                var laid = 0;

                foreach (var one in many)
                foreach (var other in rows ? (IReadOnlyList<MatrixLine>)_board.Columns : _board.Rows)
                {
                    var row = rows ? one : other;
                    var column = rows ? other : one;

                    if (_board.IsSelfPair(row, column)) continue;

                    // Filling works on what is undecided; the rubber works on what is decided.
                    // The same gesture over the opposite set of cells, so "put the brush down
                    // this row" means something whichever is on it.
                    if (Erasing ? _board.At(row, column) == null
                                : _board.At(row, column) != null) continue;

                    Daub(row, column);
                    laid++;
                }

                Reported(laid, Erasing ? "cleared" : Quoted(_brush.Symbol) + " put in",
                         (Erasing ? "decided cell of " : "empty cell of ")
                         + (rows ? "row " + Number(At(_board.Rows, line))
                                 : "column " + Letter(At(_board.Columns, line))));
            }

            /// <summary>
            /// Lay the armed rank down every checked cell of the picked rows or columns.
            ///
            /// The priority view's half of the fill. It writes only where a symbol already is,
            /// because a rank on an undecided pair is a priority for a test nobody has agreed
            /// to build - and it overwrites a rank that is already there, unlike the symbol
            /// fill, because ranking is a judgement somebody revises rather than a blank they
            /// are working through.
            /// </summary>
            /// <param name="many">The picked lines.</param>
            /// <param name="rows">True when they are rows.</param>
            private void RankSweep(IReadOnlyList<MatrixLine> many, bool rows)
            {
                var what = rows ? "row" : "column";

                Remember(what: many.Count > 1
                             ? "ranking " + Ui.Count(many.Count, what)
                             : "ranking that " + what);

                var laid = 0;

                foreach (var one in many)
                foreach (var other in rows ? (IReadOnlyList<MatrixLine>)_board.Columns : _board.Rows)
                {
                    var row = rows ? one : other;
                    var column = rows ? other : one;

                    if (_board.IsSelfPair(row, column)) continue;

                    var symbol = _board.At(row, column);

                    if (symbol == null || MatrixBoard.IsNotRequired(symbol)) continue;
                    if (_board.RankAt(row, column) == _rank) continue;

                    _board.Rank(row, column, _rank);
                    Repaint(row, column);
                    laid++;
                }

                Reported(laid,
                         _rank == MatrixBoard.NotSet
                             ? "unranked"
                             : "set to " + MatrixBoard.RankNamed(_rank).ToLowerInvariant(),
                         "checked cell of " + what);
            }

            /// <summary>
            /// Open the standing instructions, and let them fill the board.
            ///
            /// Remembered before the window opens rather than after it closes, because what a
            /// person wants back from one press of undo is the matrix as it was before they went
            /// in - not one of the four edits they made while they were in there.
            /// </summary>
            /// <summary>
            /// Edit the fields every line carries, then write them across the board.
            ///
            /// The apply is the point of the window rather than a step after it: a field counted
            /// down the axis is a promise about forty lines, and leaving the board unwritten until
            /// somebody found a second button would make the promise look broken.
            /// </summary>
            private void Fields()
            {
                var was = Held();

                if (!MatrixFields.Ask(_board)) return;

                var changed = _board.ApplyScheme();

                Touched();

                _past.Push(new Doing(was, "changing the fields"));
                _future.Clear();

                Redraw();

                Reported(changed, "written", "value");
            }

            private void Rulebook()
            {
                var was = Held();

                if (!MatrixRules.Ask(_board, _legend.Symbols.Select(one => one.Symbol).ToList()))
                    return;

                var changed = _board.ApplyRules();

                Touched();

                _past.Push(new Doing(was, "changing the fill rules"));
                _future.Clear();

                Redraw();

                Reported(changed, "written by rule", "cell of the grid");
            }

            /// <summary>
            /// Reports what a bulk action did, with an undo control.
            ///
            /// Reported afterwards rather than confirmed beforehand: a count that can be acted on
            /// is more useful than a dialogue that is clicked through. Filling the whole grid
            /// still confirms first.
            /// </summary>
            /// <param name="count">How many cells changed.</param>
            /// <param name="did">What was done, in the past tense.</param>
            /// <param name="unit">What was changed, singular.</param>
            private void Reported(int count, string did, string unit)
            {
                Did(count == 0
                    ? Ui.Note("Nothing to do: every " + unit + " already says something.")
                    : Ui.Done(did + " " + Ui.Count(count, unit) + ". Undo takes it back."));
            }

            /// <summary>
            /// Arms a symbol, or disarms the one already armed.
            ///
            /// Arming does not change what the inspector shows. It previously did, which closed
            /// the row page holding the fill button that requires an armed brush, so the button
            /// could not be reached in the state it needs. It also dimmed the grid, greying most
            /// cells with no explanation on screen.
            ///
            /// The armed brush and the inspector's subject are separate. The pencil beside the
            /// plus opens the armed symbol.
            /// </summary>
            /// <param name="symbol">The symbol, or null for none.</param>
            private void Arm(MatrixSymbol? symbol)
            {
                _brush = symbol != null && _brush != null
                         && string.Equals(_brush.Symbol, symbol.Symbol, StringComparison.Ordinal)
                    ? null
                    : symbol;

                RedrawStrip();
                RedrawPalette();
                RedrawInspector();
                Pointing();
                Editable();
            }

            /// <summary>
            /// Puts the armed symbol on the cursor, so the pointer indicates what a click does.
            ///
            /// A click in the grid either selects a cell or paints it. Previously that state was
            /// reported only in the strip at the top of the window, so an armed brush was usually
            /// discovered by painting an unintended cell.
            ///
            /// Set once on the containing sheet and forced from there. Arming a symbol does not
            /// rebuild the cells, so a cursor set per cell would report the symbol armed when
            /// those cells were last drawn.
            /// </summary>
            private void Pointing()
            {
                // In the priority view the cursor carries the armed rank, for the same reason it
                // carries a symbol elsewhere.
                if (_shown == Showing.Priority)
                {
                    var mark = _rank == MatrixBoard.High ? "H"
                        : _rank == MatrixBoard.Medium ? "M"
                        : _rank == MatrixBoard.Low ? "L"
                        : string.Empty;

                    var laid = "rank\u0001" + _rank.ToString(CultureInfo.InvariantCulture);

                    if (string.Equals(laid, _carrying, StringComparison.Ordinal)) return;

                    _carrying = laid;
                    _sheet.ForceCursor = true;

                    if (mark.Length == 0)
                    {
                        _sheet.Cursor = Cursors.Cross;
                        return;
                    }

                    var paint = Ranked(_rank);

                    _sheet.Cursor = Pointer.Carrying(mark, paint, Ui.InkOn(paint)) ?? Cursors.Pen;
                    return;
                }

                var wash = Washed(_brush == null ? null : _legend.Meaning(_brush.Symbol) ?? _brush);

                // Drawing a bitmap on every press of the palette is work nobody asked for, and
                // the answer only changes when the symbol or its colour does.
                var carrying = _brush == null
                    ? string.Empty
                    : _brush.Symbol + "\u0001" + (wash as System.Windows.Media.SolidColorBrush)?.Color.ToString(
                          CultureInfo.InvariantCulture);

                if (string.Equals(carrying, _carrying, StringComparison.Ordinal)) return;

                _carrying = carrying;

                // Forced, so a cell cannot answer for itself - and set even when nothing is
                // armed, because the hand over a cell is what says the cell can be pressed.
                _sheet.ForceCursor = true;

                if (_brush == null)
                {
                    _sheet.Cursor = Cursors.Hand;
                    return;
                }

                // The eraser takes the ground the palette gives it rather than a symbol's wash:
                // it is not one of them, and colouring it as though it were says it lays
                // something down.
                var ground = Erasing ? Skin.Of("Nav") : wash;

                _sheet.Cursor = Pointer.Carrying(_brush.Symbol, ground, Ui.InkOn(ground))
                                ?? (Erasing ? Cursors.Cross : Cursors.Pen);
            }

            /// <summary>Whether there is a symbol for the pencil to change.</summary>
            private void Editable()
            {
                if (_editing == null) return;

                var symbol = _pickedSymbol ?? _brush;

                _editing.IsEnabled = symbol != null && _legend.Meaning(symbol.Symbol) != null;
            }

            /// <summary>
            /// Draws the contents of one cell. The only thing a symbol change touches.
            ///
            /// The mirror half is drawn faintly. A clash matrix is mirrored about its diagonal
            /// (A against B is the same test as B against A), so the grid holds each pair twice.
            /// The lower half carries the same symbol quietly and names the other cell in its
            /// tooltip; writing to either half writes both.
            /// </summary>
            /// <summary>
            /// One cell in the results view.
            ///
            /// Zero and "no test covers this pair" are drawn differently: a tested pair with
            /// nothing open is complete, an untested pair is a gap in the coordination. Clean is a
            /// zero on a pale ground; untested is left blank and counted in the strip.
            ///
            /// The heat scale is normalised to the board's worst pair rather than a fixed number.
            /// </summary>
            /// <param name="cell">The cell.</param>
            /// <param name="row">Its row.</param>
            /// <param name="column">Its column.</param>
            private void Counted(Border cell, MatrixLine row, MatrixLine column)
            {
                cell.Opacity = 1.0;

                var at = Where(row, column);
                var spine = _board.IsSelfPair(row, column) && _style.Spine;

                // The spine's heavier edge, set on every cell rather than only on the ones that
                // have it - otherwise a cell that was on the diagonal in one view keeps the edge
                // in the next, and the line the eye follows is drawn twice.
                cell.BorderThickness = spine
                    ? new Thickness(0, 0, 2, 2)
                    : new Thickness(0, 0, 1, 1);

                if (spine && _style.SpineColour.Length > 0)
                    cell.BorderBrush = Painted(_style.SpineColour, "SpineBd");
                else cell.Tint(Border.BorderBrushProperty, spine ? "SpineBd" : "Bd");

                if (_board.IsSelfPair(row, column))
                {
                    Ground(cell, spine ? _style.SpineColour : string.Empty, spine ? "Spine" : "Nav");

                    cell.Child = null;
                    cell.ToolTip = at + " — a discipline against itself";
                    return;
                }

                var open = _counts.OpenBetween(row, column);
                var ever = _counts.EverBetween(row, column);

                // The mirror half is echoed in every view, including this one. Drawn at full
                // strength the results view printed every count twice and lost the diagonal among
                // them.
                if (!_board.IsDrawnHalf(row, column))
                {
                    cell.Background = System.Windows.Media.Brushes.Transparent;

                    cell.Child = open < 0 || !_style.Echo
                        ? null
                        : new TextBlock
                        {
                            Text = open.ToString(CultureInfo.InvariantCulture),
                            FontSize = Math.Max(10, Lettering - 2),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextWrapping = TextWrapping.NoWrap,
                            Opacity = 0.9,
                        }.Tint(TextBlock.ForegroundProperty, "Faint");

                    var pair = _board.MirrorOf(row, column);

                    cell.ToolTip = pair == null
                        ? at
                        : "The same pair as " + pair.Value.Row.Titled + " against "
                          + pair.Value.Column.Titled + ", which is the half this matrix keeps.";

                    Named(cell);
                    return;
                }

                if (open < 0)
                {
                    cell.Background = System.Windows.Media.Brushes.Transparent;
                    cell.Child = null;
                    cell.ToolTip = at + " — " + row.Titled + " against " + column.Titled
                                   + ". No test in the document covers this pair.";

                    Named(cell);
                    return;
                }

                var ground = open == 0 ? Skin.Of("OkBg") : Heat(open);

                cell.Background = ground;

                cell.Child = new TextBlock
                {
                    Text = open.ToString(CultureInfo.InvariantCulture),
                    FontSize = Math.Max(10, Lettering - 1),
                    FontWeight = open == 0 ? FontWeights.Normal : FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = Ui.InkOn(ground),
                };

                cell.ToolTip = at + " — " + row.Titled + " against " + column.Titled + ". "
                               + (open == 0
                                   ? "Tested, nothing open"
                                   : Ui.Count(open, "clash") + " still open")
                               + (ever > open
                                   ? " of " + ever.ToString(CultureInfo.InvariantCulture) + " found."
                                   : ".");

                Named(cell);
            }

            /// <summary>
            /// One cell of the priority view.
            ///
            /// A rank describes a test, so a cell with no test in it is left blank. Drawing "not
            /// set" across every undecided cell would obscure the pairs that are ranked. A checked
            /// but unranked pair is drawn as the faintest of the four.
            ///
            /// The three colours match the usual convention for the same three words, so the view
            /// reads without reference to the legend.
            /// </summary>
            /// <param name="cell">The cell.</param>
            /// <param name="row">Its row.</param>
            /// <param name="column">Its column.</param>
            private void Urgency(Border cell, MatrixLine row, MatrixLine column)
            {
                cell.Opacity = 1.0;

                var at = Where(row, column);
                var symbol = _board.At(row, column);
                var rank = _board.RankAt(row, column);
                var self = _board.IsSelfPair(row, column);
                var spine = self && _style.Spine;

                // The diagonal and the mirror half are properties of the board, not of the
                // symbols, so every view draws them. Set here rather than in a branch, so a cell
                // that was on the diagonal in one view does not keep the heavier edge in the
                // next.
                cell.BorderThickness = spine
                    ? new Thickness(0, 0, 2, 2)
                    : new Thickness(0, 0, 1, 1);

                if (spine && _style.SpineColour.Length > 0)
                    cell.BorderBrush = Painted(_style.SpineColour, "SpineBd");
                else cell.Tint(Border.BorderBrushProperty, spine ? "SpineBd" : "Bd");

                var mark = rank == MatrixBoard.High ? "H"
                    : rank == MatrixBoard.Medium ? "M"
                    : rank == MatrixBoard.Low ? "L"
                    : string.Empty;

                // The half that is not worked in, drawn the way it is drawn on the symbols: the
                // mark quietly, on no ground at all, so a row can still be read across without
                // the dead triangle competing with the live one.
                if (!_board.IsDrawnHalf(row, column))
                {
                    cell.Background = System.Windows.Media.Brushes.Transparent;

                    cell.Child = mark.Length == 0 || !_style.Echo
                        ? null
                        : new TextBlock
                        {
                            Text = mark,
                            FontSize = Math.Max(11, Lettering - 2),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextWrapping = TextWrapping.NoWrap,
                            Opacity = 0.9,
                        }.Tint(TextBlock.ForegroundProperty, "Faint");

                    var pair = _board.MirrorOf(row, column);

                    cell.ToolTip = pair == null
                        ? at
                        : "The same pair as " + pair.Value.Row.Titled + " against "
                          + pair.Value.Column.Titled + ". A matrix is mirrored, so one rank "
                          + "covers both.";

                    Named(cell);
                    return;
                }

                if (self)
                {
                    Ground(cell, _style.SpineColour, "Spine");

                    cell.Child = null;
                    cell.ToolTip = at + " \u2014 " + row.Titled + " against itself.";

                    Named(cell);
                    return;
                }

                if (symbol == null || MatrixBoard.IsNotRequired(symbol))
                {
                    // Left the way the symbols view leaves them, so the two drawings agree about
                    // which pairs exist: white for undecided, the ruled-out grey and its dash
                    // for a pair somebody has agreed not to check.
                    if (symbol == null)
                    {
                        cell.Background = System.Windows.Media.Brushes.Transparent;
                        cell.Child = null;
                    }
                    else
                    {
                        Ground(cell, _style.RuledColour, "BdStrong");

                        cell.Child = new TextBlock
                        {
                            Text = MatrixBoard.NotRequired,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        }.Tint(TextBlock.ForegroundProperty, "Muted");
                    }

                    cell.ToolTip = at + " \u2014 " + row.Titled + " against " + column.Titled
                                   + (symbol == null
                                       ? ". Not decided yet, so there is nothing to rank."
                                       : ". Agreed not required, so there is nothing to rank.");

                    Named(cell);
                    return;
                }

                var ground = Ranked(rank);

                cell.Background = ground;

                cell.Child = mark.Length == 0
                    ? null
                    : new TextBlock
                    {
                        Text = mark,
                        FontSize = Math.Max(10, Lettering - 1),
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        Foreground = Ui.InkOn(ground),
                    };

                cell.ToolTip = at + " \u2014 " + row.Titled + " against " + column.Titled + ". "
                               + (rank == MatrixBoard.NotSet
                                   ? "Checked, no priority set."
                                   : MatrixBoard.RankNamed(rank) + " priority.")
                               + " Checked as " + Quoted(symbol) + ".";

                Named(cell);
            }

            /// <summary>
            /// The ground for a rank: red, amber, green, and a pale nothing for unranked.
            ///
            /// The same three the host draws its own priority column in, so a matrix beside a
            /// results grid does not use two different scales for one word.
            /// </summary>
            /// <param name="rank">The rank.</param>
            private static System.Windows.Media.Brush Ranked(int rank)
            {
                // Read from Core rather than written here, because the export draws the same
                // three colours: a coordinator who marks a printed matrix up in red and hands it
                // back is marking up a picture of the screen, and two reds would make it a
                // picture of a different board.
                var colour = MatrixInk.OfRank(rank);

                return colour.Length == 0 ? Skin.Of("Card2") : Painted(colour, "Card2");
            }

            /// <summary>
            /// The wash for a count, from the mildest thing on the board to the worst.
            ///
            /// Scaled to the worst pair rather than to a fixed number of clashes: a job with
            /// nine thousand on its heaviest pair and one with ninety both want the same picture,
            /// which is where to go first.
            /// </summary>
            /// <param name="open">How many are still open on this pair.</param>
            private System.Windows.Media.Brush Heat(int open) =>
                // Pale amber to a strong red, which is the one ramp a coordinator reads without
                // a key - and the only two ends this needs, because the question is "how bad".
                // The ramp itself is Core's, so the sheet that goes to the meeting is the
                // picture the meeting was shown.
                Painted(MatrixInk.Heat(open, _counts.Worst), "Card2");

            private void Paint(Border cell, MatrixLine row, MatrixLine column)
            {
                // All three views draw the same board: the spine down the diagonal, the half
                // that is not worked in echoed quietly, and only the cells' own contents
                // different between them.
                if (_shown == Showing.Results) { Counted(cell, row, column); return; }
                if (_shown == Showing.Priority) { Urgency(cell, row, column); return; }

                var symbol = _board.At(row, column);
                var meaning = symbol == null ? null : _legend.Meaning(symbol);
                var mirrored = !_board.IsDrawnHalf(row, column);

                // The cell's coordinates first, so a tip answers "which one is this" before it
                // answers "what is in it" - which is the question somebody has when they are
                // reading a wrong symbol off a grid twenty wide.
                var at = Where(row, column);

                // The mirrored half carries no letter. At 45% opacity it was still 480 letters
                // on a grid holding 231 decisions, each reading as something to check. It remains
                // clickable, and a click writes the pair, so the user need not know which half
                // this board keeps.
                if (mirrored)
                {
                    // The mirror half is echoed at 90% opacity in the faintest ink.
                    //
                    // Drawn at full strength it competed with the live half. Omitted entirely, a
                    // row could not be read across, which is what the matrix is for in front of a
                    // client. At 55% of the faint ink the contrast was about 2:1 against the
                    // sheet, which does not survive a projector or a printed page. At 90% it is
                    // slightly over 3:1: subordinate to the live half without being invisible.
                    // Measured, not estimated; 85% gave 2.88:1 against the pane behind the sheet.
                    cell.Background = System.Windows.Media.Brushes.Transparent;
                    cell.Opacity = 1.0;

                    var echoed = symbol == null ? null : _legend.Meaning(symbol);

                    cell.Child = symbol == null || !_style.Echo
                        ? null
                        : new TextBlock
                        {
                            Text = symbol,
                            FontSize = Math.Max(11, Lettering - 2),
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            TextWrapping = TextWrapping.NoWrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,

                            // Ninety per cent. Quieter
                            // than the half that is live, and still a mark rather than a hint of
                            // one. Fifty-five was two to one, which is what a projector loses.
                            Opacity = 0.9,
                        }.Tint(TextBlock.ForegroundProperty, "Faint");

                    // Only two states carry across the echo: a disabled symbol and a symbol with
                    // no legend entry. Both mean the pair builds no test, so drawing them as
                    // ordinary decisions would make the mirrored half of a row read as handled.
                    // The strikethrough and the red carry across; nothing else does.
                    if (cell.Child is TextBlock echoing && !MatrixBoard.IsNotRequired(symbol))
                    {
                        if (echoed == null) echoing.Tint(TextBlock.ForegroundProperty, "RedTx");
                        else if (!echoed.Enabled)
                            echoing.TextDecorations = TextDecorations.Strikethrough;
                    }

                    if (cell.Child is UIElement quiet) quiet.Opacity *= Dimmed(symbol);

                    var pair = _board.MirrorOf(row, column);

                    cell.ToolTip = pair == null
                        ? at
                        : "The same test as " + pair.Value.Row.Titled + " against "
                          + pair.Value.Column.Titled + ", which is the cell above the diagonal. "
                          + "A matrix is mirrored, so this pair is only ever run once. Putting a "
                          + "symbol here still puts it in both.";

                    Named(cell);
                    return;
                }

                cell.Opacity = 1.0;

                var text = new TextBlock
                {
                    Text = symbol ?? string.Empty,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = Lettering,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                text.Wearing("Body");

                // The diagonal is a discipline against itself and remains editable. Steel against
                // steel finds members modelled through each other; a row merging two suppliers has
                // supplier-against-supplier clashes only here; a duplicates test is usually a
                // source against itself. It takes a click like any other cell, but no bulk fill
                // writes it, and it is drawn distinctly so a symbol there reads as deliberate.
                var self = _board.IsSelfPair(row, column);

                // Set here rather than in one branch, so a diagonal cell cleared of its symbol
                // does not keep the heavier edge that having one gave it.
                cell.BorderThickness = self && _style.Spine
                    ? new Thickness(0, 0, 2, 2)
                    : new Thickness(0, 0, 1, 1);

                var spine = self && _style.Spine;

                if (spine && _style.SpineColour.Length > 0)
                    cell.BorderBrush = Painted(_style.SpineColour, "SpineBd");
                else cell.Tint(Border.BorderBrushProperty, spine ? "SpineBd" : "Bd");

                if (MatrixBoard.IsNotRequired(symbol))
                {
                    // A ruled-out cell must not resemble an undecided one. It was a white cell
                    // with a 5px dash in the faintest ink, beside undecided cells that are white
                    // and empty: the two states with opposite meanings drawn as the least
                    // separable pair on the grid. A grey ground and a full-weight dash keep white
                    // meaning "still to do".
                    //
                    // On the diagonal the spine's colour takes precedence, so the line remains
                    // followable through whatever state its cells are in; the dash carries the
                    // rest.
                    //
                    // BdStrong rather than Nav: Nav against white is 1.17:1, which carried almost
                    // none of the separation this branch exists to create. BdStrong is 1.5:1, with
                    // the dash in Muted at full weight on top.
                    Ground(cell,
                           spine ? _style.SpineColour : _style.RuledColour,
                           spine ? "Spine" : "BdStrong");

                    cell.Child = new TextBlock
                    {
                        Text = MatrixBoard.NotRequired,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }.Tint(TextBlock.ForegroundProperty, "Muted");

                    cell.ToolTip = at + " · " + row.Titled + " against " + column.Titled
                                   + " — not required, agreed. No test is made. Delete empties "
                                   + "it back to undecided.";

                    return;
                }

                if (symbol == null)
                {
                    if (spine) Ground(cell, _style.SpineColour, "Spine");
                    else cell.Background = System.Windows.Media.Brushes.Transparent;

                    cell.ToolTip = at + " · "
                                   + (self
                                       ? row.Titled + " against itself — not tested. Bulk fills "
                                         + "leave this alone; click it to test a discipline "
                                         + "against itself deliberately."
                                       : row.Titled + " against " + column.Titled
                                         + " — not tested. Click to put a symbol in it.");
                }
                else if (meaning == null)
                {
                    cell.Tint(Border.BackgroundProperty, "RedBg");
                    text.Tint(TextBlock.ForegroundProperty, "RedTx");
                    cell.ToolTip = at + " · the legend does not say what " + Quoted(symbol)
                                   + " means, so this cell makes no test.";
                }
                else if (!meaning.Enabled)
                {
                    // A disabled symbol is struck through on the "makes no test" grey. Off, a
                    // cell previously moved from one pale fill to another 2% away, so cells could
                    // stop meaning anything while the grid still read as handled. On the diagonal
                    // it keeps the spine's colour so the line stays continuous; the strikethrough
                    // carries the meaning on either ground.
                    Ground(cell, spine ? _style.SpineColour : string.Empty, spine ? "Spine" : "Nav");

                    text.TextDecorations = TextDecorations.Strikethrough;
                    text.Tint(TextBlock.ForegroundProperty, "Muted");

                    cell.ToolTip = at + " · "
                                   + (self
                                       ? row.Titled + " against itself"
                                       : row.Titled + " against " + column.Titled)
                                   + " — " + Quoted(symbol) + " is turned off in the legend, so "
                                   + "this pair builds no test. Tick “Build tests from this "
                                   + "symbol” to bring it back.";
                }
                else
                {
                    // Each symbol gets its own wash from its position in the legend. The letter
                    // is always drawn, so the cell reads in greyscale, on a projector, and for
                    // users who do not distinguish these hues: colour makes the pattern
                    // scannable, it does not carry the meaning.
                    //
                    // The diagonal is excluded and keeps one colour. Broken into 22 washes it
                    // stops reading as a line, and a self-pair is a different kind of decision
                    // from the pairs around it. The letter still identifies the test, in italics.
                    if (spine)
                    {
                        Ground(cell, _style.SpineColour, "Spine");
                        text.FontStyle = FontStyles.Italic;
                    }
                    else
                    {
                        if (self) text.FontStyle = FontStyles.Italic;

                        cell.Background = Washed(meaning);

                        // Ink is chosen from the fill beneath rather than the theme. The ten
                        // washes are fixed (they belong to the matrix, not the seat), so a
                        // theme-supplied near-white letter sat on a pale pastel at about 1:1 in
                        // the dark palette.
                        if (meaning.Colour.Length > 0)
                            text.Foreground = Ui.InkOn(cell.Background);
                    }

                    cell.ToolTip = at + " · "
                                   + (self
                                       ? row.Titled + " against itself"
                                       : row.Titled + " against " + column.Titled)
                                   + " — " + MatrixNaming.Worded(meaning.Test) + " at "
                                   + Millimetres(meaning.Tolerance)
                                   + Finding(row, column);
                }

                cell.Child = text;

                var faded = Dimmed(symbol);

                if (faded < 1.0)
                {
                    text.Opacity = faded;

                    if (cell.Background is System.Windows.Media.SolidColorBrush wash
                        && wash.Color.A > 0)
                        cell.Background = Faded(wash, faded);
                }

                Ruled(cell, row, column);
                Named(cell);
            }

            /// <summary>
            /// Marks a cell written by a fill rule and names the rule in its tooltip.
            ///
            /// A rule-filled cell and a hand-filled cell are otherwise identical, and the
            /// difference matters: a rule-filled cell will change again when the rules or folders
            /// change, and a hand-filled one will not.
            ///
            /// Drawn as a corner mark rather than a second character, so it reads across a block
            /// without competing with the symbol.
            /// </summary>
            /// <param name="cell">The cell.</param>
            /// <param name="row">Its row.</param>
            /// <param name="column">Its column.</param>
            private void Ruled(Border cell, MatrixLine row, MatrixLine column)
            {
                if (!_board.IsByRule(row, column)) return;

                var corner = new System.Windows.Shapes.Path
                {
                    Data = System.Windows.Media.Geometry.Parse("M0,0 L6,0 L0,6 Z"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Opacity = 0.55,
                    IsHitTestVisible = false,
                };

                corner.Tint(System.Windows.Shapes.Path.FillProperty, "Muted");

                var both = new Grid();

                both.Children.Add(corner);

                if (cell.Child is UIElement had)
                {
                    cell.Child = null;
                    both.Children.Add(had);
                }

                cell.Child = both;

                var rule = _board.RuleOn(row, column);

                cell.ToolTip = (cell.ToolTip as string ?? string.Empty)
                               + (rule == null
                                   ? "  Put here by a fill rule."
                                   : "  Put here by the rule " + Quoted(rule.Worded())
                                     + ". Write in it to overrule that.");
            }

            /// <summary>
            /// What a cell is, for anything reading the screen out loud.
            ///
            /// The tip already says it, in the words somebody would want; a tip needs a hover,
            /// and a hover is the one thing a narrator's user cannot do.
            /// </summary>
            /// <param name="cell">The cell.</param>
            private static void Named(Border cell) =>
                System.Windows.Automation.AutomationProperties.SetName(
                    cell, cell.ToolTip as string ?? string.Empty);

            /// <summary>
            /// The wash a symbol's cells carry: its own colour, or one derived from its position
            /// in the legend.
            ///
            /// An empty colour means "assigned automatically", which is the default state. A
            /// legend of four symbols therefore gets four distinguishable colours without any
            /// being chosen, while a practice that always draws clearances green can state that.
            /// </summary>
            /// <param name="meaning">The legend entry, or null.</param>
            private System.Windows.Media.Brush Washed(MatrixSymbol? meaning) =>
                meaning == null
                    ? Skin.Of("Nav")
                    : meaning.Colour.Length > 0
                        ? Painted(meaning.Colour, "Nav")
                        : Skin.Symbol(Hue(meaning.Symbol));

            /// <summary>
            /// Paint a cell with a colour somebody chose, or with the skin's own when they have
            /// not.
            ///
            /// A chosen colour is a fixed brush and a skin key is a live one, so the two cannot
            /// be set the same way: a cell given a brush keeps it when the theme changes, which
            /// is right - the colour is the matrix's, not the theme's.
            /// </summary>
            /// <param name="cell">The cell.</param>
            /// <param name="colour">What was chosen, or empty.</param>
            /// <param name="key">The skin key to fall back to.</param>
            private static void Ground(Border cell, string colour, string key)
            {
                if (colour.Length > 0) cell.Background = Painted(colour, key);
                else cell.Tint(Border.BackgroundProperty, key);
            }

            /// <summary>
            /// Tints the header of every line in a coloured folder with a pale wash of that
            /// colour.
            ///
            /// The band alone identifies the group but not its members, and on a wide matrix
            /// scrolled sideways the band is the first thing off screen, so the folder colour was
            /// not visible beside the discipline it applies to.
            ///
            /// The wash is a fraction of the distance to white rather than a fixed pale colour,
            /// so a strong and an already-pale folder colour both produce a legible tint. Cells
            /// are excluded: a cell's colour states what is decided in it, and a second colour
            /// beneath would put two meanings on one ground.
            ///
            /// A selected line keeps its selection ground, which is transient state rather than a
            /// property of the line.
            /// </summary>
            /// <param name="head">The header.</param>
            /// <param name="line">The row or column it stands for.</param>
            /// <param name="chosen">True while it is selected.</param>
            private void Grouped(Border head, MatrixLine line, bool chosen)
            {
                if (chosen) return;

                var colour = _board.ColourOf(line.Folder);

                if (colour.Length == 0) return;

                head.Background = Washing(colour, 0.62);
            }

            /// <summary>
            /// A colour most of the way to white, for a ground something has to be read on.
            ///
            /// Blended rather than made transparent: the headers sit over a pane whose own
            /// colour differs between the two palettes, and an alpha wash would come out a
            /// different hue on each of them.
            /// </summary>
            /// <param name="colour">The colour, as <c>#RRGGBB</c>.</param>
            /// <param name="toward">How far to white, nought being the colour itself.</param>
            private static System.Windows.Media.Brush Washing(string colour, double toward)
            {
                var found = Painted(colour, "Pane");

                if (!(found is System.Windows.Media.SolidColorBrush solid)) return found;

                var how = Math.Min(1.0, Math.Max(0.0, toward));

                var pale = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)(solid.Color.R + ((255 - solid.Color.R) * how)),
                        (byte)(solid.Color.G + ((255 - solid.Color.G) * how)),
                        (byte)(solid.Color.B + ((255 - solid.Color.B) * how))));

                pale.Freeze();

                return pale;
            }

            /// <summary>
            /// One chosen colour as a brush, or the skin's own when it cannot be read.
            ///
            /// Core keeps these as text and checks only that they look like a colour; this is
            /// where one becomes one, and a string that got past both is still not a crash.
            /// </summary>
            /// <param name="colour">The colour, as <c>#RRGGBB</c>.</param>
            /// <param name="key">The skin key to fall back to.</param>
            private static System.Windows.Media.Brush Painted(string colour, string key)
            {
                if (!_inks.TryGetValue(colour, out var brush))
                {
                    try
                    {
                        var made = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                                .ConvertFromString(colour));

                        made.Freeze();
                        brush = made;
                    }
                    catch (Exception)
                    {
                        brush = null;
                    }

                    _inks[colour] = brush;
                }

                return brush ?? Skin.Of(key);
            }

            /// <summary>
            /// Every colour that has been asked for, made once.
            ///
            /// Paint runs per cell and a matrix of twenty-two is four hundred and eighty-four of
            /// them on every redraw; parsing the same six characters that many times is work
            /// nobody asked for. Null in here is a colour that could not be read, remembered so
            /// it is not tried again either.
            /// </summary>
            private static readonly Dictionary<string, System.Windows.Media.Brush?> _inks =
                new Dictionary<string, System.Windows.Media.Brush?>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Where one symbol sits in the legend, which is the wash it is drawn on.
            ///
            /// A symbol the legend has never heard of is drawn in the red of an unexplained
            /// cell rather than on a wash, so it never reaches here; -1 is the safe answer for
            /// the reserved mark, which is not a legend entry either.
            /// </summary>
            /// <param name="symbol">The symbol in the cell.</param>
            private int Hue(string? symbol)
            {
                if (symbol == null) return 0;

                for (var at = 0; at < _legend.Symbols.Count; at++)
                    if (string.Equals(_legend.Symbols[at].Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        return at;

                return 0;
            }

            /// <summary>
            /// How strongly a cell is drawn while one symbol is being highlighted.
            ///
            /// Selecting a symbol reported how many cells used it without indicating which. Cells
            /// not carrying it are drawn back while the highlight is on, so the matches are at
            /// full strength. Nothing is hidden, moved or filtered, so the shape of the matrix is
            /// unchanged.
            ///
            /// The fade is applied to the cell content, not the border. Applied to the border it
            /// took the grid lines and the diagonal with it, leaving the highlighted cells on a
            /// blank field with nothing to read coordinates against. Three tenths rather than two:
            /// at two the grid structure was also lost.
            /// </summary>
            /// <param name="symbol">What is in the cell.</param>
            private double Dimmed(string? symbol) =>
                _lit == null || string.Equals(_lit, symbol, StringComparison.OrdinalIgnoreCase)
                    ? 1.0
                    : 0.3;

            /// <summary>
            /// One wash mixed back towards the sheet, so a cell can drop back without its rules
            /// dropping back with it.
            /// </summary>
            /// <param name="wash">The colour the cell carries.</param>
            /// <param name="much">How much of it to keep, from zero to one.</param>
            private static System.Windows.Media.Brush Faded(
                System.Windows.Media.SolidColorBrush wash, double much)
            {
                var ground = (Skin.Of("Card") as System.Windows.Media.SolidColorBrush)?.Color
                             ?? System.Windows.Media.Colors.White;

                var one = wash.Color;

                var made = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        (byte)((one.R * much) + (ground.R * (1 - much))),
                        (byte)((one.G * much) + (ground.G * (1 - much))),
                        (byte)((one.B * much) + (ground.B * (1 - much)))));

                made.Freeze();

                return made;
            }

            /// <summary>The symbol being looked for, or null when none is.</summary>
            private string? _lit;

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

            /// <summary>
            /// Something changed: save in a few seconds, unless something else changes first.
            ///
            /// Restarting the timer on every act is the point. Twenty cells dragged in four
            /// seconds is one save rather than twenty, and the file is written when the hand
            /// stops.
            /// </summary>
            private void Touched()
            {
                _saver.Stop();
                _saver.Start();
            }

            private void Recount()
            {
                var plan = MatrixPlanner.Plan(_board, _legend, _pattern);

                _count.Text = plan.Explain();
                _count.ToolTip = plan.Tests.Count == 0
                    ? null
                    : "First one: " + Quoted(plan.Tests[0].Name);

                Warnings();
                RedrawProgress();
            }

            /// <summary>
            /// What the blue button has to say for itself before it is pressed.
            ///
            /// Counted from the lines themselves rather than from a full sanity check, because
            /// this runs on every keystroke and a sanity check asks the document what it holds.
            /// A row pointing at nothing is the fault that check finds most often and the only
            /// one that is free to spot from here.
            /// </summary>
            private void Warnings()
            {
                var lost = 0;

                foreach (var line in _board.Rows) if (line.IsUnbacked) lost++;
                foreach (var line in _board.Columns) if (line.IsUnbacked) lost++;

                // Checks whether the document accepts writes at all, not merely whether a clash
                // engine exists. On a document that refuses writes, the matrix was drawn, the
                // build was pressed, and the refusal arrived afterwards as three lines out of two
                // hundred.
                var shut = _session.Clash.IsAvailable && !_session.Clash.CanEdit;

                _build.ToolTip = shut
                    ? "This document will not take new tests — it is open read-only, or the seat "
                      + "has no licence to write them. The matrix saves and exports as it is."
                    : lost == 0
                        ? "Write the tests this matrix asks for into the document."
                        : Ui.Count(lost, "row or column") + " points at nothing this model has, so "
                          + "their cells build no tests. Press Check to see which.";
            }

            /// <summary>
            /// How many of the decidable pairs have been decided.
            ///
            /// A matrix is complete when every pair has been ruled on, either tested or
            /// deliberately not tested. The rail counts the cells in the live half, reports how
            /// many are still empty, and offers to move the cursor to the first of them.
            /// </summary>
            private void RedrawProgress()
            {
                _progress.Children.Clear();

                if (_board.Rows.Count == 0 || _board.Columns.Count == 0) return;

                // The foot bar counts what the active view measures. It is the same bar in every
                // view, so "160 of 190 decided" under the priority view answers a question that
                // view is not asking.
                if (_shown == Showing.Priority)
                {
                    RankProgress();
                    return;
                }

                if (_shown == Showing.Results)
                {
                    CountProgress();
                    return;
                }

                var live = 0;
                var done = 0;
                var ruled = 0;
                var mute = 0;

                foreach (var row in _board.Rows)
                foreach (var column in _board.Columns)
                {
                    if (!_board.IsKeptHalf(row, column)) continue;

                    var held = _board.At(row, column);

                    // A discipline against itself is not an open pair. Counted as one, the bar
                    // stopped at 190 of 210 on a 20-discipline matrix and never moved, and "go to
                    // the first open pair" parked on the diagonal. It counts only once something
                    // has been put in it deliberately.
                    if (held == null && _board.IsSelfPair(row, column)) continue;

                    live++;

                    if (held == null) continue;

                    // A pair ruled out is decided. That is the whole point of being able to say
                    // so: the bar reaches the end when every pair has been ruled on, not when
                    // every pair has been given a test.
                    done++;

                    if (MatrixBoard.IsNotRequired(held)) { ruled++; continue; }

                    // A cell whose symbol is disabled is decided but builds nothing, so it is
                    // counted as decided and named separately. Counted as done without comment,
                    // the bar would report a finished matrix while forty pairs go untested.
                    var meaning = _legend.Meaning(held);

                    if (meaning != null && !meaning.Enabled) mute++;
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

                // The bar's fill uses star-width columns rather than a fixed pixel width. Fixed
                // at 168px inside a track that stretches to the panel (243px), a completed matrix
                // stopped its bar at 69%.
                var run = new Grid();

                run.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(live == 0 ? 0 : done, GridUnitType.Star),
                });

                run.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(live == 0 ? 1 : Math.Max(0, live - done), GridUnitType.Star),
                });

                var filled = new Border();

                filled.Tint(Border.BackgroundProperty, "Accent");

                Grid.SetColumn(filled, 0);
                run.Children.Add(filled);

                track.Child = run;

                _progress.Children.Add(track);

                var left = live - done;

                _progress.Children.Add(Ui.Small(done.ToString(CultureInfo.InvariantCulture)
                    + " of " + live.ToString(CultureInfo.InvariantCulture) + " pairs"
                    + (left == 0
                        ? ". All of them."
                        : ", " + left.ToString(CultureInfo.InvariantCulture) + " still open")));

                if (ruled > 0)
                    _progress.Children.Add(Ui.Small(Ui.Count(ruled, "of them ruled out")));

                if (mute > 0)
                {
                    var off = Ui.Small(Ui.Count(mute, "of them")
                        + " carry a symbol that is turned off, so they build no test");

                    off.TextWrapping = TextWrapping.Wrap;
                    off.Tint(TextBlock.ForegroundProperty, "AmberTx");

                    _progress.Children.Add(off);
                }

                if (left > 0)
                {
                    var go = Ui.Quiet("Go to the first undecided pair", Undecided);

                    go.HorizontalAlignment = HorizontalAlignment.Left;
                    go.Margin = new Thickness(0, 6, 0, 0);

                    _progress.Children.Add(go);
                }
            }

            /// <summary>
            /// Walk the cursor to the next cell carrying one symbol, wrapping at the end.
            ///
            /// It does not touch what the inspector is showing, so the symbol stays on the panel
            /// and its cells stay lit while somebody walks them - which is the whole gesture:
            /// "show me every clearance" is asked once and answered as many times as there are
            /// clearances.
            /// </summary>
            /// <param name="symbol">The symbol to look for.</param>
            private void Stepping(string symbol)
            {
                var cells = _board.Rows.Count * _board.Columns.Count;

                if (cells == 0) return;

                var from = Math.Max(0, _atRow) * _board.Columns.Count + Math.Max(0, _atColumn);

                for (var step = 1; step <= cells; step++)
                {
                    var at = (from + step) % cells;
                    var r = at / _board.Columns.Count;
                    var c = at % _board.Columns.Count;

                    if (!string.Equals(_board.At(_board.Rows[r], _board.Columns[c]), symbol,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;

                    Go(r, c);
                    return;
                }
            }

            /// <summary>
            /// Move the cursor without disturbing what the inspector is showing.
            ///
            /// <see cref="Land"/> is what a press on a cell does and it clears the panel, which
            /// is right for a press and wrong for a tool that is walking the grid on the panel's
            /// behalf.
            /// </summary>
            /// <param name="down">Which row.</param>
            /// <param name="across">Which column.</param>
            private void Go(int down, int across)
            {
                _atRow = down;
                _atColumn = across;
                _markRow = -1;
                _markColumn = -1;

                RedrawRing();
                Bring();
                RedrawStrip();
            }

            /// <summary>Put the cursor on the first pair nobody has ruled on.</summary>
            private void Undecided()
            {
                for (var r = 0; r < _board.Rows.Count; r++)
                for (var c = 0; c < _board.Columns.Count; c++)
                {
                    if (!_board.IsKeptHalf(_board.Rows[r], _board.Columns[c])) continue;
                    if (_board.IsSelfPair(_board.Rows[r], _board.Columns[c])) continue;
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
            /// Allows a header to be dragged to a new position on its own axis.
            ///
            /// A drag must be distinguished from a click, since the same header opens a menu. A
            /// press records the origin only; it becomes a drag once the pointer passes the
            /// system threshold, and the resulting flag suppresses the menu the release would
            /// otherwise open.
            ///
            /// Rows drop on rows and columns on columns; a row dropped on a column header has no
            /// meaningful result.
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
                        var what = rows ? "row" : "column";
                        var set = Moving(moved, rows);

                        // Dropped on one of its own members, the set has nowhere to go: the
                        // block would be asked to land inside itself, and the answer would be
                        // whatever the arithmetic happened to produce.
                        if (set.Any(one => ReferenceEquals(one, line))) return;

                        var lines = rows ? _board.Rows : _board.Columns;

                        if (lines.ToList().FindIndex(l => ReferenceEquals(l, line)) < 0) return;

                        Remember(what: set.Count > 1
                                     ? "moving " + Ui.Count(set.Count, what)
                                     : "moving that " + what);

                        // The set lands as a contiguous block in board order. Each line goes to
                        // the index after the previous one, read back from the board rather than
                        // computed in advance: a move from below the target shifts every index
                        // above it.
                        var at = At(lines, line);

                        foreach (var one in set)
                        {
                            // With its twin on the other axis, so grouping a matrix is one job.
                            _board.MoveLine(one, at);

                            at = At(rows ? _board.Rows : _board.Columns, one) + 1;
                        }

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

            /// <summary>
            /// What a drag from this header moves: the current selection, or the single line.
            ///
            /// A drag starting inside a selection acts on the selection, matching the rule the
            /// menus and the gutter already follow. Previously the selection stayed put and only
            /// the dragged line moved.
            ///
            /// Returned in board order rather than selection order, so a dropped set keeps its
            /// existing arrangement.
            /// </summary>
            /// <param name="line">The line the drag started on.</param>
            /// <param name="rows">True for rows.</param>
            private List<MatrixLine> Moving(MatrixLine line, bool rows)
            {
                if (!Chosen(line, rows) || _chosen.Count < 2)
                    return new List<MatrixLine> { line };

                var order = rows ? (IReadOnlyList<MatrixLine>)_board.Rows : _board.Columns;

                return order.Where(one => _chosen.Any(picked => ReferenceEquals(picked, one)))
                            .ToList();
            }

            /// <summary>What a dragged row and a dragged column are carried as.</summary>
            private const string RowCarried = "CamelWorks.MatrixRow";
            private const string ColumnCarried = "CamelWorks.MatrixColumn";


            // ---------------------------------------------------------------------------
            // Rows, columns and cells
            // ---------------------------------------------------------------------------

            /// <summary>
            /// Adds a discipline: a row, its matching column, and focus in the name field.
            ///
            /// Previously two separate actions. "Add a discipline" added a row only, "add a
            /// column" added a column only, and they ended in different places. Building a
            /// 20-discipline matrix therefore meant setting each discipline up twice, and any
            /// difference in the two names broke the mirroring: both cells of the pair went live
            /// and the dead half gained a gap.
            ///
            /// A clash matrix is a list of disciplines against itself, so this is one action
            /// across both axes with the same result wherever it is invoked.
            /// </summary>
            private void AddRow()
            {
                Remember(what: "adding a category");

                var made = _board.AddPair(new MatrixLine(Fresh, Next(_board.Rows)));

                Seed();
                Redraw();
                Pick(made, rows: true);

                _scroll.ScrollToBottom();
                Naming();
            }

            /// <summary>
            /// Put the caret where a new line gets its name.
            ///
            /// The panel on the right, which is already open, already says what the picked line
            /// is, and is where every other thing about it is changed. A box in the grid's own
            /// frame said the same thing in a second place and made adding a discipline look like
            /// a different job from renaming one.
            /// </summary>
            private void Naming() =>
                _inspector.Dispatcher.BeginInvoke(
                    new Action(() => Ui.Safely(() => _rename?.Focus())),
                    System.Windows.Threading.DispatcherPriority.Input);

            /// <summary>
            /// Go to the box that makes a discipline, and put the caret in it.
            ///
            /// Every way of adding one arrives here, so there is one shape to the act wherever it
            /// is started from: the strip over the grid, and the box itself. A discipline gets its
            /// name as it is made rather than being made called "New category" and left to be
            /// found again.
            /// </summary>
            private void Adding()
            {
                _scroll.ScrollToBottom();
                Naming();
            }

            /// <summary>
            /// Adds a column and selects it.
            ///
            /// On a mirrored board rows and columns are one list, so this adds a discipline and
            /// behaves identically to the other ways of adding one.
            ///
            /// Where the axes are independent it adds a column. It previously added a row in that
            /// case: the method was an alias written before independent axes existed, and its only
            /// caller was a menu on a mirrored board.
            /// </summary>
            private void AddColumn()
            {
                if (_board.Paired) { AddRow(); return; }

                Remember(what: "adding a column");

                var made = _board.AddColumn(new MatrixLine(Fresh, Next(_board.Columns)));

                Seed();
                Redraw();
                Pick(made, rows: false);
            }

            /// <summary>What a category is called before anybody has said what it is.</summary>
            private const string Fresh = "New category";

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
            /// <summary>
            /// What can be done to several selected lines at once.
            ///
            /// Only actions that mean the same thing applied to each. Name, number and code are
            /// per-line and are excluded. Filing, filling and removing are included.
            /// </summary>
            /// <param name="many">The selected lines, in axis order.</param>
            /// <param name="rows">True when they are rows.</param>
            private List<Parts.Entry> ManyMenu(IReadOnlyList<MatrixLine> many, bool rows)
            {
                var what = rows ? "row" : "column";
                var folders = _board.Folders(rows);

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading(Ui.Count(many.Count, what) + " picked"),
                };

                // Through the same gutter act, so filling eight rows from the menu and filling
                // them from the mark beside them are one thing - and so it lays down whatever
                // the grid is currently showing.
                if (_shown == Showing.Priority)
                    items.Add(Parts.Entry.Saying(
                        _rank == MatrixBoard.NotSet
                            ? "Clear their priorities"
                            : "Rank them " + MatrixBoard.RankNamed(_rank).ToLowerInvariant(),
                        Glyphs.Warn,
                        "Every checked cell on them, whatever it says now",
                        () => Sweep(many[0], rows)));
                else if (_shown == Showing.Symbols && _brush != null)
                    items.Add(Parts.Entry.Saying(
                        Erasing ? "Clear their undecided cells" : "Fill with " + Quoted(_brush.Symbol),
                        Glyphs.Sweep,
                        Erasing
                            ? "Takes the decisions off every cell on them"
                            : "Puts it in every cell on them that is still undecided",
                        () => Sweep(many[0], rows)));

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Folder"));

                foreach (var folder in folders)
                {
                    var into = folder;

                    items.Add(Parts.Entry.Act("Put in " + into, Glyphs.Layers, () =>
                    {
                        Remember(what: "filing " + Ui.Count(many.Count, what));

                        foreach (var one in many) _board.File(one, into);

                        Redraw();
                    }));
                }

                items.Add(Parts.Entry.Act("Put in a new folder", Glyphs.Add,
                    () => Parts.Ask(_grid, "What is the folder called?", string.Empty, "File",
                        typed =>
                        {
                            if (string.IsNullOrWhiteSpace(typed)) return;

                            Remember(what: "filing " + Ui.Count(many.Count, what));

                            foreach (var one in many) _board.File(one, typed.Trim());

                            Redraw();
                        })));

                if (many.Any(l => l.Folder.Length > 0))
                    items.Add(Parts.Entry.Act("Take out of their folders", Glyphs.Up, () =>
                    {
                        Remember(what: "taking " + Ui.Count(many.Count, what) + " out of a folder");

                        foreach (var one in many) _board.File(one, null);

                        Redraw();
                    }));

                items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Danger("Remove " + Ui.Count(many.Count, "discipline"),
                    Glyphs.Bin,
                    () => Parts.Sure(_grid,
                        "Remove " + Ui.Count(many.Count, "discipline")
                        + " and every decision on them, on both axes?",
                        "Remove", () =>
                        {
                            Remember(what: "removing " + Ui.Count(many.Count, "discipline"));

                            foreach (var one in many) _board.RemovePair(one);

                            Pick(null, rows);
                            Redraw();
                        })));

                return items;
            }

            private List<Parts.Entry> LineMenu(MatrixLine line, int at, bool rows)
            {
                var lines = rows ? _board.Rows : _board.Columns;
                var what = rows ? "row" : "column";

                // Name, number and code are not on this menu. They were modal dialogues doing
                // what the inspector does in place, and behaved differently from it, so the same
                // edit had two outcomes depending on the route taken.
                //
                // Menu order is consistent across this screen: what it is, what it points at,
                // where it is filed, where it sits, then the destructive action last, behind a
                // rule, in the danger colour.
                //
                // A menu opened on one of several selected lines acts on all of them, matching
                // the cells' behaviour.
                var many = Chosen(line, rows) && _chosen.Count > 1
                    ? _chosen.Where(l => !Hidden(l)).ToList()
                    : new List<MatrixLine>();

                if (many.Count > 1) return ManyMenu(many, rows);

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading(line.Titled),

                    Parts.Entry.Saying("Edit details", Glyphs.Pencil,
                        "Name, number and code, in the panel", () => Pick(line, rows)),

                    Parts.Entry.Saying("Rename", Glyphs.Pencil,
                        "The same as double-clicking its header",
                        () => Parts.Ask(_grid, rows ? "Name this row" : "Name this column",
                                        line.Name, "Rename", typed =>
                        {
                            if (string.IsNullOrWhiteSpace(typed)) return;

                            Remember(what: "renaming a " + what);

                            _board.Rename(line, typed.Trim());

                            Redraw();
                            Pick(line, rows);
                        })),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Source: " + line.Source.Kinded()),
                };

                items.AddRange(SourceMenu(line));

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading(line.Folder.Length == 0
                    ? "Not in a folder"
                    : "Folder: " + line.Folder));

                items.AddRange(FilingMenu(line));

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Position"));

                if (at > 0)
                    items.Add(Parts.Entry.Act("Move up", Glyphs.Up, () =>
                    {
                        Remember(what: "moving that " + (rows ? "row" : "column"));

                        _board.MoveLine(line, at - 1);
                        Redraw();
                    }));

                if (at < lines.Count - 1)
                    items.Add(Parts.Entry.Act("Move down", Glyphs.Down, () =>
                    {
                        Remember(what: "moving that " + (rows ? "row" : "column"));

                        _board.MoveLine(line, at + 1);
                        Redraw();
                    }));

                items.Add(Parts.Entry.Act(rows ? "Insert a row below" : "Insert a column after",
                    Glyphs.Add, () =>
                    {
                        if (rows) AddRow(); else AddColumn();
                    }));

                items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Danger("Delete", Glyphs.Bin, () =>
                {
                    Remember(what: "taking out that " + what);

                    // Both axes: half a category left behind is a line naming a discipline
                    // the matrix no longer has, and every cell on it is dead.
                    _board.RemovePair(line);
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
                            sets.Select(s => new KeyValuePair<string, int>(Labelled(s), s.Members))
                                .ToList(),
                            line.Source.IsSet
                                ? sets.Where(s => string.Equals(
                                          s.Trail,
                                          line.Source.Folder.Length == 0
                                              ? line.Source.Name
                                              : line.Source.Folder + "/" + line.Source.Name,
                                          StringComparison.OrdinalIgnoreCase))
                                      .Select(Labelled).ToArray()
                                : null,
                            null,
                            picked =>
                            {
                                var trail = picked.FirstOrDefault();

                                if (trail == null) return;

                                var found = sets.FirstOrDefault(s => string.Equals(
                                    Labelled(s), trail, StringComparison.OrdinalIgnoreCase));

                                if (found == null) return;

                                Remember(what: "pointing " + Quoted(line.Titled) + " at another set");

                                Pointed(line, found.IsSearch
                                    ? MatrixSource.SearchSet(found.Name, found.Folder)
                                    : MatrixSource.SelectionSet(found.Name, found.Folder));
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

                                Remember(what: "pointing " + Quoted(line.Titled) + " at models");

                                Pointed(line, MatrixSource.Models(picked.ToList()));
                            })));

                items.Add(Parts.Entry.Saying("What is selected now", Glyphs.Focus,
                    "Captured as a list of elements, so nothing modelled later joins it",
                    () => Capture(line, MatrixSourceKind.CurrentSelection)));

                items.Add(Parts.Entry.Saying("A pick from the selection tree", Glyphs.Layers,
                    "Select the branches in the Navisworks tree, then choose this",
                    () => Capture(line, MatrixSourceKind.TreePick)));

                // The one source on this screen that costs a pass of the model, which is why it
                // says so and runs under a job with a Stop. Everything else here is the document
                // answering about itself.
                items.Add(Parts.Entry.Saying("What is visible now", Glyphs.Eye,
                    "Walks the model once to find what is not hidden, then holds that list",
                    () => CaptureVisible(line)));

                if (!line.Source.IsEmpty)
                    items.Add(Parts.Entry.Act("Clear its source", Glyphs.Close, () =>
                    {
                        Remember(what: "pointing " + Quoted(line.Titled) + " at nothing");
                        Pointed(line, MatrixSource.Nothing);
                    }));

                return items;
            }

            /// <summary>Hold whatever is selected in the host right now.</summary>
            private void Capture(MatrixLine line, MatrixSourceKind kind)
            {
                try
                {
                    var keys = _session.Selected();

                    if (keys.Count == 0)
                    {
                        Warned(Ui.Problem("Nothing is selected. Select it in Navisworks first, "
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

                Ui.Working("Reading what is visible…", job =>
                {
                    var seen = 0;

                    foreach (var key in _session.Visible())
                    {
                        if (!job.Step(++seen, "elements read")) return;

                        keys.Add(key);
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
                Pointed(line, MatrixSource.Captured(kind, keys));

                Did(Ui.Done(line.Name + " now holds " + Ui.Count(keys.Count, "element")
                             + ", as they are today. Anything modelled later is not in it."));
            }

            /// <summary>
            /// Points a line at a source, and points the line standing for it on the other axis at
            /// the same source.
            ///
            /// Setting only one side left the other pointing at nothing, so the pair built no test
            /// and the two stopped mirroring each other, turning the triangle into a square whose
            /// dead half showed decisions that had not been made. A column deliberately pointed
            /// elsewhere is left alone.
            /// </summary>
            /// <param name="line">The row or column.</param>
            /// <param name="source">Where its elements come from.</param>
            private void Pointed(MatrixLine line, MatrixSource source)
            {
                var twin = _board.Point(line, source);

                Redraw();

                if (twin != null)
                    Said(Ui.Note("The column for " + Quoted(line.Name) + " now points at the same "
                                 + "thing, so the pairs on it can be built."), hold: true);
            }

            /// <summary>What goes in one cell.</summary>
            /// <summary>
            /// The cells a menu action applies to: the marked block, or the cell under the
            /// pointer.
            ///
            /// With a block marked, the menu previously wrote only the cell under the pointer and
            /// left the rest unchanged with the block still drawn around them. The keyboard has
            /// always written the whole block.
            ///
            /// Applies only when the press is inside the block; a right-click elsewhere is about
            /// that cell.
            /// </summary>
            /// <param name="row">The row pressed on.</param>
            /// <param name="column">The column pressed on.</param>
            private List<(MatrixLine Row, MatrixLine Column)> Reached(MatrixLine row,
                                                                     MatrixLine column)
            {
                if (Blocked)
                {
                    var held = Block().ToList();

                    if (held.Any(c => ReferenceEquals(c.Row, row)
                                      && ReferenceEquals(c.Column, column)))
                        return held;
                }

                return new List<(MatrixLine, MatrixLine)> { (row, column) };
            }

            /// <summary>Put one symbol, or nothing, in every cell the menu reaches.</summary>
            /// <param name="cells">The cells.</param>
            /// <param name="symbol">The symbol, or null to clear them.</param>
            /// <param name="what">What to call the act in the undo history.</param>
            private void Laid(IReadOnlyList<(MatrixLine Row, MatrixLine Column)> cells,
                              string? symbol, string what)
            {
                Remember(what: what);

                foreach (var (row, column) in cells) _board.Set(row, column, symbol);

                // The mirror of a cell inside the block can sit outside it, so anything larger
                // than one cell is drawn again rather than repainted pair by pair.
                if (cells.Count > 1) RepaintAll();
                else Repaint(cells[0].Row, cells[0].Column);

                Recount();
            }

            /// <summary>
            /// Everything a folder can have done to it, from the band that draws it.
            ///
            /// Dissolve and delete are worded to be clearly distinct. A folder holds no source,
            /// symbol or decision, so removing the folder costs nothing; removing its disciplines
            /// costs every decision on those rows and columns.
            /// </summary>
            /// <param name="folder">The folder.</param>
            private List<Parts.Entry> FolderMenu(string folder)
            {
                var held = _board.InFolder(rows: true, folder).Count;
                var shut = _shut.Contains(folder);
                var others = _board.Folders(rows: true)
                                   .Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))
                                   .ToList();

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading(folder + "  ·  " + Ui.Count(held, "discipline")),

                    Parts.Entry.Act(shut ? "Expand" : "Collapse",
                                    shut ? Glyphs.Down : Glyphs.Up, () => Shut(folder)),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Folder"),

                    Parts.Entry.Act("Colour", Glyphs.Palette, () =>
                    {
                        var held = new Parts.Box { Colour = _board.ColourOf(folder) };

                        Parts.Pick(_grid, "What colour is " + folder + "?", held,
                            MatrixLook.Colours(held.Colour, colour => held.Colour = colour),
                            colour =>
                            {
                                Remember(what: "colouring a folder");

                                _board.Paint(folder, colour);
                                Redraw();
                            });
                    }),

                    Parts.Entry.Act("Rename", Glyphs.Pencil,
                        () => Parts.Ask(_grid, "What is this folder called?", folder, "Rename",
                            typed =>
                            {
                                if (string.IsNullOrWhiteSpace(typed)) return;

                                Remember(what: "renaming a folder");

                                _shut.Remove(folder);
                                _board.RenameFolder(folder, typed.Trim());
                                Redraw();
                            })),
                };

                if (others.Count > 0)
                    items.Add(Parts.Entry.Act("Merge folders  ·  "
                                              + Ui.Count(others.Count, "other"), Glyphs.Layers,
                        () => Parts.Search(_grid, "Which folders go into " + folder + "?",
                            others.Select(f => new KeyValuePair<string, int>(
                                              f, _board.InFolder(rows: true, f).Count)).ToList(),
                            null, null,
                            picked =>
                            {
                                if (picked.Count == 0) return;

                                Remember(what: "merging folders");

                                foreach (var gone in picked) _shut.Remove(gone);

                                var moved = _board.MergeFolders(picked.Concat(new[] { folder }),
                                                                folder);

                                Redraw();

                                Did(Ui.Done(Ui.Count(picked.Count, "folder") + " merged into "
                                             + Quoted(folder) + ", "
                                             + Ui.Count(moved, "line") + " in all."));
                            })));

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Remove"));

                items.Add(Parts.Entry.Act("Ungroup", Glyphs.Eraser,
                    () =>
                    {
                        Remember(what: "dissolving a folder");

                        _shut.Remove(folder);
                        _board.DissolveFolder(folder);
                        Redraw();

                        Did(Ui.Done(Quoted(folder) + " is gone. Its "
                                     + Ui.Count(held, "discipline") + " stayed."));
                    }));

                items.Add(Parts.Entry.Danger("Delete folder and disciplines", Glyphs.Bin,
                    () => Parts.Sure(_grid,
                        "This takes " + Ui.Count(held, "discipline") + " off the matrix, both "
                        + "axes, and every decision on them with it. Nothing in the document "
                        + "changes - the tests already built stay as they are.",
                        "Delete", () =>
                        {
                            Remember(what: "deleting a folder and its disciplines");

                            _shut.Remove(folder);

                            var went = _board.RemoveFolder(folder);

                            Seed();
                            Redraw();

                            Did(Ui.Done(Ui.Count(went, "discipline") + " taken out. "
                                         + "Undo puts them back."));
                        })));

                return items;
            }

            /// <summary>
            /// Put one line in a folder, offering the ones that exist and the making of a new one.
            /// </summary>
            /// <param name="line">The row or column.</param>
            private List<Parts.Entry> FilingMenu(MatrixLine line)
            {
                var items = new List<Parts.Entry>();

                foreach (var folder in _board.Folders(rows: true))
                {
                    var one = folder;

                    if (string.Equals(one, line.Folder, StringComparison.OrdinalIgnoreCase)) continue;

                    items.Add(Parts.Entry.Act(one, Glyphs.Folder, () =>
                    {
                        Remember(what: "filing " + Quoted(line.Titled) + " in " + Quoted(one));

                        _board.File(line, one);
                        Redraw();
                    }));
                }

                if (items.Count > 0) items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Act("New folder…", Glyphs.Add,
                    () => Parts.Ask(_grid, "What is the new folder called?", string.Empty,
                                    "Create",
                        typed =>
                        {
                            if (string.IsNullOrWhiteSpace(typed)) return;

                            Remember(what: "filing " + Quoted(line.Titled) + " in a new folder");

                            _board.File(line, typed.Trim());
                            Redraw();
                        })));

                if (line.Folder.Length > 0)
                    items.Add(Parts.Entry.Act("Take it out of " + Quoted(line.Folder),
                                              Glyphs.Close, () =>
                    {
                        Remember(what: "taking " + Quoted(line.Titled) + " out of its folder");

                        _board.File(line, null);
                        Redraw();
                    }));

                return items;
            }

            private List<Parts.Entry> CellMenu(MatrixLine row, MatrixLine column)
            {
                var here = _board.At(row, column);
                var reach = Reached(row, column);
                var many = reach.Count > 1;

                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading(many
                        ? Ui.Count(reach.Count, "cell") + " selected"
                        : Where(row, column) + "  ·  " + row.Titled + " × " + column.Titled),
                };

                if (_legend.IsEmpty)
                    items.Add(Parts.Entry.Saying("The legend is empty", Glyphs.Add,
                        "A cell carries a symbol, and a symbol is a setting. Make one first.",
                        AddSymbol));
                else
                    items.Add(Parts.Entry.Heading(many ? "Set the symbol" : "Set the symbol"));

                foreach (var symbol in _legend.Symbols)
                {
                    var one = symbol;

                    items.Add(Parts.Entry.Saying(
                        one.Symbol + (string.Equals(one.Symbol, here, StringComparison.OrdinalIgnoreCase)
                            ? "  ·  in this cell now" : string.Empty),
                        Glyphs.Tick,
                        MatrixNaming.Worded(one.Test) + " at " + Millimetres(one.Tolerance)
                        + (one.Enabled ? string.Empty : ", turned off"),
                        () => Laid(reach, one.Symbol,
                                   "putting " + Quoted(one.Symbol)
                                   + (many ? " in " + Ui.Count(reach.Count, "cell")
                                           : " in a cell"))));
                }

                items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Saying("Not required", Glyphs.Close,
                    "Agreed, and no test is made. Distinct from an undecided cell.",
                    () => Laid(reach, MatrixBoard.NotRequired,
                               many ? "ruling out " + Ui.Count(reach.Count, "pair")
                                    : "ruling out a pair")));

                if (many || here != null)
                    items.Add(Parts.Entry.Act("Clear", Glyphs.Close,
                        () => Laid(reach, null,
                                   many ? "clearing " + Ui.Count(reach.Count, "cell")
                                        : "clearing a cell")));

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Clipboard"));

                items.Add(Parts.Entry.Saying(
                    many ? "Copy these " + Ui.Count(reach.Count, "cell") : "Copy",
                    Glyphs.Copy, "Ctrl+C. Paste it anywhere on the grid, or into a spreadsheet.",
                    () => Copy()));

                items.Add(Parts.Entry.Saying("Paste", Glyphs.Changes,
                    "Ctrl+V. What was copied goes down with this cell as its top left.",
                    Paste));

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Whole grid"));

                items.Add(Parts.Entry.Danger("Fill every cell", Glyphs.SelectAll, () =>
                {
                    if (here == null) return;

                    // The one bulk act that asks first. It overwrites every decision on the
                    // matrix at once, which is not a mistake anybody makes twice, and the count
                    // afterwards would be a count of everything they had done all afternoon.
                    Parts.Sure(_grid,
                        "This puts " + Quoted(here) + " in every cell of the matrix, over "
                        + "everything already decided. The diagonal is left alone: a discipline "
                        + "against itself is set deliberately, not by a fill.",
                        "Fill", () =>
                        {
                            Remember(what: "filling the whole grid with " + Quoted(here));
                            _board.Fill(here);
                            RepaintAll();

                            Did(Ui.Done("The grid is filled with " + Quoted(here)
                                         + ". Undo takes it back."));
                        });
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
            /// <summary>
            /// How much of the board has nothing left open on it.
            ///
            /// Of the pairs a test actually covers, because a pair nobody has run is not a pair
            /// somebody has finished with - and a bar that counted them as either would say the
            /// job was further along or further behind than it is.
            /// </summary>
            private void CountProgress()
            {
                var covered = 0;
                var clear = 0;

                foreach (var row in _board.Rows)
                foreach (var column in _board.Columns)
                {
                    if (!_board.IsKeptHalf(row, column)) continue;
                    if (_board.IsSelfPair(row, column)) continue;

                    var open = _counts.OpenBetween(row, column);

                    if (open < 0) continue;

                    covered++;

                    if (open == 0) clear++;
                }

                _progress.Children.Add(Ui.Eyebrow("Cleared"));

                var track = new Border
                {
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 5, 0, 4),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                track.Tint(Border.BackgroundProperty, "Nav");

                var run = new Grid();

                run.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(covered == 0 ? 0 : clear, GridUnitType.Star),
                });

                run.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(covered == 0 ? 1 : Math.Max(0, covered - clear),
                                           GridUnitType.Star),
                });

                var filled = new Border();

                filled.Tint(Border.BackgroundProperty, "Accent");

                Grid.SetColumn(filled, 0);
                run.Children.Add(filled);

                track.Child = run;

                _progress.Children.Add(track);

                _progress.Children.Add(Ui.Small(
                    clear.ToString(CultureInfo.InvariantCulture) + " of "
                    + covered.ToString(CultureInfo.InvariantCulture)
                    + " tested pairs have nothing open"));

                _progress.Children.Add(Ui.Small(
                    Ui.Count(_counts.Open, "clash") + " open across "
                    + Ui.Count(_counts.Tests, "test")));
            }

            /// <summary>
            /// How much of the priority picture is filled in: ranked, of the pairs that could be.
            ///
            /// Of the checked pairs, not of every pair, because an undecided cell has no test to
            /// be urgent about - a bar counting those could never reach the end on a matrix that
            /// deliberately leaves forty per cent of itself blank.
            /// </summary>
            private void RankProgress()
            {
                var live = 0;

                foreach (var row in _board.Rows)
                foreach (var column in _board.Columns)
                {
                    if (!_board.IsKeptHalf(row, column)) continue;
                    if (_board.IsSelfPair(row, column)) continue;

                    var held = _board.At(row, column);

                    if (held == null || MatrixBoard.IsNotRequired(held)) continue;

                    live++;
                }

                var done = _board.Ranks().Count(one => !_board.IsSelfPair(one.Row, one.Column));

                _progress.Children.Add(Ui.Eyebrow("Ranked"));

                var track = new Border
                {
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 5, 0, 4),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                track.Tint(Border.BackgroundProperty, "Nav");

                var run = new Grid();

                run.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(live == 0 ? 0 : done, GridUnitType.Star),
                });

                run.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(live == 0 ? 1 : Math.Max(0, live - done), GridUnitType.Star),
                });

                var filled = new Border();

                filled.Tint(Border.BackgroundProperty, "Accent");

                Grid.SetColumn(filled, 0);
                run.Children.Add(filled);

                track.Child = run;

                _progress.Children.Add(track);

                _progress.Children.Add(Ui.Small(
                    done.ToString(CultureInfo.InvariantCulture) + " of "
                    + live.ToString(CultureInfo.InvariantCulture) + " checked pairs ranked"));
            }

            private void RedrawPalette()
            {
                _palette.Children.Clear();

                // Upper-cased here, because Ui.Eyebrow does it to the text it is handed and
                // this writes over that text - which is how the one eyebrow on the screen ended
                // up in sentence case beside DECIDED and RANKED.
                _paletteTitle.Text = _shown == Showing.Priority ? "PRIORITY"
                    : _shown == Showing.Results ? "WHAT CAME BACK"
                    : "PALETTE";

                // The three things a legend button does - add a symbol, change one, give the
                // unmeant ones meanings - are all about the symbols, so they go while the
                // symbols are not what is being laid down.
                if (_legendActs != null)
                    _legendActs.Visibility = _shown == Showing.Symbols
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                if (_shown == Showing.Priority)
                {
                    RankKeys();
                    return;
                }

                if (_shown == Showing.Results)
                {
                    CountKeys();
                    return;
                }

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
                        () => Arm(one),
                        hue: at,
                        paint: Washed(one)));
                }

                // The two marks that are not symbols: one rules a pair out on purpose, the
                // other empties the cell back to undecided. Both below the symbols, because they
                // are not part of the key a client reads off the grid.
                _palette.Children.Add(Swatch(MatrixBoard.NotRequired, "Not required",
                    "agreed, and no test is made", "-",
                    _brush != null && MatrixBoard.IsNotRequired(_brush.Symbol),
                    false, () => Arm(Ruling())));

                _palette.Children.Add(Swatch(Rubber, "Not decided", "empties the cell", "Del",
                                          Erasing, false, () => Arm(Rubbing())));

            }

            /// <summary>
            /// The four ranks, as the four keys a press in the priority view lays down.
            ///
            /// Drawn by the same swatch the symbols are, in the colours the cells carry, because
            /// a key that is not drawn the way the thing it explains is drawn is not a key - and
            /// this list is doing both jobs at once: it is the legend for the picture, and it is
            /// what arms the hand.
            /// </summary>
            private void RankKeys()
            {
                var ranks = new[]
                {
                    (Rank: MatrixBoard.High, Says: "most urgent", Stroke: "1", Mark: "H"),
                    (Rank: MatrixBoard.Medium, Says: "in the middle", Stroke: "2", Mark: "M"),
                    (Rank: MatrixBoard.Low, Says: "when there is time", Stroke: "3", Mark: "L"),
                };

                foreach (var one in ranks)
                {
                    var wanted = one.Rank;

                    _palette.Children.Add(Swatch(one.Mark, MatrixBoard.RankNamed(one.Rank),
                                                 one.Says, one.Stroke, _rank == wanted, true,
                                                 () => Arming(wanted),
                                                 paint: Ranked(one.Rank)));
                }

                // Below the three, the way the eraser sits below the symbols: it is not a rank,
                // it takes one off.
                _palette.Children.Add(Swatch("\u00d7", "Not set", "takes the rank off", "0",
                                             _rank == MatrixBoard.NotSet, false,
                                             () => Arming(MatrixBoard.NotSet)));

                var says = Ui.Small("A rank goes on a pair that is checked. Cells with nothing "
                                    + "in them have no test to be urgent about.");

                says.TextWrapping = TextWrapping.Wrap;
                says.Margin = new Thickness(4, 6, 4, 4);

                _palette.Children.Add(says);
            }

            /// <summary>
            /// The key for the numbers and washes in the results view.
            ///
            /// A key rather than a palette: in this view nothing is painted, so the symbol legend
            /// armed brushes the view cannot use. What the key states is what a heat map cannot:
            /// where the scale runs from and to, and that a blank cell is not a zero.
            /// </summary>
            private void CountKeys()
            {
                var worst = Math.Max(1, _counts.Worst);

                Keyed(Skin.Of("OkBg"), "0", "Nothing open",
                      "tested, and everything on it is decided");

                Keyed(Heat((worst + 1) / 2), "\u2026", "Some open",
                      "the wash runs to the worst on this board");

                Keyed(Heat(worst), worst.ToString(CultureInfo.InvariantCulture), "The worst pair",
                      "which is where a meeting goes first");

                Keyed(System.Windows.Media.Brushes.Transparent, string.Empty, "No test covers it",
                      "a hole in the coordination, not a clean pair");

                var says = Ui.Small("Open means active and new; a decided clash is not counted.");

                says.TextWrapping = TextWrapping.Wrap;
                says.Margin = new Thickness(4, 6, 4, 4);

                _palette.Children.Add(says);
            }

            /// <summary>
            /// One line of a key: a swatch, what it is, and what it means.
            ///
            /// Shaped like the palette's own keys so the panel reads as one thing, and without
            /// their cursor or their press, because there is nothing here to arm.
            /// </summary>
            /// <param name="paint">The wash.</param>
            /// <param name="mark">What is written in it.</param>
            /// <param name="name">What it is.</param>
            /// <param name="says">The quieter line under it.</param>
            private void Keyed(System.Windows.Media.Brush paint, string mark, string name,
                               string says)
            {
                var line = new DockPanel { LastChildFill = true, Margin = new Thickness(5, 3, 6, 3) };

                var letter = new TextBlock
                {
                    Text = mark,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Ui.InkOn(paint),
                };

                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    ClipToBounds = true,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = paint,
                    Child = letter,
                };

                swatch.Tint(Border.BorderBrushProperty, "Bd");

                DockPanel.SetDock(swatch, Dock.Left);
                line.Children.Add(swatch);

                var words = Ui.Stack();

                words.VerticalAlignment = VerticalAlignment.Center;

                words.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }.Wearing("Body"));

                var under = Ui.Small(says);

                under.TextWrapping = TextWrapping.Wrap;
                words.Children.Add(under);

                line.Children.Add(words);

                _palette.Children.Add(line);
            }

            /// <summary>Put a rank on the hand.</summary>
            /// <param name="rank">The rank, or <see cref="MatrixBoard.NotSet"/> to take one off.</param>
            private void Arming(int rank)
            {
                _rank = rank;

                RedrawStrip();
                RedrawPalette();
                RedrawView();
                Pointing();
            }

            /// <summary>One key on the palette, or one choice in the inspector.</summary>
            /// <param name="mark">The symbol, or the cross that clears.</param>
            /// <param name="kind">What kind of test it is.</param>
            /// <param name="says">The tolerance, or what it does.</param>
            /// <param name="stroke">The key on the keyboard that does the same thing.</param>
            /// <param name="armed">True while it is the one on the brush.</param>
            /// <param name="live">False for a symbol that is turned off, and for the cross.</param>
            /// <param name="onPress">Arms it.</param>
            /// <param name="hue">Where it sits in the legend, or -1 for the marks that are not in it.</param>
            /// <param name="paint">The wash its cells carry, or null to take it from the hue.</param>
            private UIElement Swatch(string mark, string kind, string says, string stroke,
                                     bool armed, bool live, Action onPress, int hue = -1,
                                     System.Windows.Media.Brush? paint = null)
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
                        : "Apply " + Quoted(mark) + ", or type " + stroke
                          + " with the cursor in the grid.",
                };

                key.Tint(Border.BorderBrushProperty, "Accent");

                if (armed) key.Tint(Border.BackgroundProperty, "Card");

                var line = new DockPanel { LastChildFill = true };

                // The ink is taken from the fill beneath. There is no implicit TextBlock style
                // in the sheet, so text that requests no brush is drawn in default black, which
                // is legible in the light palette by coincidence and about 1.5:1 on the dark
                // washes.
                var letter = new TextBlock
                {
                    Text = mark,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body");

                var swatch = new Border
                {
                    Width = 24,
                    Height = 24,
                    ClipToBounds = true,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = letter,
                };

                // The key carries the same wash the cells do, because a key that is not drawn
                // the way the thing it explains is drawn is not a key.
                if (live && paint != null) swatch.Background = paint;
                else if (live && hue >= 0) swatch.Background = Skin.Symbol(hue);
                else swatch.Tint(Border.BackgroundProperty, live ? "AccentWash" : "Nav");

                if (live) letter.Foreground = Ui.InkOn(swatch.Background);

                // And a symbol that is turned off is struck through here as well as in the grid,
                // so the palette and the cells agree about what builds nothing.
                if (!live && hue >= 0)
                {
                    letter.TextDecorations = TextDecorations.Strikethrough;
                    letter.Tint(TextBlock.ForegroundProperty, "Muted");
                }

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

                Reachable(key, mark + ", " + kind + ", " + says
                               + (stroke.Length == 0 ? string.Empty : ". Key " + stroke)
                               + (armed ? ". On the brush." : string.Empty),
                          onPress);

                return key;
            }

            /// <summary>
            /// The reserved mark as something the brush can carry.
            ///
            /// A symbol object so the brush, the drag and the sweep all work on it unchanged -
            /// but never in the legend, so nothing can give it a tolerance or switch it on.
            /// </summary>
            private static MatrixSymbol Ruling() =>
                new MatrixSymbol(MatrixBoard.NotRequired, MatrixTest.Hard, 0, false);

            /// <summary>The mark the palette shows for taking a decision back off a cell.</summary>
            private const string Rubber = "\u00d7";

            /// <summary>
            /// The eraser, as a symbol the brush can carry.
            ///
            /// "Not decided" previously disarmed the brush rather than arming an eraser, so
            /// pressing it made every subsequent click do nothing and emptying a cell was
            /// reachable only from the Delete key.
            ///
            /// The brush carries a symbol or nothing, and nothing already means "no brush", so
            /// the eraser needs a mark of its own, as the ruled-out pair does. It writes an empty
            /// cell through the same path every other stroke takes.
            /// </summary>
            private static MatrixSymbol Rubbing() =>
                new MatrixSymbol(Rubber, MatrixTest.Hard, 0, false);

            /// <summary>Whether what is on the brush takes a decision away rather than making one.</summary>
            private bool Erasing =>
                _brush != null && string.Equals(_brush.Symbol, Rubber, StringComparison.Ordinal);

            /// <summary>What the brush puts in a cell: its mark, or nothing at all.</summary>
            private string? Laying => _brush == null || Erasing ? null : _brush.Symbol;

            /// <summary>What is on the brush, as a person would say it.</summary>
            private string Brushed =>
                _brush == null ? "No symbol" : Erasing ? "The eraser" : Quoted(_brush.Symbol);

            /// <summary>Put one symbol in the inspector, or clear it out.</summary>
            /// <param name="symbol">The symbol, or null.</param>
            private void Show(MatrixSymbol? symbol)
            {
                _pickedSymbol = symbol;

                if (symbol != null) _picked = null;

                Lighting(symbol == null || MatrixBoard.IsNotRequired(symbol.Symbol)
                    ? null
                    : symbol.Symbol);

                RedrawInspector();
                Editable();
            }

            /// <summary>Light every cell carrying one symbol, and drop the rest back.</summary>
            /// <param name="symbol">The symbol, or null to put them all back to full strength.</param>
            private void Lighting(string? symbol)
            {
                if (string.Equals(_lit, symbol, StringComparison.Ordinal)) return;

                _lit = symbol;

                RepaintAll();
                RedrawStrip();
            }

            // ---------------------------------------------------------------------------
            // The strip, the inspector, the keyboard and taking it back
            // ---------------------------------------------------------------------------

            /// <summary>What is armed, where the cursor is, how big it is, and undo.</summary>
            private void RedrawStrip()
            {
                _strip.Children.Clear();
                _verbs.Children.Clear();

                if (_board.Rows.Count == 0 && _board.Columns.Count == 0) return;

                void Into(Panel panel, UIElement said, double left)
                {
                    // One size for the whole row. A caption at the button size beside a fact at
                    // the hint size are two baselines however carefully they are centred.
                    if (said is Control control) control.FontSize = StripSize;
                    else if (said is TextBlock words) words.FontSize = StripSize;

                    if (said is FrameworkElement held)
                    {
                        held.VerticalAlignment = VerticalAlignment.Center;
                        held.Margin = new Thickness(left, 0, 0, 0);
                    }

                    panel.Children.Add(said);
                }

                // What is going on, on the left, where a sentence is read from.
                void Say(UIElement said, double left = StripGap) =>
                    Into(_strip, said, _strip.Children.Count == 0 ? 0 : left);

                // What can be done, against the right-hand edge, in the same place every time.
                void Does(UIElement said, double left = StripGap) =>
                    Into(_verbs, said, _verbs.Children.Count == 0 ? 0 : left);

                // The strip carries state that has a control attached to it, not commentary. It
                // previously narrated the brush, the inspector's subject and the cursor position
                // on every click, all of which are already visible elsewhere.
                //
                // The seat's standing notice lives here rather than in a band across the top,
                // which pushed the matrix down by its own height on every open, or floating,
                // which covered the content beneath. Six words here with the full sentence on the
                // tooltip; a press dismisses it for the life of the application, since the
                // condition is a property of the Navisworks edition.
                if (!_session.Clash.IsAvailable && !_hushed)
                {
                    // Three words and a mark, because the strip is one line. Written out in
                    // full, with a Dismiss control beside it, it pushed "Saving as you go" onto a
                    // second line, which moves the matrix down.
                    var told = new DockPanel
                    {
                        LastChildFill = true,
                        Cursor = Cursors.Hand,
                        Background = System.Windows.Media.Brushes.Transparent,
                        ToolTip = "This Navisworks edition has no clash engine, so the matrix "
                                  + "cannot be run into tests here - but everything else works. "
                                  + "Draw it, save it, send it out as a spreadsheet, and build it "
                                  + "on a seat that has one. "
                                  + (_session.ClashProblem ?? string.Empty).Trim()
                                  + "  Press to put this away.",
                    };

                    var mark = Ui.Glyph(Glyphs.Warn, 12, "AmberTx", new Thickness(0, 0, 5, 0));

                    mark.VerticalAlignment = VerticalAlignment.Center;

                    DockPanel.SetDock(mark, Dock.Left);
                    told.Children.Add(mark);
                    told.Children.Add(new TextBlock
                    {
                        Text = "No clash engine",
                        FontSize = StripSize,
                        VerticalAlignment = VerticalAlignment.Center,
                    }.Wearing("Hint"));

                    told.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                    {
                        e.Handled = true;
                        _hushed = true;
                        RedrawStrip();
                    });

                    Reachable(told, "No clash engine on this seat. Press to put this away.", () =>
                    {
                        _hushed = true;
                        RedrawStrip();
                    });

                    Say(told);
                }

                // What is on the brush belongs on the strip only while the brush is what a
                // press puts down. On the other two views it is a fact about a screen nobody is
                // looking at - and "No symbol selected" under a picture of priorities reads as
                // something being wrong.
                if (_shown == Showing.Symbols && _brush == null)
                {
                    Say(Ui.Small("No symbol selected"));
                }
                else if (_shown == Showing.Symbols && _brush != null)
                {
                    Say(Ui.Glyph(Glyphs.Pencil, 12, "Accent", new Thickness(0, 0, 8, 0)));

                    Say(new TextBlock
                    {
                        Text = Brushed + " selected",
                        FontWeight = FontWeights.SemiBold,
                    }.Wearing("Body"));

                    Say(Ui.Quiet("Clear selection", () => Arm(null)), 8);
                }

                // Of the five modes, the selected line and the highlighted symbol were not
                // stated. The highlight dims most of the grid, which without explanation reads as
                // a fault rather than a search, so it is stated here with the control that ends
                // it.
                //
                // The results view is stated because the cells no longer resemble the symbols
                // they replaced, and the counts need saying: open clashes, not total.
                //
                // No "back" control on the strip: the view chooser above does that, and every
                // line added here pushes the plan count towards an edge that clips. The results
                // view arms nothing, so it states no mode; its counts are in the panel foot.
                if (_shown == Showing.Priority)
                {
                    // The mark and the words in one panel: the strip sets a left margin on
                    // everything it is handed, so a gap asked for on the glyph itself is a gap
                    // that is overwritten - which is why this read "(!)Laying high".
                    var laying = new StackPanel { Orientation = Orientation.Horizontal };

                    laying.Children.Add(Ui.Glyph(Glyphs.Warn, 12, "Accent",
                                                 new Thickness(0, 0, 7, 0)));

                    laying.Children.Add(new TextBlock
                    {
                        Text = _rank == MatrixBoard.NotSet
                            ? "Clearing ranks"
                            : "Laying " + MatrixBoard.RankNamed(_rank).ToLowerInvariant(),
                        FontSize = StripSize,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    }.Wearing("Body"));

                    Say(laying);
                }

                if (_lit != null)
                {
                    Say(Ui.Small("Showing only " + Quoted(_lit)));
                    Say(Ui.Quiet("Show all", () => Lighting(null)), 8);
                }

                // The cursor's own address went with the rest: it is drawn in the cell it is
                // in, and repeating it in words on every arrow press was the noisiest of them.
                // A marked block stays, because it is a mode - what the next key typed will do
                // is different, and nothing else on the screen says so.
                if (Blocked
                    && _atRow >= 0 && _atRow < _board.Rows.Count
                    && _atColumn >= 0 && _atColumn < _board.Columns.Count)
                    Say(Ui.Small("Block " + Where(_board.Rows[Math.Min(_atRow, _markRow)],
                                                  _board.Columns[Math.Min(_atColumn, _markColumn)])
                                 + " to " + Where(_board.Rows[Math.Max(_atRow, _markRow)],
                                                  _board.Columns[Math.Max(_atColumn, _markColumn)])
                                 + " — type a symbol for all of it"));

                // A selection is a mode and is stated here. Eight highlighted rows are visible
                // while on screen and invisible once scrolled past, and the next action applies to
                // all eight.
                if (_chosen.Count > 1)
                {
                    Say(Ui.Small(Ui.Count(_chosen.Count, _chosenRows ? "row" : "column")
                                 + " picked"));

                    Say(Ui.Quiet("Pick one", () => Pick(_picked, _chosenRows)), 8);
                }

                // The tally and whether it is saved are both state, and both belong with the
                // rest of the state - on the left, where a sentence is read from.
                Say(Ui.Loose(_count.Wearing("Hint")));
                Say(Saying());

                // From here on these are controls, and all of them sit on the right.
                //
                // The add-line control is under the last row, which on a matrix of any size is off
                // the bottom of the screen; this is the route to it. There were previously two of
                // these with different behaviour: one scrolled to the name box, the other created
                // a row called "New category" and left it to be found. Both added the same thing.
                Does(Ui.Quiet("Add discipline", Adding));

                // The equivalent link for columns. The plus that adds one sits past the last
                // column, which on a scrollable matrix is off the right edge.
                //
                // The keyboard reference is here because most of it was undocumented: the palette
                // chips teach the digits and the cell page mentions shift with an arrow, but Alt
                // with an arrow existed only in an accessibility name.
                Does(Ui.Quiet("Keys", Keys));

                Does(Ui.Quiet(_sideOpen ? "Hide the options" : "Options", () =>
                {
                    _sideOpen = !_sideOpen;
                    RedrawPanes();
                }));

                var back = Ui.Quiet("Undo", Undo);

                back.IsEnabled = _past.Count > 0;
                back.ToolTip = _past.Count == 0
                    ? "Nothing to undo"
                    : "Take back " + _past.Peek().What;

                Does(back);

                var on = Ui.Quiet("Redo", Redo);

                on.IsEnabled = _future.Count > 0;
                on.ToolTip = _future.Count == 0
                    ? "Nothing to redo"
                    : "Redo " + _future.Peek().What;

                Does(on, 4);
            }

            /// <summary>Everything the keyboard does over the grid, in one place.</summary>
            private void Keys()
            {
                var said = new[]
                {
                    ("Arrows", "Move the cursor one cell"),
                    ("Shift + arrow", "Extend the selection from the cursor"),
                    ("Ctrl+A", "Select the whole grid; Escape deselects"),
                    ("A digit or a symbol's letter", "Enter that symbol and move right"),
                    ("With cells selected", "Enter it in every selected cell"),
                    ("Space", "Enter the selected symbol"),
                    ("Delete, Backspace, 0", "Clear the cell"),
                    ("Minus", "Mark the pair as not required"),
                    ("Home, End", "First and last column of the row"),
                    ("PageUp, PageDown", "Scroll one screen of rows"),
                    ("Ctrl+Home, Ctrl+End", "First and last cell of the grid"),
                    ("Alt + arrow", "Move this row or column, and its pair"),
                    ("Ctrl+C, Ctrl+X", "Copy or cut the selection"),
                    ("Ctrl+V", "Paste from the cursor"),
                    ("Ctrl+Z, Ctrl+Y", "Undo and redo"),
                    ("Return, in the box under the rows", "Add a discipline and keep typing"),
                };

                var rows = Ui.Stack();

                foreach (var (key, does) in said)
                {
                    var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 5) };

                    var chip = new Border
                    {
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 2),
                        Margin = new Thickness(0, 0, 9, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        Child = new TextBlock { Text = key, FontSize = 11 }.Wearing("Body"),
                    };

                    chip.Tint(Border.BorderBrushProperty, "BdStrong");
                    chip.Tint(Border.BackgroundProperty, "Card");

                    DockPanel.SetDock(chip, Dock.Left);
                    line.Children.Add(chip);

                    var what = Ui.Small(does);

                    what.TextWrapping = TextWrapping.Wrap;
                    what.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(what);

                    rows.Children.Add(line);
                }

                Ui.Configure("Keyboard shortcuts", null,
                    new Ui.Page("Keyboard shortcuts", () => Ui.Loose(rows)));
            }

            /// <summary>Show or hide the options panel, and say so on the strip.</summary>
            private void RedrawPanes()
            {
                _sidePane.Visibility = _sideOpen ? Visibility.Visible : Visibility.Collapsed;

                RedrawStrip();
            }

            /// <summary>Whether the matrix is safe, in the fewest words that are true.</summary>
            private UIElement Saying()
            {
                if (_unsaved != null)
                {
                    var bad = Ui.Small("Not saved — " + _unsaved);

                    bad.Tint(TextBlock.ForegroundProperty, "RedTx");

                    return bad;
                }

                if (_saved == null) return Ui.Small("Saving as you go");

                var said = Ui.Small("Saved "
                    + _saved.Value.ToString("HH:mm", CultureInfo.CurrentCulture));

                said.Tint(TextBlock.ForegroundProperty, "Ok");

                return said;
            }

            /// <summary>
            /// The inspector: everything about the last thing pressed, editable in place.
            ///
            /// Replaces four dialogues. Naming, numbering, coding and pointing a row were four
            /// menu items each opening a box; a symbol's kind and tolerance were a dropdown and a
            /// field in a strip at the bottom of the window. All of it is one always-present
            /// panel showing the row, column, symbol or cell last touched.
            /// </summary>
            private void RedrawInspector()
            {
                _inspector.Children.Clear();
                _acts.Children.Clear();

                if (_pickedSymbol != null) SymbolPage(_pickedSymbol);
                else if (_picked != null) LinePage(_picked, _pickedRows);
                else if (_atRow >= 0 && _atRow < _board.Rows.Count
                         && _atColumn >= 0 && _atColumn < _board.Columns.Count)
                    CellPage(_board.Rows[_atRow], _board.Columns[_atColumn]);
                else
                {
                    _inspector.Children.Add(Ui.Eyebrow("Nothing selected"));

                    var idle = Ui.Small("Press a row, a column, a symbol or a cell, and what it "
                                        + "is and what can be done to it are here.");

                    idle.TextWrapping = TextWrapping.Wrap;
                    idle.Margin = new Thickness(0, 4, 0, 0);

                    _inspector.Children.Add(idle);

                    Verbs("Matrix actions",
                        Doable(Glyphs.Next, "Go to the first undecided pair",
                               Undecided));
                }

                // The verb strip is always present rather than collapsing when empty. The panel
                // docks the palette below it, so a strip appearing and disappearing between a
                // cell and a row moved the palette keys 30px each time. The guard remains for the
                // frame before the first draw.
                _actsPane.Visibility = _acts.Children.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;
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

            /// <summary>
            /// One action, drawn as a symbol rather than a labelled button.
            ///
            /// Five sentence-length buttons in a 260px panel wrap onto two lines each and take a
            /// panel row apiece, which pushed the panel's subject off the top. As symbols they are
            /// one row in a fixed position, with the sentence on the tooltip.
            /// </summary>
            /// <param name="glyph">The symbol.</param>
            /// <param name="tip">The sentence it replaces, shown as the tooltip.</param>
            /// <param name="onPress">Performs the action.</param>
            /// <param name="danger">True for destructive actions.</param>
            private static System.Windows.Controls.Button Doable(string glyph, string tip,
                                                                 Action? onPress,
                                                                 bool danger = false) =>
                Ui.Icon(glyph, tip, onPress, danger ? "RedTx" : "Muted");

            /// <summary>
            /// The verbs for whatever the panel is showing, in a row with fixed positions.
            ///
            /// An unavailable verb is drawn in place and greyed rather than omitted. Omitted, the
            /// fill appeared only while a symbol was armed, so arming a brush moved clear,
            /// duplicate and the delete button one position right. The delete button sits behind
            /// a gap of its own.
            ///
            /// The row is labelled with what it acts on; between a hairline and the palette it
            /// otherwise read as belonging to the palette.
            /// </summary>
            /// <param name="label">What these verbs act on.</param>
            /// <param name="buttons">The verbs, with null wherever a gap belongs.</param>
            private void Verbs(string label, params UIElement?[] buttons)
            {
                var said = Ui.Small(label);

                said.Margin = new Thickness(0, 0, 0, 2);
                _acts.Children.Add(said);

                var row = new UIElement[buttons.Length];

                for (var at = 0; at < buttons.Length; at++)
                    row[at] = buttons[at] ?? new Border { Width = 12 };

                _acts.Children.Add(Ui.Across(row));
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

                // A rename repaints the row and column headers only, not the whole screen. A
                // full redraw rebuilt the grid and the panel, including this text box, so the
                // caret was lost after each character. The name appears in exactly those two
                // places; the panel is already correct, since that is where the text is being
                // typed.
                _rename = Ui.Text(line.Name, typed =>
                {
                    Remember("name " + at + rows);

                    _board.Rename(line, typed);

                    RedrawDown();
                    RedrawAcross();
                    Recount();
                }, 100);

                Told("Name", _rename);

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
                    Remember("number " + at + rows);

                    // Through the board, so typing over a counted field takes this line out of
                    // the count rather than being overwritten by the next apply. That is the
                    // gesture: a value typed on a line is a decision about that line.
                    _board.Write(line, MatrixField.NumberKey, typed,
                                 byHand: Counted(MatrixField.NumberKey));

                    RedrawDown();
                    RedrawAcross();
                    Recount();
                }, 40);

                number.Margin = new Thickness(0, 3, 0, 0);
                numbered.Children.Add(number);

                Grid.SetColumn(numbered, 0);
                pair.Children.Add(numbered);

                var coded = Ui.Stack();

                coded.Children.Add(Ui.Small("Code"));

                var code = Ui.Text(line.Code, typed =>
                {
                    Remember("code " + at + rows);

                    _board.Write(line, MatrixField.CodeKey, typed,
                                 byHand: Counted(MatrixField.CodeKey));

                    RedrawDown();
                    RedrawAcross();
                    Recount();
                }, 40);

                code.Margin = new Thickness(0, 3, 0, 0);
                coded.Children.Add(code);

                Grid.SetColumn(coded, 2);
                pair.Children.Add(coded);

                pair.Margin = new Thickness(0, 0, 0, 9);
                _inspector.Children.Add(pair);

                // The project's own fields, in the order the scheme lists them. Without these a
                // custom field could only ever be filled by a rule, so a sub-discipline code that
                // differs line by line — which is what one usually is — had nowhere to be typed.
                foreach (var field in MatrixNaming.Custom(_board.Scheme))
                {
                    var here = field;
                    var held = Ui.Stack();

                    held.Children.Add(Ui.Small(here.Title));

                    var box = Ui.Text(line.Value(here.Key), typed =>
                    {
                        Remember(here.Key + " " + at + rows);

                        _board.Write(line, here.Key, typed, byHand: Counted(here.Key));

                        RedrawDown();
                        RedrawAcross();
                        Recount();
                    }, 120);

                    box.Margin = new Thickness(0, 3, 0, 9);
                    held.Children.Add(box);

                    _inspector.Children.Add(held);
                }

                if (line.IsCustom) _inspector.Children.Add(Released(line, at, rows));

                _inspector.Children.Add(Ui.Small("Source"));

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

                // The button is its own anchor: a menu hung off the panel would open somewhere
                // else on the screen entirely. It can be, now that the primitive admits a
                // handler added after the fact rather than demanding one it then null-checks.
                var elsewhere = Doable(Glyphs.Layers, "Change its source", null);

                elsewhere.Click += (s, e) =>
                    Ui.Safely(() => Parts.PopMenu(elsewhere, SourceMenu(line)));

                System.Windows.Controls.Button? filling = null;

                var live = 0;
                var done = 0;

                // Counts every pair the line takes part in, not only those on the kept half of
                // the diagonal. Counted on the kept half, row 1 reported "of 20" and row 18 "of
                // 3", while the list below both named nineteen disciplines.
                foreach (var other in rows ? _board.Columns : _board.Rows)
                {
                    var row = rows ? line : other;
                    var column = rows ? other : line;
                    var held = _board.At(row, column);

                    if (held == null && _board.IsSelfPair(row, column)) continue;

                    live++;

                    if (held != null) done++;
                }

                var count = Ui.Small(done.ToString(CultureInfo.InvariantCulture) + " of "
                    + live.ToString(CultureInfo.InvariantCulture) + " of this " + what
                    + "'s pairs decided");

                count.Margin = new Thickness(0, 10, 0, 6);
                _inspector.Children.Add(count);

                Against(line, rows);

                filling = Doable(Glyphs.Sweep,
                    _brush == null
                        ? "Fill the rest of this " + what + " — arm a symbol on the palette first"
                        : Erasing
                            ? "Take every decision off this " + what
                            : "Fill the rest of this " + what + " with " + Quoted(_brush.Symbol), () =>
                    {
                        if (_brush == null) return;

                        Remember(what: "filling the rest of that " + what);

                        var laid = 0;

                        foreach (var other in rows ? _board.Columns : _board.Rows)
                        {
                            var row = rows ? line : other;
                            var column = rows ? other : line;

                            if (_board.IsSelfPair(row, column)) continue;

                            if (Erasing ? _board.At(row, column) == null
                                        : _board.At(row, column) != null) continue;

                            Daub(row, column);
                            laid++;
                        }

                        Reported(laid, Quoted(_brush!.Symbol) + " put in",
                                 "empty cell of this " + what);
                    });

                // Its own place in the row whether or not there is anything to fill with, so
                // arming a symbol does not move the four buttons beside it.
                filling.IsEnabled = _brush != null;

                // Drawn in the danger colour: clearing a line removes every decision on it, and
                // as a neutral grey cross it was indistinguishable from duplicate beside it.
                var clearing = Doable(Glyphs.Eraser, "Clear every cell of this " + what, () =>
                {
                    Remember(what: "clearing that " + what);

                    var cleared = 0;

                    foreach (var other in rows ? _board.Columns : _board.Rows)
                    {
                        var row = rows ? line : other;
                        var column = rows ? other : line;

                        if (_board.At(row, column) == null) continue;

                        _board.Set(row, column, null);
                        Repaint(row, column);
                        cleared++;
                    }

                    Reported(cleared, "Cleared", "cell of this " + what);
                }, danger: true);

                // Duplicating a line is offered because much of a matrix is the same package
                // split by level or area, where only one word of the row differs.
                var copying = Doable(Glyphs.Copy,
                    "Duplicate this " + what + ", source and all", () =>
                {
                    Remember(what: "duplicating " + Quoted(line.Titled));

                    var copy = new MatrixLine(line.Name + " (copy)",
                                              Next(rows ? _board.Rows : _board.Columns),
                                              line.Source)
                    {
                        Code = line.Code,
                    };

                    // A duplicated discipline is a discipline: it lands on both axes and moves
                    // on both, next to the one it was copied from. Copying a row into the rows
                    // alone left a matrix one row taller than it was wide.
                    _board.AddPair(copy);
                    _board.MoveLine(copy, at + 1);

                    Redraw();
                    Pick(copy, rows: true);
                });

                var removing = Doable(Glyphs.Bin, "Take this " + what + " out", () =>
                {
                    Remember(what: "taking out " + Quoted(line.Titled));

                    var went = 0;

                    foreach (var other in rows ? _board.Columns : _board.Rows)
                        if (_board.At(rows ? line : other, rows ? other : line) != null) went++;

                    // Both axes: half a category left behind is a line naming a discipline
                    // the matrix no longer has, and every cell on it is dead.
                    _board.RemovePair(line);

                    _picked = null;

                    Redraw();

                    // The one destructive act that said nothing afterwards. Every bulk fill
                    // reports a count and names undo; taking a line out took more than any of
                    // them and went quietly.
                    Did(Ui.Done(Quoted(line.Titled) + " taken out"
                                 + (went == 0
                                     ? ". Nothing was decided on it."
                                     : ", with " + Ui.Count(went, "decision") + " on it.")
                                 + " Undo takes it back."));
                }, danger: true);

                Verbs("Actions for this " + what, elsewhere, filling, copying, null, clearing, removing);
            }

            /// <summary>
            /// What a row or column is checked against, grouped by the test that checks it.
            ///
            /// A mirrored matrix keeps each pair once, so half of what a discipline is checked
            /// against sits on the other axis. Following that by eye means tracking a line that
            /// turns at the diagonal across two scroll axes, so it is stated here in words.
            ///
            /// Grouped by test rather than listed per pair. Twenty-one bullets of equal weight,
            /// each repeating name, symbol, kind and tolerance, cannot be counted or scanned.
            /// Grouped, a row reads as three or four facts: seven hard, five clearances, three
            /// ruled out, three undecided.
            ///
            /// Ruled-out and undecided pairs are included; the undecided group is what states
            /// what is left to do on the line.
            /// </summary>
            /// <param name="line">The row or the column.</param>
            /// <param name="rows">True for a row.</param>
            private void Against(MatrixLine line, bool rows)
            {
                var held = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var open = new List<string>();

                foreach (var other in rows ? _board.Columns : _board.Rows)
                {
                    var row = rows ? line : other;
                    var column = rows ? other : line;
                    var symbol = _board.At(row, column);

                    if (symbol == null)
                    {
                        // The line against itself is not a pair anybody has left open.
                        if (!_board.IsSelfPair(row, column)) open.Add(other.Titled);

                        continue;
                    }

                    if (!held.TryGetValue(symbol, out var names))
                        held[symbol] = names = new List<string>();

                    names.Add(other.Titled);
                }

                if (held.Count == 0 && open.Count == 0) return;

                var titled = Ui.Small("Checked against");

                titled.Margin = new Thickness(0, 2, 0, 4);
                _inspector.Children.Add(titled);

                var groups = Ui.Stack();

                // The legend's own order, so the groups come out in the order the palette lists
                // them however the row happens to run - and a row read twice reads the same way.
                for (var at = 0; at < _legend.Symbols.Count; at++)
                {
                    var symbol = _legend.Symbols[at];

                    if (!held.TryGetValue(symbol.Symbol, out var names)) continue;

                    held.Remove(symbol.Symbol);

                    groups.Children.Add(Gathered(
                        symbol.Symbol, Washed(symbol), symbol.Enabled,
                        MatrixNaming.Worded(symbol.Test),
                        Millimetres(symbol.Tolerance)
                        + (symbol.Enabled ? string.Empty : " · turned off, so these build nothing"),
                        names));
                }

                // Whatever the legend has nothing to say about, including the reserved mark.
                foreach (var pair in held)
                {
                    var ruled = MatrixBoard.IsNotRequired(pair.Key);

                    groups.Children.Add(Gathered(
                        ruled ? MatrixBoard.NotRequired : pair.Key, null, false,
                        ruled ? "Not required" : Quoted(pair.Key),
                        ruled ? "agreed, and no test is made"
                              : "not in the legend, so these make no test",
                        pair.Value));
                }

                if (open.Count > 0)
                    groups.Children.Add(Gathered("", null, false, "Undecided",
                                                 "no test either way, yet", open));

                // No scroller of its own. Capped and scrolled inside the already-scrolling panel,
                // a wheel over it moved one of two things depending on the pointer position, and
                // the list could hide its own last group. The panel scrolls; the verbs, palette
                // and progress are docked clear of it.
                groups.Margin = new Thickness(0, 0, 0, 6);

                _inspector.Children.Add(groups);
            }

            /// <summary>
            /// One group of that list: the test, how many, and which disciplines.
            ///
            /// The names run on rather than one to a line. Twenty-one names down a panel is a
            /// column of text nobody reads to the end; the same names as a run are three lines,
            /// and the eye is looking for one of them rather than reading all of them.
            /// </summary>
            /// <param name="mark">The symbol, or empty for the pairs nobody has decided.</param>
            /// <param name="paint">The wash its cells carry, or null for anything not in the legend.</param>
            /// <param name="live">False for a symbol that builds nothing, and for the two marks.</param>
            /// <param name="title">What the test is.</param>
            /// <param name="under">The tolerance, or what the mark means.</param>
            /// <param name="names">The disciplines on the other side of each pair.</param>
            private static UIElement Gathered(string mark, System.Windows.Media.Brush? paint,
                                              bool live, string title,
                                              string under, IReadOnlyList<string> names)
            {
                var card = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 7),
                    Margin = new Thickness(0, 0, 0, 5),
                };

                card.Tint(Border.BorderBrushProperty, "Bd");
                card.Tint(Border.BackgroundProperty, "Card");

                var head = new DockPanel { LastChildFill = true };

                var letter = new TextBlock
                {
                    Text = mark,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body");

                if (!live && paint != null) letter.TextDecorations = TextDecorations.Strikethrough;

                var swatch = new Border
                {
                    Width = 22,
                    Height = 20,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = letter,
                };

                // The same wash the cells carry, so the group and the grid are one key. A group
                // with no symbol - the open one - is drawn as the empty cells it stands for.
                if (live && paint != null) swatch.Background = paint;
                else if (mark.Length == 0) swatch.Tint(Border.BackgroundProperty, "Card");
                else swatch.Tint(Border.BackgroundProperty, "Nav");

                if (live) letter.Foreground = Ui.InkOn(swatch.Background);

                if (mark.Length == 0) swatch.Tint(Border.BorderBrushProperty, "BdStrong");
                if (mark.Length == 0) swatch.BorderThickness = new Thickness(1);

                DockPanel.SetDock(swatch, Dock.Left);
                head.Children.Add(swatch);

                var tally = new TextBlock
                {
                    Text = names.Count.ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                }.Tint(TextBlock.ForegroundProperty, "Muted");

                DockPanel.SetDock(tally, Dock.Right);
                head.Children.Add(tally);

                var says = Ui.Stack();

                says.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                }.Wearing("Body"));

                var meant = Ui.Small(under);

                meant.TextWrapping = TextWrapping.Wrap;
                says.Children.Add(meant);

                head.Children.Add(says);

                var all = Ui.Stack();

                all.Children.Add(head);

                var listed = Ui.Small(string.Join(" · ", names));

                listed.TextWrapping = TextWrapping.Wrap;
                listed.Margin = new Thickness(0, 5, 0, 0);
                listed.Wearing("Body");
                listed.Opacity = 0.85;

                all.Children.Add(listed);

                card.Child = all;

                return card;
            }

            /// <summary>One symbol: what it means, which every cell carrying it follows.</summary>
            /// <param name="symbol">The symbol.</param>
            private void SymbolPage(MatrixSymbol symbol)
            {
                _inspector.Children.Add(Ui.Eyebrow("Symbol " + symbol.Symbol));

                // Symbol editing happens in one place: the form opened by the pencil on the
                // palette. This page previously offered five editable fields, making a second
                // route to change a symbol's meaning, and a colour picker in the grid-wide
                // settings window made a third. This page reports what the symbol is and what its
                // cells are doing.
                var meant = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 7, 9, 8),
                    Margin = new Thickness(0, 6, 0, 8),
                };

                meant.Tint(Border.BorderBrushProperty, "Bd");
                meant.Tint(Border.BackgroundProperty, "Card");

                var head = new DockPanel { LastChildFill = true };

                var letter = new TextBlock
                {
                    Text = symbol.Symbol,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body");

                if (!symbol.Enabled) letter.TextDecorations = TextDecorations.Strikethrough;

                var swatch = new Border
                {
                    Width = 24,
                    Height = 22,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = letter,
                };

                if (symbol.Enabled)
                {
                    swatch.Background = Washed(symbol);
                    letter.Foreground = Ui.InkOn(swatch.Background);
                }
                else swatch.Tint(Border.BackgroundProperty, "Nav");

                DockPanel.SetDock(swatch, Dock.Left);
                head.Children.Add(swatch);

                var reading = Ui.Stack();

                reading.Children.Add(new TextBlock
                {
                    Text = MatrixNaming.Worded(symbol.Test) + " at " + Millimetres(symbol.Tolerance),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                }.Wearing("Body"));

                var state = Ui.Small(symbol.Enabled
                    ? "Every cell carrying it becomes a test with these settings."
                    : "Turned off, so its cells say what was agreed and build nothing.");

                state.TextWrapping = TextWrapping.Wrap;
                reading.Children.Add(state);

                head.Children.Add(reading);

                meant.Child = head;
                _inspector.Children.Add(meant);

                var using_ = 0;

                foreach (var cell in _board.Cells())
                    if (string.Equals(cell.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase))
                        using_++;

                _inspector.Children.Add(Ui.Small(Ui.Count(using_, "cell") + " carrying it"
                    + (using_ == 0
                        ? "."
                        : ", lit in the grid while this is showing.")));

                var gap = new Border { Height = 8 };

                _inspector.Children.Add(gap);

                // Fills only the undecided pairs, so it cannot overwrite a decision. A matrix is
                // usually filled exceptions first and blanket last, and the only blanket fills
                // available were one line or the whole grid over everything already decided.
                var rest = Doable(Glyphs.Sweep,
                    "Apply to every undecided pair", () =>
                {
                    Remember(what: "filling every undecided pair with " + Quoted(symbol.Symbol));

                    var laid = _board.FillEmpty(symbol.Symbol);

                    RepaintAll();
                    Recount();
                    RedrawInspector();

                    Reported(laid, Quoted(symbol.Symbol) + " put in", "undecided pair");
                });

                var everywhere = Doable(Glyphs.SelectAll, "Apply to every cell", () =>
                    Parts.Sure(_grid,
                        "This puts " + Quoted(symbol.Symbol) + " in every cell of the matrix, "
                        + "over everything already decided. The diagonal is left alone.",
                        "Fill", () =>
                        {
                            Remember(what: "filling the whole grid with "
                                             + Quoted(symbol.Symbol));
                            _board.Fill(symbol.Symbol);
                            RepaintAll();

                            Did(Ui.Done("The grid is filled with " + Quoted(symbol.Symbol)
                                         + ". Undo takes it back."));
                        }));

                // Steps the cursor from one matching cell to the next. Reporting the count alone
                // left the matches to be found by eye on a grid too large to see at once.
                var stepping = Doable(Glyphs.Next,
                    "Go to the next cell carrying " + Quoted(symbol.Symbol),
                    () => Stepping(symbol.Symbol));

                stepping.IsEnabled = using_ > 0;

                Verbs("Symbol actions",
                    Doable(Glyphs.Pencil, "Change its mark, colour, kind and "
                                          + "its tolerance", EditSymbol),
                    stepping, rest, everywhere, null,
                    Doable(Glyphs.Bin, "Take " + Quoted(symbol.Symbol) + " out of the legend",
                        () =>
                        {
                            // Renaming a symbol takes its cells with it; dropping one leaves
                            // them saying something nothing explains, and used to do it on one
                            // unconfirmed press.
                            if (using_ == 0)
                            {
                                Reset(symbol, symbol.Test, symbol.Tolerance, symbol.Enabled,
                                      drop: true);
                                return;
                            }

                            Parts.Sure(_grid,
                                Ui.Count(using_, "cell") + " still carries " + Quoted(symbol.Symbol)
                                + ". Taking it out of the legend leaves them saying something the "
                                + "legend does not explain, and they build no tests. Rename it "
                                + "instead and the cells follow it.",
                                "Delete",
                                () => Reset(symbol, symbol.Test, symbol.Tolerance, symbol.Enabled,
                                            drop: true));
                        },
                        danger: true));
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

                if (!_board.IsDrawnHalf(row, column))
                {
                    var mirrored = Ui.Small("The other side of the diagonal. A pair is only run "
                                            + "once, so what goes here goes in both.");

                    mirrored.TextWrapping = TextWrapping.Wrap;
                    mirrored.Margin = new Thickness(0, 0, 0, 9);
                    _inspector.Children.Add(mirrored);
                }

                // The palette is drawn once. This page previously rebuilt the same keys from the
                // same component, 200px above the real palette, and the two behaved differently:
                // one set the cell, the other armed the brush. The palette arms; this page reports
                // the cell's contents and how to change them.
                var held = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(9, 7, 9, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                };

                held.Tint(Border.BorderBrushProperty, "Bd");
                held.Tint(Border.BackgroundProperty, "Card");

                var meaning = here == null ? null : _legend.Meaning(here);

                var says = Ui.Stack();

                says.Children.Add(new TextBlock
                {
                    Text = here == null
                        ? "Nothing yet"
                        : MatrixBoard.IsNotRequired(here)
                            ? "Not required"
                            : Quoted(here) + (meaning == null
                                ? " — not in the legend"
                                : " — " + MatrixNaming.Worded(meaning.Test)),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                }.Wearing("Body"));

                var under = Ui.Small(here == null
                    ? "This pair is undecided."
                    : MatrixBoard.IsNotRequired(here)
                        ? "Agreed, and no test is made."
                        : meaning == null
                            ? "Define it in the palette, or this cell builds no test."
                            : Millimetres(meaning.Tolerance)
                              + (meaning.Enabled ? string.Empty : " · turned off, so it builds nothing"));

                under.TextWrapping = TextWrapping.Wrap;
                says.Children.Add(under);

                held.Child = says;
                _inspector.Children.Add(held);

                var how = Ui.Small("Type its key with the cursor in the grid, or press it on the "
                                   + "palette below and click. Shift and an arrow takes a block.");

                how.TextWrapping = TextWrapping.Wrap;
                _inspector.Children.Add(how);

                var ruling = Doable(Glyphs.Barred, "Rule this pair out — agreed, and no test", () =>
                {
                    Remember(what: "ruling out a pair");
                    _board.Set(row, column, MatrixBoard.NotRequired);
                    Repaint(row, column);
                    RedrawInspector();
                    Recount();
                });

                ruling.IsEnabled = !MatrixBoard.IsNotRequired(here);

                var emptying = Doable(Glyphs.Eraser, "Clear the cell", () =>
                {
                    Remember(what: "clearing a cell");
                    _board.Set(row, column, null);
                    Repaint(row, column);
                    RedrawInspector();
                    Recount();
                }, danger: true);

                emptying.IsEnabled = here != null;

                var onward = Doable(Glyphs.Next, "Go to the next undecided pair",
                                    Undecided);

                Verbs("Cell actions", ruling, onward, null, emptying);
            }

            // ---------------------------------------------------------------------------
            // The keyboard
            // ---------------------------------------------------------------------------

            /// <summary>Draw the ring where the cursor is, and the pane over the block.</summary>
            private void RedrawRing()
            {
                if (_atRow < 0 || _atColumn < 0
                    || _atRow >= _board.Rows.Count || _atColumn >= _board.Columns.Count)
                {
                    _ring.Visibility = Visibility.Collapsed;
                    _range.Visibility = Visibility.Collapsed;
                    return;
                }

                Grid.SetRow(_ring, _atRow);
                Grid.SetColumn(_ring, _atColumn);
                _ring.Visibility = Visibility.Visible;

                if (!Blocked)
                {
                    _range.Visibility = Visibility.Collapsed;
                    return;
                }

                Grid.SetRow(_range, Math.Min(_atRow, _markRow));
                Grid.SetColumn(_range, Math.Min(_atColumn, _markColumn));
                Grid.SetRowSpan(_range, Math.Abs(_atRow - _markRow) + 1);
                Grid.SetColumnSpan(_range, Math.Abs(_atColumn - _markColumn) + 1);
                _range.Visibility = Visibility.Visible;
            }

            /// <summary>Whether a block of more than one cell is marked out.</summary>
            private bool Blocked =>
                _markRow >= 0 && _markColumn >= 0
                && _markRow < _board.Rows.Count && _markColumn < _board.Columns.Count
                && (_markRow != _atRow || _markColumn != _atColumn);

            /// <summary>Let go of the block, if one is held.</summary>
            private void Unblock()
            {
                if (_markRow < 0 && _markColumn < 0) return;

                _markRow = -1;
                _markColumn = -1;

                RedrawRing();
            }

            /// <summary>
            /// Take the block from wherever the cursor is to wherever it is being dragged to.
            ///
            /// The anchor is set once, on the first shifted press or arrow, and stays put while
            /// the other corner moves - which is how a range behaves in every sheet there has
            /// ever been.
            /// </summary>
            /// <param name="down">The row the other corner is on.</param>
            /// <param name="across">The column it is on.</param>
            private void Reach(int down, int across)
            {
                if (_markRow < 0 || _markColumn < 0)
                {
                    _markRow = _atRow < 0 ? down : _atRow;
                    _markColumn = _atColumn < 0 ? across : _atColumn;
                }

                _atRow = Math.Max(0, Math.Min(_board.Rows.Count - 1, down));
                _atColumn = Math.Max(0, Math.Min(_board.Columns.Count - 1, across));

                Settled();
            }

            /// <summary>Every cell of the block, or the one under the cursor when there is none.</summary>
            private IEnumerable<(MatrixLine Row, MatrixLine Column)> Block()
            {
                if (_atRow < 0 || _atColumn < 0
                    || _atRow >= _board.Rows.Count || _atColumn >= _board.Columns.Count)
                    yield break;

                var fromRow = Blocked ? Math.Min(_atRow, _markRow) : _atRow;
                var toRow = Blocked ? Math.Max(_atRow, _markRow) : _atRow;
                var fromColumn = Blocked ? Math.Min(_atColumn, _markColumn) : _atColumn;
                var toColumn = Blocked ? Math.Max(_atColumn, _markColumn) : _atColumn;

                for (var r = fromRow; r <= toRow; r++)
                for (var c = fromColumn; c <= toColumn; c++)
                    yield return (_board.Rows[r], _board.Columns[c]);
            }

            /// <summary>
            /// Copies the selected cells, and pastes them elsewhere.
            ///
            /// Repeated blocks are common: several disciplines treating services identically is
            /// the same column of marks repeated.
            ///
            /// Tab-separated text, so the system clipboard is interoperable: a block copied here
            /// pastes into Excel as cells, and a block of letters from a spreadsheet pastes in
            /// here. An empty cell is an empty field, which keeps a paste aligned with the shape
            /// it was cut from.
            /// </summary>
            /// <param name="cut">True to empty the cells after copying them.</param>
            private void Copy(bool cut = false)
            {
                var held = Block().ToList();

                if (held.Count == 0) return;

                var fromRow = Blocked ? Math.Min(_atRow, _markRow) : _atRow;
                var fromColumn = Blocked ? Math.Min(_atColumn, _markColumn) : _atColumn;
                var toRow = Blocked ? Math.Max(_atRow, _markRow) : _atRow;
                var toColumn = Blocked ? Math.Max(_atColumn, _markColumn) : _atColumn;

                var text = new System.Text.StringBuilder();

                for (var r = fromRow; r <= toRow; r++)
                {
                    if (r > fromRow) text.Append('\n');

                    for (var c = fromColumn; c <= toColumn; c++)
                    {
                        if (c > fromColumn) text.Append('\t');

                        text.Append(_board.At(_board.Rows[r], _board.Columns[c]) ?? string.Empty);
                    }
                }

                // The clipboard is another process's to refuse - a program holding it open, a
                // remote session with none at all - and a screen that dies because a copy did
                // not work is worse than a copy that did not work.
                try
                {
                    Clipboard.SetText(text.ToString());
                }
                catch (Exception)
                {
                    Warned(Ui.Problem("Windows would not give up the clipboard. Try again."));
                    return;
                }

                if (cut) Laid(held, null, "cutting " + Ui.Count(held.Count, "cell"));

                Did(Ui.Done(Ui.Count(held.Count, "cell") + (cut ? " cut" : " copied")
                             + ". Put the cursor where it goes and press Ctrl+V."));
            }

            /// <summary>
            /// Pastes the copied block with the cursor as its top-left corner.
            ///
            /// Clipped at the edges of the grid rather than wrapped or refused: a block pasted
            /// near the end places as much as fits and reports how much, which is more useful
            /// than an error about a rectangle that does not fit.
            /// </summary>
            private void Paste()
            {
                if (_atRow < 0 || _atColumn < 0) return;

                string held;

                try
                {
                    if (!Clipboard.ContainsText()) { Warned(Ui.Problem("There is nothing to paste.")); return; }

                    held = Clipboard.GetText();
                }
                catch (Exception)
                {
                    Warned(Ui.Problem("Windows would not give up the clipboard. Try again."));
                    return;
                }

                if (string.IsNullOrEmpty(held)) return;

                var lines = held.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                var known = new HashSet<string>(_legend.Symbols.Select(sym => sym.Symbol),
                                                StringComparer.OrdinalIgnoreCase);

                var laid = 0;
                var strange = new List<string>();

                Remember(what: "pasting cells");

                for (var r = 0; r < lines.Length; r++)
                {
                    var at = _atRow + r;

                    if (at >= _board.Rows.Count) break;

                    var fields = lines[r].Split('\t');

                    for (var c = 0; c < fields.Length; c++)
                    {
                        var into = _atColumn + c;

                        if (into >= _board.Columns.Count) break;

                        var said = fields[c].Trim();

                        // A symbol nobody has given a meaning to would build a test with no kind
                        // and no tolerance, so it is put in and named rather than dropped: the
                        // legend can be told what it means, and until it is, the pre-flight says
                        // so in the words it already uses for a symbol with no meaning.
                        if (said.Length > 0 && !known.Contains(said)
                            && !MatrixBoard.IsNotRequired(said) && !strange.Contains(said))
                            strange.Add(said);

                        _board.Set(_board.Rows[at], _board.Columns[into],
                                   said.Length == 0 ? null : said);
                        laid++;
                    }
                }

                RepaintAll();
                Recount();

                Did(Ui.Done(Ui.Count(laid, "cell") + " pasted"
                             + (strange.Count == 0
                                 ? "."
                                 : ", and " + Ui.Count(strange.Count, "symbol")
                                   + " nothing in the legend explains: "
                                   + string.Join(", ", strange.Select(Quoted)) + ".")));
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
                    else if (e.Key == Key.A) { Everything(); e.Handled = true; }
                    else if (e.Key == Key.C) { Copy(); e.Handled = true; }
                    else if (e.Key == Key.X) { Copy(cut: true); e.Handled = true; }
                    else if (e.Key == Key.V) { Paste(); e.Handled = true; }
                    else if (e.Key == Key.Home) { Step(-_board.Rows.Count, -_board.Columns.Count); e.Handled = true; }
                    else if (e.Key == Key.End) { Step(_board.Rows.Count, _board.Columns.Count); e.Handled = true; }

                    return;
                }

                if (_atRow < 0 || _atColumn < 0) { _atRow = 0; _atColumn = 0; }

                // Alt with an arrow moves the line itself. Reordering was drag-only, so the one
                // gesture that changes the matrix's structure was unavailable without a mouse.
                if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                {
                    switch (e.Key)
                    {
                        case Key.Up: Shift(rows: true, by: -1); e.Handled = true; return;
                        case Key.Down: Shift(rows: true, by: 1); e.Handled = true; return;
                        case Key.Left: Shift(rows: false, by: -1); e.Handled = true; return;
                        case Key.Right: Shift(rows: false, by: 1); e.Handled = true; return;
                    }

                    return;
                }

                // Shift with an arrow extends the block. The cursor moves as usual; the opposite
                // corner stays, and everything between them receives the next symbol.
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    switch (e.Key)
                    {
                        case Key.Left: Reach(_atRow, _atColumn - 1); e.Handled = true; return;
                        case Key.Right: Reach(_atRow, _atColumn + 1); e.Handled = true; return;
                        case Key.Up: Reach(_atRow - 1, _atColumn); e.Handled = true; return;
                        case Key.Down: Reach(_atRow + 1, _atColumn); e.Handled = true; return;
                        case Key.Home: Reach(_atRow, 0); e.Handled = true; return;
                        case Key.End: Reach(_atRow, _board.Columns.Count - 1); e.Handled = true; return;
                        case Key.PageUp: Reach(_atRow - Screenful, _atColumn); e.Handled = true; return;
                        case Key.PageDown: Reach(_atRow + Screenful, _atColumn); e.Handled = true; return;
                    }
                }

                // The number keys apply to whatever the view is drawing. The arrows, the marker
                // and the block are the same in all three views; a "2" that armed the second
                // symbol while the priority view was showing would write a decision the view does
                // not display.
                if (_shown == Showing.Priority)
                {
                    var wanted = Ranking(e.Key);

                    if (wanted >= 0)
                    {
                        _rank = wanted;

                        RedrawPalette();
                        RedrawStrip();
                        Pointing();
                        LayRank();

                        e.Handled = true;
                        return;
                    }
                }

                // Nothing is laid down over a count that came out of the document, from the
                // keyboard any more than from the hand.
                if (_shown == Showing.Results)
                    switch (e.Key)
                    {
                        case Key.Delete:
                        case Key.Back:
                        case Key.Space:
                        case Key.OemMinus:
                        case Key.Subtract:
                            e.Handled = true;
                            return;
                    }

                switch (e.Key)
                {
                    case Key.Left: Step(0, -1); e.Handled = true; return;
                    case Key.Right: Step(0, 1); e.Handled = true; return;
                    case Key.Up: Step(-1, 0); e.Handled = true; return;
                    case Key.Down: Step(1, 0); e.Handled = true; return;

                    // A grid built to be bigger than the window had no way of moving a
                    // windowful at a time, which on a matrix of forty rows is forty presses.
                    case Key.PageUp: Step(-Screenful, 0); e.Handled = true; return;
                    case Key.PageDown: Step(Screenful, 0); e.Handled = true; return;

                    case Key.Escape: Unblock(); e.Handled = true; return;

                    case Key.Home: Step(0, -_board.Columns.Count); e.Handled = true; return;
                    case Key.End: Step(0, _board.Columns.Count); e.Handled = true; return;

                    case Key.Delete:
                    case Key.Back:
                    case Key.D0:
                    case Key.NumPad0:
                        Lay(null);
                        e.Handled = true;
                        return;

                    case Key.OemMinus:
                    case Key.Subtract:
                        Lay(MatrixBoard.NotRequired);
                        e.Handled = true;
                        return;

                    case Key.Space:
                        if (_brush != null) Lay(Laying);
                        e.Handled = true;
                        return;
                }

                if (_shown != Showing.Symbols) return;

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

            /// <summary>
            /// Which rank a key stands for while the priority view is on, or -1 for a key that
            /// is not one.
            ///
            /// The same three digits the palette prints on its keys, and nought for taking a
            /// rank off - the way nought empties a cell on the symbols. The delete keys and the
            /// space bar come here too, so the hands that already know this grid do not have to
            /// learn a second set.
            /// </summary>
            /// <param name="key">The key.</param>
            private int Ranking(Key key)
            {
                switch (key)
                {
                    case Key.D1:
                    case Key.NumPad1: return MatrixBoard.High;

                    case Key.D2:
                    case Key.NumPad2: return MatrixBoard.Medium;

                    case Key.D3:
                    case Key.NumPad3: return MatrixBoard.Low;

                    case Key.D0:
                    case Key.NumPad0:
                    case Key.Delete:
                    case Key.Back: return MatrixBoard.NotSet;

                    case Key.Space: return _rank;
                }

                return -1;
            }

            /// <summary>
            /// Put the armed rank in the cell the cursor is on, or across the marked block.
            ///
            /// The same two shapes <see cref="Lay"/> has, for the same reason: a matrix is
            /// ranked in runs - a discipline is urgent against everything wet, or a package is
            /// not urgent against anything - and a run is a marked block and one key.
            /// </summary>
            private void LayRank()
            {
                if (_atRow < 0 || _atColumn < 0
                    || _atRow >= _board.Rows.Count || _atColumn >= _board.Columns.Count) return;

                if (Blocked)
                {
                    var laid = 0;

                    Remember(what: _rank == MatrixBoard.NotSet
                        ? "clearing the ranks on a block"
                        : "ranking a block " + MatrixBoard.RankNamed(_rank).ToLowerInvariant());

                    foreach (var (row, column) in Block())
                    {
                        if (_board.IsSelfPair(row, column)) continue;

                        var held = _board.At(row, column);

                        if (held == null || MatrixBoard.IsNotRequired(held)) continue;
                        if (_board.RankAt(row, column) == _rank) continue;

                        _board.Rank(row, column, _rank);
                        Repaint(row, column);
                        laid++;
                    }

                    Reported(laid,
                             _rank == MatrixBoard.NotSet
                                 ? "Unranked"
                                 : "set to " + MatrixBoard.RankNamed(_rank).ToLowerInvariant(),
                             "checked cell of the block");

                    RedrawProgress();
                    return;
                }

                var one = _board.Rows[_atRow];
                var against = _board.Columns[_atColumn];
                var symbol = _board.At(one, against);

                // A rank goes on a pair somebody has agreed to check. On one that is undecided
                // the cursor still moves on, because a hand ranking a run down a row should not
                // stop dead at the one cell nobody has ruled on yet.
                if (symbol != null && !MatrixBoard.IsNotRequired(symbol)
                    && _board.RankAt(one, against) != _rank)
                {
                    Remember(what: _rank == MatrixBoard.NotSet
                        ? "clearing a rank"
                        : "ranking a pair " + MatrixBoard.RankNamed(_rank).ToLowerInvariant());

                    _board.Rank(one, against, _rank);
                    Repaint(one, against);
                    RedrawProgress();
                }

                Step(0, 1);
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
                _markRow = -1;
                _markColumn = -1;

                _atRow = Onward(_board.Rows, _atRow, down);
                _atColumn = Onward(_board.Columns, _atColumn, across);

                Settled();
            }

            /// <summary>
            /// Where the cursor lands, stepping over anything a shut folder is hiding.
            ///
            /// A shut folder keeps its lines - they are given no height and no width rather than
            /// taken out - so every index and every cell stays what it was and nothing else has
            /// to know about folders. The cursor is the one thing that does: walked into a line
            /// nobody can see it would sit on a cell that is not on the screen, and the next
            /// arrow would look like it did nothing.
            /// </summary>
            /// <param name="lines">The rows or the columns.</param>
            /// <param name="from">Where the cursor is.</param>
            /// <param name="by">How far, and which way.</param>
            private int Onward(IReadOnlyList<MatrixLine> lines, int from, int by)
            {
                if (lines.Count == 0) return 0;

                var at = Math.Max(0, Math.Min(lines.Count - 1, from + by));

                // Folded as well as hidden: the line a shut folder collapses to draws the
                // folder, not its own cells, so there is nothing on it for a cursor to sit in.
                bool Past(MatrixLine line) => Hidden(line) || Folded(line);

                if (!Past(lines[at])) return at;

                // On past it, the way it was already going; and if that runs out of grid, back
                // the other way, so the last visible line is never a place the cursor cannot leave.
                var way = by >= 0 ? 1 : -1;

                for (var tried = at; tried >= 0 && tried < lines.Count; tried += way)
                    if (!Past(lines[tried])) return tried;

                for (var tried = at; tried >= 0 && tried < lines.Count; tried -= way)
                    if (!Past(lines[tried])) return tried;

                return Math.Max(0, Math.Min(lines.Count - 1, from));
            }

            /// <summary>How many rows a page key moves, which is however many are on the screen.</summary>
            private int Screenful =>
                Math.Max(1, (int)Math.Floor(Math.Max(1, _scroll.ViewportHeight) / CellHigh) - 1);

            /// <summary>Take the whole grid as one block.</summary>
            private void Everything()
            {
                if (_board.Rows.Count == 0 || _board.Columns.Count == 0) return;

                _atRow = 0;
                _atColumn = 0;
                _markRow = _board.Rows.Count - 1;
                _markColumn = _board.Columns.Count - 1;

                Settled();
            }

            /// <summary>
            /// Move the row or column the cursor is on, one place, and follow it.
            ///
            /// The cursor moves with the line rather than staying where it was, so holding the
            /// keys walks one row up a list the way dragging it does.
            /// </summary>
            /// <param name="rows">True to move the row, false the column.</param>
            /// <param name="by">-1 for up or left, 1 for down or right.</param>
            private void Shift(bool rows, int by)
            {
                var at = rows ? _atRow : _atColumn;
                var count = rows ? _board.Rows.Count : _board.Columns.Count;
                var to = at + by;

                if (at < 0 || to < 0 || to >= count) return;

                Remember(what: "moving that " + (rows ? "row" : "column"));

                _board.MoveLine(rows ? _board.Rows[at] : _board.Columns[at], to);

                if (rows) _atRow = to; else _atColumn = to;

                _markRow = -1;
                _markColumn = -1;

                Redraw();
                Settled();
            }

            /// <summary>
            /// Handles cursor movement: draws it, scrolls to it, and reports its position.
            ///
            /// The inspector follows the cursor, since there is one selection. It previously
            /// refused to redraw while a row was selected, so pressing a row and then using the
            /// arrows left the inspector describing that row while symbols went into cells
            /// elsewhere.
            /// </summary>
            private void Settled()
            {
                if (_picked != null || _pickedSymbol != null)
                {
                    _picked = null;
                    _pickedSymbol = null;

                    Lighting(null);

                    RedrawAcross();
                    RedrawDown();
                }

                RedrawRing();
                Bring();
                RedrawStrip();
                RedrawInspector();
            }

            /// <summary>
            /// Put a symbol in the cell under the cursor and step right, as a sheet does - or in
            /// every cell of the block, which is the same gesture at the size the job works at.
            /// </summary>
            /// <param name="symbol">The symbol, or null to empty it.</param>
            private void Lay(string? symbol)
            {
                if (_atRow < 0 || _atColumn < 0
                    || _atRow >= _board.Rows.Count || _atColumn >= _board.Columns.Count) return;

                if (Blocked)
                {
                    var laid = 0;

                    // Ctrl+A followed by a symbol key fills only undecided pairs, matching every
                    // other bulk gesture here. It previously overwrote every decision on the grid,
                    // while the equivalent menu action asked first. The deliberate overwrite
                    // remains on the deliberate press.
                    var all = _markRow == 0 && _markColumn == 0
                              && _atRow == _board.Rows.Count - 1
                              && _atColumn == _board.Columns.Count - 1;

                    Remember(what: symbol == null
                        ? "clearing a block of cells"
                        : "putting " + Quoted(symbol) + " in a block of cells");

                    foreach (var (row, column) in Block())
                    {
                        if (all && symbol != null && _board.At(row, column) != null) continue;

                        // The diagonal is left out of a block the way it is left out of every
                        // other fill: a discipline against itself is a decision somebody makes
                        // deliberately, one cell at a time.
                        if (_board.IsSelfPair(row, column)) continue;
                        if (string.Equals(_board.At(row, column), symbol, StringComparison.Ordinal)) continue;

                        _board.Set(row, column, symbol);
                        Repaint(row, column);
                        laid++;
                    }

                    Reported(laid, symbol == null ? "Cleared" : Quoted(symbol) + " put in",
                             all ? "undecided pair of the grid" : "cell of the block");

                    // The block stays, so a wrong symbol is one keystroke to correct rather than
                    // a selection to make again.
                    return;
                }

                var one = _board.Rows[_atRow];
                var against = _board.Columns[_atColumn];

                Remember(what: symbol == null ? "clearing a cell" : "putting " + Quoted(symbol) + " in a cell");

                _board.Set(one, against, symbol);
                Repaint(one, against);

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
                json.Set("style", _style.ToJson());

                return json;
            }

            /// <summary>Put the board and the legend back to what one of those was.</summary>
            /// <param name="json">What they were.</param>
            private void Restore(JsonValue json)
            {
                // What was on the brush, so it can be picked up again on the other side. Undoing
                // a fill and then having to go and re-arm the symbol to carry on is the sort of
                // small tax that stops people using undo at all.
                var was = _brush?.Symbol;

                _board = MatrixBoard.FromJson(json["board"]);
                _legend = LegendOf(json["legend"]);
                _style = MatrixStyle.FromJson(json["style"]);

                _board.UpperIsLive = _style.UpperIsLive;

                _picked = null;
                _pickedSymbol = null;
                _brush = was == null
                    ? null
                    : _legend.Symbols.FirstOrDefault(sym => string.Equals(
                        sym.Symbol, was, StringComparison.Ordinal));

                Redraw();
            }

            /// <summary>
            /// Records the matrix's current state so the next edit can be undone.
            ///
            /// A run of keystrokes is one act: typing "150" into a tolerance is three changes and
            /// one user action, so an edit that names its run is recorded only the first time that
            /// run is seen. Any other action clears the name.
            /// </summary>
            /// <param name="run">Names a run of edits sharing one undo entry, or empty.</param>
            /// <param name="what">What is about to happen, for undo to report.</param>
            private void Remember(string run = "", string what = "")
            {
                if (run.Length > 0 && string.Equals(run, _remembered, StringComparison.Ordinal))
                    return;

                _remembered = run;

                Touched();

                _past.Push(new Doing(Held(), what.Length > 0 ? what : "that change"));
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

                var step = _past.Pop();

                _future.Push(new Doing(Held(), step.What));

                Restore(step.Held);

                Said(Ui.Note("Undone: " + step.What + ". Redo puts it back."));
            }

            private void Redo()
            {
                if (_future.Count == 0) return;

                var step = _future.Pop();

                _past.Push(new Doing(Held(), step.What));

                Restore(step.Held);

                Said(Ui.Note("Redone: " + step.What + "."));
            }

            /// <summary>The legend, as the project file and the undo stack both hold it.</summary>
            private JsonValue LegendJson() =>
                JsonValue.Array(_legend.Symbols.Select(s =>
                    JsonValue.Object()
                        .Set("symbol", s.Symbol)
                        .Set("test", (double)(int)s.Test)
                        .Set("tolerance", s.Tolerance)
                        .Set("on", s.Enabled)
                        .Set("colour", s.Colour)));

            /// <summary>A legend read back out of that.</summary>
            /// <param name="json">What was written.</param>
            private static MatrixLegend LegendOf(JsonValue? json) =>
                new MatrixLegend((json?.Items ?? Enumerable.Empty<JsonValue>()).Select(held =>
                    new MatrixSymbol(held["symbol"].AsString() ?? string.Empty,
                                     (MatrixTest)(int)held["test"].AsDouble(),
                                     held["tolerance"].AsDouble(),
                                     held["on"].AsBool(true),
                                     held["colour"].AsString())));

            /// <summary>
            /// Change one symbol's meaning, keeping every other symbol as it was.
            ///
            /// The legend is immutable, so an edit is a new legend built from the old one. Every
            /// cell carrying that symbol changes meaning at once, which is the point of a legend
            /// and is why the cells are repainted rather than the grid rebuilt.
            /// </summary>
            /// <param name="symbol">The symbol being changed.</param>
            /// <param name="test">The kind of test it now means.</param>
            /// <param name="tolerance">The tolerance it now carries, in metres.</param>
            /// <param name="enabled">Whether tests are built from it.</param>
            /// <param name="drop">True to take it out of the legend altogether.</param>
            /// <param name="run">
            /// Names a run of edits that should cost one undo between them, such as typing a
            /// tolerance digit by digit. Empty for a single act.
            /// </param>
            /// <param name="rebuild">
            /// False while the inspector is the thing being typed into. Redrawing it under the
            /// caret takes the focus away, so a tolerance could only ever be one digit long.
            /// </param>
            /// <param name="colour">
            /// The colour its cells carry, empty to go back to the one its place in the legend
            /// gives it, or null to leave it as it is.
            /// </param>
            private void Reset(MatrixSymbol symbol, MatrixTest test, double tolerance,
                               bool enabled, bool drop = false, string run = "",
                               bool rebuild = true, string? colour = null)
            {
                Remember(run, drop
                    ? "taking " + Quoted(symbol.Symbol) + " out of the legend"
                    : "changing what " + Quoted(symbol.Symbol) + " means");

                // The symbol is replaced in place rather than removed and appended. The legend's
                // order determines each symbol's number key and its automatic wash, so rebuilding
                // by filtering and appending moved an edited symbol to the end: its keystroke
                // changed, every symbol below it shifted up, and every automatically coloured cell
                // changed colour.
                var kept = _legend.Symbols.ToList();

                var at = kept.FindIndex(one => string.Equals(one.Symbol, symbol.Symbol,
                                                             StringComparison.OrdinalIgnoreCase));

                var made = drop
                    ? null
                    : new MatrixSymbol(symbol.Symbol, test, tolerance, enabled,
                                       colour ?? symbol.Colour);

                if (made == null) { if (at >= 0) kept.RemoveAt(at); }
                else if (at >= 0) kept[at] = made;
                else kept.Add(made);

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

                if (rebuild) RedrawInspector();

                RepaintAll();
            }

            /// <summary>
            /// Open the window that says how the matrix is drawn.
            ///
            /// Everything in it applies as it is pressed, against the grid behind the window, and
            /// is saved with the matrix like any other edit - because a colour is judged by
            /// looking at the grid, not by reading the name of it in a dialogue.
            /// </summary>
            private void Looks() =>
                MatrixLook.Ask(_style,
                    () =>
                    {
                        // Recorded for undo like every other edit. This panel touched the save
                        // timer without recording anything, so a colour change could only be
                        // reversed by remembering the previous value.
                        Remember("look", "changing how the matrix is drawn");
                        Touched();

                        // The half is on the board as well as in the style, so that undo and the
                        // saved grid keep it. Setting it here is what keeps the two in step.
                        _board.UpperIsLive = _style.UpperIsLive;

                        // A whole redraw rather than a repaint: the field settings change how
                        // wide the row headers are and how tall the column band is, which is the
                        // shape of the grid rather than what is in it.
                        Redraw();
                    });

            /// <summary>
            /// Renames a symbol and updates the cells carrying it.
            ///
            /// A symbol's letter was previously fixed at creation, so a legend imported from a
            /// client's sheet as "1" and "2" could not be renamed except by removing the entry,
            /// which orphaned every cell using it, and repainting them.
            /// </summary>
            /// <param name="symbol">The symbol as it is.</param>
            /// <param name="to">What it should be called.</param>
            private void Rename(MatrixSymbol symbol, string to)
            {
                var wanted = (to ?? string.Empty).Trim();

                if (wanted.Length == 0
                    || string.Equals(wanted, symbol.Symbol, StringComparison.Ordinal)) return;

                if (MatrixBoard.IsNotRequired(wanted))
                {
                    Warned(Ui.Problem("A dash is the mark for a pair ruled out, so it cannot be "
                                      + "a symbol as well."));
                    return;
                }

                if (_legend.Meaning(wanted) != null)
                {
                    Warned(Ui.Problem("The legend already has a " + Quoted(wanted) + "."));
                    return;
                }

                Remember(what: "renaming " + Quoted(symbol.Symbol) + " to " + Quoted(wanted));

                // Where it already sat, for the same reason: a renamed symbol that jumped to the
                // end of the legend renumbered every key under it.
                var kept = _legend.Symbols.ToList();

                var at = kept.FindIndex(one => string.Equals(one.Symbol, symbol.Symbol,
                                                             StringComparison.OrdinalIgnoreCase));

                var made = new MatrixSymbol(wanted, symbol.Test, symbol.Tolerance,
                                            symbol.Enabled, symbol.Colour);

                if (at >= 0) kept[at] = made; else kept.Add(made);

                _legend = new MatrixLegend(kept);

                var moved = _board.Recast(symbol.Symbol, wanted);

                if (_brush != null && string.Equals(_brush.Symbol, symbol.Symbol,
                                                    StringComparison.OrdinalIgnoreCase))
                    _brush = made;

                _pickedSymbol = made;

                Redraw();
                Show(made);

                Did(Ui.Done(Quoted(symbol.Symbol) + " is now " + Quoted(wanted)
                             + (moved == 0
                                 ? ". No cell was carrying it."
                                 : ", and so are " + Ui.Count(moved, "cell") + ".")));
            }

            /// <summary>
            /// Creates a symbol, asking for all five of its properties at once.
            ///
            /// It previously asked for the letter only, so every symbol was created as a hard
            /// clash at the default tolerance and the remaining four decisions had to be found
            /// afterwards.
            /// </summary>
            private void AddSymbol()
            {
                var made = MatrixKey.Ask("A symbol for the legend", "Add",
                    new MatrixKeyEdit { Symbol = Suggested() }, Clashing);

                if (made == null) return;

                Remember(what: "adding the symbol " + Quoted(made.Symbol));

                var one = new MatrixSymbol(made.Symbol, made.Test, made.Tolerance, made.Enabled,
                                           made.Colour);

                _legend = new MatrixLegend(_legend.Symbols.Concat(new[] { one }));

                RedrawPalette();
                RepaintAll();
                Recount();

                Arm(one);
                Show(one);
            }

            /// <summary>
            /// Change one, in the same form it was made in.
            ///
            /// The one on the brush, or the one the panel is showing - which are the two ways a
            /// symbol is ever "the one you mean" on this screen.
            /// </summary>
            private void EditSymbol()
            {
                var symbol = _pickedSymbol ?? _brush;

                if (symbol == null || _legend.Meaning(symbol.Symbol) == null)
                {
                    Warned(Ui.Note("Press a symbol on the palette first, and the pencil changes "
                                   + "that one."));
                    return;
                }

                var was = symbol.Symbol;

                var made = MatrixKey.Ask("The symbol " + was, "Save",
                    new MatrixKeyEdit
                    {
                        Symbol = was,
                        Colour = symbol.Colour,
                        Test = symbol.Test,
                        Tolerance = symbol.Tolerance,
                        Enabled = symbol.Enabled,
                    },
                    wanted => string.Equals(wanted, was, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : Clashing(wanted));

                if (made == null) return;

                // The rename first, because everything else is keyed on the name it ends up with.
                if (!string.Equals(made.Symbol, was, StringComparison.Ordinal))
                    Rename(symbol, made.Symbol);

                var now = _legend.Meaning(made.Symbol);

                if (now == null) return;

                Reset(now, made.Test, made.Tolerance, made.Enabled, colour: made.Colour);
                Recount();
            }

            /// <summary>
            /// Why a symbol cannot be called that, or null when it can.
            ///
            /// The legend is keyed on the symbol, so a name that collides does not fail - it
            /// quietly replaces the entry it collided with, taking its tolerance and every cell's
            /// meaning with it.
            /// </summary>
            /// <param name="wanted">The name being asked for.</param>
            private string? Clashing(string wanted)
            {
                if (MatrixBoard.IsNotRequired(wanted))
                    return "A dash is the mark for a pair ruled out, so it cannot be a symbol "
                           + "as well.";

                return _legend.Meaning(wanted) == null
                    ? null
                    : "The legend already has a " + Quoted(wanted) + ".";
            }

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
                    Did(Ui.Done("Every symbol in the grid is already in the legend."));
                    return;
                }

                Remember(what: "covering the grid\u2019s symbols");

                _legend = _legend.Covering(_board);

                RedrawPalette();
                RepaintAll();

                Did(Ui.Done(Ui.Count(missing.Count, "symbol")
                             + " added as an ordinary hard clash. Correct the ones that are not."));
            }


            // ---------------------------------------------------------------------------
            // Where a matrix comes from, and where it goes
            // ---------------------------------------------------------------------------

            private List<Parts.Entry> Starting() => new List<Parts.Entry>
            {
                Parts.Entry.Saying("From saved sets", Glyphs.Folder,
                    "A row per set you tick, named after it and pointed at it",
                    FromSets),

                Parts.Entry.Saying("From existing clash tests", Glyphs.Model,
                    "Every test says what it is between, so the matrix is already there",
                    FromModel),

                Parts.Entry.Saying("From a spreadsheet", Glyphs.Table,
                    "The client's own workbook: choose the tab and where the matrix starts on it",
                    FromSheet),

                Parts.Entry.Saying("Reload saved matrix", Glyphs.Folder,
                    "The matrix as it was left last time",
                    () =>
                    {
                        // It pushed an undo entry and returned in silence when the project had
                        // nothing saved: a menu item that did nothing and said nothing.
                        var rows = _board.Rows.Count;
                        var columns = _board.Columns.Count;

                        Remember(what: "reading the saved matrix back in");

                        Load();
                        Redraw();

                        if (_board.Rows.Count == rows && _board.Columns.Count == columns
                            && rows + columns == 0)
                            Warned(Ui.Note("Nothing is saved in this project yet. A matrix saves "
                                           + "itself as you work, so there will be one here once "
                                           + "you have started."));
                    }),

                Parts.Entry.Rule(),

                Parts.Entry.Danger("Clear matrix", Glyphs.Bin, () =>
                    Parts.Sure(_grid,
                        "This clears the grid and the legend. Nothing in the document changes - "
                        + "the tests already built stay exactly as they are.",
                        "Clear", () =>
                        {
                            Remember(what: "starting again");

                            _board = new MatrixBoard();
                            _legend = new MatrixLegend();
                            Redraw();
                        })),
            };

            /// <summary>
            /// Everything that reorganises the whole grid.
            ///
            /// These were filed under "Start from", whose other items create a new matrix and
            /// discard the current one. Pointing every row at a set, ordering both axes by code,
            /// squaring off the columns and choosing whether the axes are one list all operate on
            /// an existing matrix.
            ///
            /// This menu acts on the whole grid; a line's own menu acts on one line.
            /// </summary>
            private List<Parts.Entry> Arranging()
            {
                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading("All rows and columns"),

                    Parts.Entry.Saying("Match rows to sets", Glyphs.Layers,
                        "Tick the sets; each one finds the row of the same name that is still "
                        + "pointing at nothing", PointRows),

                    Parts.Entry.Saying("Sort by discipline code", Glyphs.Sort,
                        "Both axes in one order and a folder per code, so a block covers one "
                        + "discipline against another", Grouped),

                    // Filling by rule writes into the grid, so it is a bulk action rather than a
                    // setting.
                    Parts.Entry.Saying(
                        _board.Rules.Count == 0
                            ? "Fill by rule\u2026"
                            : Ui.Count(_board.Rules.Count, "fill rule") + "\u2026",
                        Glyphs.Checklist,
                        "\u201cAll services against all architecture, B\u201d - and it stays "
                        + "true as the matrix grows", Rulebook),

                    // Alongside the fill rules rather than in the drawing window: both write
                    // across the whole board from one sentence, where the drawing window only
                    // changes how what is already there is shown.
                    Parts.Entry.Saying(
                        Ui.Count(_board.Scheme.Fields.Count, "field") + "\u2026",
                        Glyphs.Columns,
                        "What each line carries, and whether it is counted down the axis rather "
                        + "than typed line by line", Fields),
                };

                var folders = _board.Folders(rows: true);

                if (folders.Count > 0)
                {
                    items.Add(Parts.Entry.Rule());
                    items.Add(Parts.Entry.Heading(Ui.Count(folders.Count, "folder")));

                    items.Add(Parts.Entry.Saying("Regroup folders", Glyphs.Sort,
                        "Put every folder's members back together, keeping the order they are in",
                        () =>
                        {
                            Remember(what: "tidying the folders");
                            _board.Tidy();
                            Redraw();
                        }));

                    items.Add(Parts.Entry.Act(
                        _shut.Count > 0 ? "Expand all folders" : "Collapse all folders", Glyphs.Layers,
                        () =>
                        {
                            if (_shut.Count > 0) _shut.Clear();
                            else foreach (var one in folders) _shut.Add(one);

                            Redraw();
                        }));
                }

                items.Add(Parts.Entry.Rule());
                items.Add(Parts.Entry.Heading("Rows and columns"));

                // Whether both axes are one list. This is what makes the grid mirror, what makes
                // half of it inactive and what makes the diagonal a self-pair. It is the default,
                // but a matrix of one trade's sets against another's is a real case, and this is
                // where it is set.
                items.Add(Parts.Entry.Saying(
                    _board.Paired
                        ? "Allow rows and columns to differ"
                        : "Keep rows and columns identical",
                    Glyphs.Columns,
                    _board.Paired
                        ? "Adding a discipline adds it to both axes, and removing it takes it "
                          + "off both. Turn this off to add to one axis without the other."
                        : "The axes are kept apart: a row is added without a column, and neither "
                          + "follows the other.",
                    () =>
                    {
                        Remember(what: _board.Paired ? "letting the axes differ"
                                                     : "making the axes one list");

                        _board.Paired = !_board.Paired;

                        // Squared off as it goes back to being one list, because a matrix that
                        // diverged while it was allowed to has to be made whole to be one again.
                        if (_board.Paired) _board.SquareOffRows();

                        Redraw();
                    }));

                if (!_board.Paired)
                    items.Add(Parts.Entry.Act("Copy rows to columns", Glyphs.Columns,
                        () =>
                        {
                            Remember(what: "squaring the columns off to the rows");
                            _board.SquareOffRows();
                            Redraw();
                        }));

                return items;
            }

            /// <summary>
            /// Every setting this screen has, in one menu.
            ///
            /// Previously in three places, none named settings: grid appearance under a strip verb
            /// called "Look", test naming under a menu called "Names and folder", and the test
            /// folder as the last item of that menu.
            ///
            /// Everything here changes how the tool behaves; nothing here edits the matrix.
            /// </summary>
            private List<Parts.Entry> Settings() => new List<Parts.Entry>
            {
                Parts.Entry.Heading("Appearance"),

                Parts.Entry.Saying("Grid, symbols and fields", Glyphs.Palette,
                    "Which half is worked in, what the spine and a ruled-out pair are coloured, "
                    + "and which of a line's fields are shown", Looks),

                Parts.Entry.Rule(),
                Parts.Entry.Heading("How a test is named: " + _pattern),

                Parts.Entry.Act("Edit the pattern", Glyphs.Pencil,
                    () => Parts.Ask(_grid,
                        "How every test is named. " + string.Join(", ",
                            MatrixNaming.Tokens.Select(t => t.Token)) + " are filled in.",
                        _pattern, "Use it", typed =>
                        {
                            _pattern = string.IsNullOrWhiteSpace(typed)
                                ? MatrixNaming.Default : typed.Trim();

                            Touched();
                            Recount();
                        })),

                Parts.Entry.Act("Insert a token", Glyphs.Add,
                    () => Parts.Choose(_grid, "Add to the pattern",
                        MatrixNaming.Tokens.Select(t => t.Title + "  " + t.Token).ToList(),
                        null, null, null, picked =>
                        {
                            var token = MatrixNaming.Tokens.FirstOrDefault(
                                t => picked.EndsWith(t.Token, StringComparison.Ordinal));

                            if (token == null) return;

                            _pattern = (_pattern + " " + token.Token).Trim();

                            Touched();
                            Recount();
                        })),

                Parts.Entry.Act("Reset", Glyphs.Close, () =>
                {
                    _pattern = MatrixNaming.Default;

                    Touched();
                    Recount();
                }),

                Parts.Entry.Rule(),
                Parts.Entry.Heading("Destination"),

                Parts.Entry.Act("Which clash folder they go in: "
                    + (_folder.Length == 0 ? "the root" : _folder), Glyphs.Folder,
                    () => Parts.Ask(_grid, "A clash folder, or nothing for the root", _folder,
                                    "Use it", typed =>
                                    {
                                        _folder = (typed ?? string.Empty).Trim();

                                        Touched();
                                        Recount();
                                    })),
            };

            /// <summary>
            /// Points every unpointed row at the saved set of the same name, in one action.
            ///
            /// Pointing a row individually is a header press, a menu, a picker and a confirm, and
            /// rows are usually named after the sets they represent. The picker is already a
            /// multi-select list, so each ticked set finds the row sharing its name.
            ///
            /// Rows already pointed are left alone, so this can be run again after rows are added.
            /// Rows that could not be matched are reported.
            /// </summary>
            private void PointRows()
            {
                var sets = Sets();

                if (sets.Count == 0)
                {
                    Warned(Ui.Problem("This model has no saved selection or search sets to point "
                                      + "the rows at."));
                    return;
                }

                var waiting = _board.Rows.Where(r => r.Source.IsEmpty).ToList();

                if (waiting.Count == 0)
                {
                    Said(Ui.Note("Every row already points at something."));
                    return;
                }

                Parts.Search(_grid,
                    "Which sets are these " + Ui.Count(waiting.Count, "row") + "?",
                    sets.Select(s => new KeyValuePair<string, int>(s.Trail, s.Members)).ToList(),
                    null, null,
                    picked =>
                    {
                        var wanted = new HashSet<string>(picked, StringComparer.OrdinalIgnoreCase);
                        var pointed = 0;

                        Remember(what: "pointing the rows at saved sets");

                        foreach (var set in sets)
                        {
                            if (!wanted.Contains(set.Trail)) continue;

                            // Matched exactly first, then loosely. Names from a client's
                            // workbook rarely match exactly ("Mechanical Ductwork" against
                            // "MEP_HVA_Ductwork"), and an exact-only match finds nothing.
                            // Compared on letters and digits alone, then on containment.
                            var row = waiting.FirstOrDefault(
                                r => r.Source.IsEmpty
                                     && string.Equals(r.Name, set.Name, StringComparison.OrdinalIgnoreCase))
                                ?? waiting.FirstOrDefault(
                                    r => r.Source.IsEmpty && Alike(r.Name, set.Name));

                            if (row == null) continue;

                            _board.Point(row, set.IsSearch
                                ? MatrixSource.SearchSet(set.Name, set.Folder)
                                : MatrixSource.SelectionSet(set.Name, set.Folder));

                            pointed++;
                        }

                        Redraw();

                        var left = _board.Rows.Where(r => r.Source.IsEmpty).Select(r => r.Name).ToList();

                        if (pointed == 0)
                            Warned(Ui.Note("None of those sets is named after a row that is "
                                           + "still waiting. Point those rows one at a time, "
                                           + "from the row itself."));

                        if (left.Count > 0)
                            Said(Ui.Note("Still pointing at nothing: "
                                         + string.Join(", ", left.Take(8))
                                         + (left.Count > 8
                                             ? " and " + (left.Count - 8).ToString(CultureInfo.InvariantCulture) + " more"
                                             : string.Empty)
                                         + "."),
                                 hold: true);
                    });
            }

            /// <summary>
            /// Whether two names are near enough to be the same discipline.
            ///
            /// Letters and digits only, folded to one case: a set called "MEP_HVA_Ductwork" and
            /// a sheet's "Mechanical Ductwork" share "ductwork", and neither shares anything with
            /// "Foundations". Deliberately blunt - it only ever proposes a pairing that a person
            /// is about to look at, and the rows it cannot match are named rather than guessed at.
            /// </summary>
            /// <param name="one">One name.</param>
            /// <param name="other">The other.</param>
            private static bool Alike(string one, string other)
            {
                var here = Bare(one);
                var there = Bare(other);

                if (here.Length < 3 || there.Length < 3) return false;

                return here.IndexOf(there, StringComparison.Ordinal) >= 0
                       || there.IndexOf(here, StringComparison.Ordinal) >= 0;
            }

            /// <summary>A name as its letters and digits alone, folded to one case.</summary>
            /// <param name="said">The name.</param>
            private static string Bare(string? said)
            {
                var made = new System.Text.StringBuilder();

                foreach (var one in said ?? string.Empty)
                    if (char.IsLetterOrDigit(one)) made.Append(char.ToLowerInvariant(one));

                return made.ToString();
            }

            /// <summary>
            /// Orders both axes by discipline code.
            ///
            /// A block fill is a rectangle, which requires the disciplines it covers to be
            /// adjacent on both axes. The codes are already on the rows and are what test names
            /// are built from, so they are the sort key; the columns follow the rows.
            /// </summary>
            private void Grouped()
            {
                if (_board.Rows.Count < 2) return;

                var order = _board.Rows
                    .OrderBy(r => r.Code.Length == 0 ? "\uffff" : r.Code, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (order.SequenceEqual(_board.Rows))
                {
                    Said(Ui.Note("The rows are already in code order."));
                    return;
                }

                Remember(what: "putting the rows in code order");

                for (var at = 0; at < order.Count; at++) _board.MoveLine(order[at], at);

                // Sorting also creates a folder per code. Sorting alone put the rows in code
                // order without drawing anything to say why; a folder states it and can then be
                // named, collapsed, merged and used in a test name. A line with no code is left
                // unfiled rather than placed in a folder with an empty name.
                var made = 0;

                foreach (var line in _board.Rows.ToList())
                {
                    if (line.Code.Length == 0 || line.Folder.Length > 0) continue;

                    _board.File(line, line.Code);
                    made++;
                }

                _board.Tidy();

                Redraw();

                Did(Ui.Done("Grouped by discipline code, down the side and across the top"
                             + (made == 0
                                 ? ". A block now covers one discipline against another."
                                 : ", in " + Ui.Count(_board.Folders(rows: true).Count, "folder")
                                   + ". Rename one, shut it, or merge two together from the band "
                                   + "beside the names.")));
            }

            /// <summary>
            /// Take a board that came from somewhere else, drawn the way this matrix is drawn.
            ///
            /// Which half is worked in lives on the board as well as in the style, so that undo
            /// and the saved grid keep it - and a board that arrives from a read or an import
            /// carries the default. Every one of the three ways in set the field directly and
            /// none of them said so, so reading a matrix out of the model silently flipped the
            /// drawing back on anybody who had asked for the other triangle.
            /// </summary>
            /// <param name="board">The board that came back.</param>
            private void Taken(MatrixBoard board)
            {
                _board = board;
                _board.UpperIsLive = _style.UpperIsLive;

                _found.Clear();
            }

            /// <summary>What a view is called, on the chooser and in the strip.</summary>
            /// <param name="which">The view.</param>
            private static string ViewNamed(Showing which) =>
                which == Showing.Priority ? "Priority"
                : which == Showing.Results ? "Results"
                : "Symbols";

            /// <summary>The mark that goes with a view.</summary>
            /// <param name="which">The view.</param>
            private static string ViewGlyph(Showing which) =>
                which == Showing.Priority ? Glyphs.Warn
                : which == Showing.Results ? Glyphs.Report
                : Glyphs.Palette;

            /// <summary>Draw the chooser again, so its label is the view that is actually on.</summary>
            private void RedrawView()
            {
                // Prefixed with "Showing:", since a bare "Priority" between two menus of actions
                // reads as another action rather than as the screen's current state.
                var pick = Parts.MenuButton("Showing: " + ViewNamed(_shown), ViewGlyph(_shown),
                                            Views());

                // And lit while it is not on the view somebody would expect to be on. The
                // symbols are the board; the other two are the board turned over, and a screen
                // that has been left turned over is a screen somebody comes back to and reads as
                // the matrix itself.
                if (_shown != Showing.Symbols && pick is Border box)
                {
                    box.Tint(Border.BackgroundProperty, "AccentWash");
                    box.Tint(Border.BorderBrushProperty, "Accent");
                }

                _viewPick.Content = Ui.Loose(pick);
            }

            /// <summary>
            /// The three matrices, and which of them is on.
            ///
            /// Only the three. Which rank a press lays down is the palette's job, because the
            /// palette is what arms the hand - and while the ranks are what is being drawn, they
            /// are what the palette holds.
            /// </summary>
            private List<Parts.Entry> Views()
            {
                var items = new List<Parts.Entry>
                {
                    Parts.Entry.Heading("Show in the cells"),

                    Ticked(Showing.Symbols, "What is checked, and with which tolerance"),
                    Ticked(Showing.Priority, "How urgent each pair is, in the host\u2019s own ranks"),
                    Ticked(Showing.Results, "How many clashes are still open on each pair"),
                };

                return items;
            }

            private Parts.Entry Ticked(Showing which, string why) =>
                Parts.Entry.Saying((which == _shown ? "\u2713  " : "     ") + ViewNamed(which),
                                   ViewGlyph(which), why, () => Show(which));

            /// <summary>
            /// Turn the grid over to one of the three views.
            ///
            /// Only the results view has to go and ask the document anything, and it is the one
            /// that can fail to arrive: an edition with no clash engine has nothing to count,
            /// and a document whose tests never said what they were between cannot be counted
            /// onto a matrix. Either way the view does not change, because a grid that had gone
            /// blank and said why in a notice would have thrown away the picture somebody was
            /// reading in order to tell them it could not draw a different one.
            /// </summary>
            /// <param name="wanted">The view to show.</param>
            private void Show(Showing wanted)
            {
                if (wanted == _shown) return;

                if (wanted != Showing.Results)
                {
                    _shown = wanted;

                    // The counts go with the view. They are a reading of the document at one
                    // moment, and a stale one silently redrawn ten minutes later is worse than
                    // counting again.
                    _counts = MatrixCounts.None;

                    RedrawCells();
                    RedrawStrip();
                    RedrawView();
                    RedrawPalette();
                    RedrawProgress();
                    Pointing();
                    return;
                }

                if (!_session.Clash.IsAvailable)
                {
                    Warned(Ui.Problem("This edition has no clash engine, so there are no results "
                                      + "to count. Everything else on this tab still works."));
                    return;
                }

                MatrixCounts? counted = null;

                Ui.Working("Counting what came back\u2026", job =>
                {
                    var read = new List<ClashTestRead>();
                    var seen = 0;

                    foreach (var test in _session.Clash.Tests())
                    {
                        if (!job.Step(++seen, "tests read")) return;

                        read.Add(new ClashTestRead(test, _session.Clash.Sides(test.Id),
                                                   _session.Clash.Results(test.Id)));
                    }

                    counted = MatrixTally.Of(read);
                });

                if (counted == null) return;

                if (counted.IsEmpty)
                {
                    Warned(Ui.Note("None of the document\u2019s tests says what it is between, so "
                                   + "nothing could be counted onto the matrix."));
                    return;
                }

                _counts = counted;
                _shown = Showing.Results;

                RedrawCells();
                RedrawStrip();
                RedrawView();
                RedrawPalette();
                RedrawProgress();
                Pointing();
            }

            /// <summary>Read the matrix the document is already carrying in its tests.</summary>
            private void FromModel()
            {
                if (!_session.Clash.IsAvailable)
                {
                    Warned(Ui.Problem("This edition has no clash engine, so there are no tests "
                                      + "to read a matrix out of."));
                    return;
                }

                List<ClashTestRead>? held = null;

                Ui.Working("Reading the document's tests\u2026", job =>
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

                    held = read;
                });

                if (held == null) return;

                if (held.Count == 0)
                {
                    Warned(Ui.Note("This document has no clash tests to read a matrix out of."));
                    return;
                }

                // Which tests are used, and how their names are structured, are set by the user.
                // This previously read every test in the document into one grid and named the rows
                // after the saved sets, leaving the discipline codes and packages in the test
                // names unread.
                var reading = MatrixReadWindow.Ask(held, _pattern);

                if (reading == null) return;

                if (!reading.IsUsable)
                {
                    Warned(Ui.Problem(reading.Explain()));
                    return;
                }

                Remember(what: "reading the matrix out of the model's tests");

                Reading(reading);

                Taken(reading.Board);

                _legend = reading.Legend;

                Redraw();

                Did(Ui.Done(reading.Explain()));

                foreach (var warning in reading.Warnings.Take(5)) Said(Ui.Note(warning), hold: true);
            }

            /// <summary>
            /// Open a matrix out of the workbook it lives in.
            ///
            /// The file is asked for first and the layout second, because the press that gets here
            /// says "open a spreadsheet" and the person making it has a file in mind, not a set of
            /// coordinates. The window that follows shows the sheet with its own row and column
            /// numbers and says what it would read while the numbers are still free to change.
            /// </summary>
            private void FromSheet()
            {
                var path = Ui.AskToOpen("Open the matrix", "Excel workbook|*.xlsx;*.xlsm|All files|*.*");

                if (string.IsNullOrWhiteSpace(path)) return;

                try
                {
                    // The mapping is kept between opens. A practice's matrices are laid out the
                    // practice's way, so the second workbook of the afternoon is almost always the
                    // same shape as the first, and being asked the same ten numbers again is a tax
                    // on having answered them once.
                    var read = SheetOrigin.Ask(path, _shape);

                    if (read == null) return;

                    if (!read.IsUsable)
                    {
                        Warned(Ui.Problem(read.Explain()));
                        return;
                    }

                    Remember(what: "opening that spreadsheet");

                    Taken(read.Grid);

                    // The legend the sheet's own symbols ask for, kept where it already says
                    // something. What arrives unexplained arrives as an ordinary hard clash,
                    // visibly, so the reader corrects the two that are not rather than
                    // describing all of them.
                    _legend = _legend.Covering(_board);

                    Redraw();

                    Did(Ui.Done(read.Explain()));

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

            /// <summary>
            /// Runs the tests this matrix has just built and reports the results.
            ///
            /// The matrix previously wrote tests and directed the user to the Clash Manager to run
            /// them, so the screen that knows which pairs exist could not answer what those pairs
            /// were created to answer. The host runs a test by id and returns its results.
            /// </summary>
            /// <param name="built">The lines that were written or changed.</param>
            private void Running(IReadOnlyList<MatrixLineChange> built)
            {
                var ids = built.Where(line => line.TestId != null)
                               .Select(line => line.TestId!)
                               .Distinct()
                               .ToList();

                if (ids.Count == 0)
                {
                    // A line that was just made has no id until the difference is taken again.
                    ids = Compare().Where(line => line.TestId != null && line.Change != MatrixChange.Extra)
                                   .Select(line => line.TestId!)
                                   .Distinct()
                                   .ToList();
                }

                if (ids.Count == 0)
                {
                    Warned(Ui.Note("There is nothing to run yet."));
                    return;
                }

                var ran = 0;
                var refused = 0;

                Ui.Working("Running the matrix's tests...", job =>
                {
                    var at = 0;

                    foreach (var id in ids)
                    {
                        if (!job.Step(++at, "tests")) return;

                        if (_session.Clash.Run(id)) ran++; else refused++;
                    }
                });

                Quiet();

                Said(ran == 0
                    ? Ui.Problem("None of them would run. " + (_session.Clash.LastProblem
                                                              ?? "The host refused."))
                    : Ui.Done(Ui.Count(ran, "test") + " run"
                              + (refused == 0
                                  ? string.Empty
                                  : ", and " + Ui.Count(refused, "more") + " refused")
                              + "."),
                     hold: true);

                if (ran > 0) Found();
            }

            /// <summary>
            /// Reads test results back onto the grid, so each cell reports its open clash count.
            ///
            /// Once the matrix is settled, the question asked of it is where the clashes are. The
            /// counts and open totals are carried on each cell's tooltip and automation name, and
            /// stated in words in the panel.
            /// </summary>
            private void Found()
            {
                if (!_session.Clash.IsAvailable) return;

                _found.Clear();

                Ui.Working("Reading what they found...", job =>
                {
                    var plan = MatrixPlanner.Plan(_board, _legend, _pattern);
                    var byName = new Dictionary<string, MatrixTestPlan>(StringComparer.OrdinalIgnoreCase);

                    foreach (var test in plan.Tests) byName[test.Name] = test;

                    var at = 0;

                    foreach (var test in _session.Clash.Tests())
                    {
                        if (!job.Step(++at, "tests")) return;

                        if (!byName.TryGetValue(test.Name, out var asked)) continue;

                        var results = _session.Clash.Results(test.Id);
                        var open = 0;

                        foreach (var one in results)
                            if (!Settled(one.HostStatus)) open++;

                        _found[Pair(asked.Row, asked.Column)] = (results.Count, open);
                    }
                });

                RepaintAll();
                RedrawInspector();

                var total = 0;

                foreach (var held in _found.Values) total += held.Found;

                Said(total == 0
                    ? Ui.Done("Nothing was found. Every pair the matrix builds is clear.")
                    : Ui.Done(Ui.Count(total, "clash") + " across the matrix. The cells carry "
                              + "their counts, and Look can show them in the grid."),
                     hold: true);
            }

            /// <summary>
            /// What this pair's test found, as a sentence, or nothing when it has not been run.
            /// </summary>
            /// <param name="row">The row.</param>
            /// <param name="column">The column.</param>
            private string Finding(MatrixLine row, MatrixLine column)
            {
                if (_found.Count == 0) return string.Empty;

                if (!_found.TryGetValue(Pair(row, column), out var held)) return string.Empty;

                return held.Found == 0
                    ? ". Run, and clear."
                    : ". " + Ui.Count(held.Found, "clash")
                      + (held.Open == 0
                          ? ", all dealt with."
                          : ", " + held.Open.ToString(CultureInfo.InvariantCulture) + " still open.");
            }

            /// <summary>
            /// What names a pair, whichever way round the grid holds it.
            ///
            /// Not the test's name: a name carries the build's own ordinal and the pattern
            /// somebody may have changed since, so a cell looking itself up by name would miss
            /// as soon as either moved. The two lines are what a pair is.
            /// </summary>
            /// <param name="row">One side.</param>
            /// <param name="column">The other.</param>
            private static string Pair(MatrixLine row, MatrixLine column)
            {
                var one = row.Name ?? string.Empty;
                var other = column.Name ?? string.Empty;

                return string.CompareOrdinal(one, other) <= 0
                    ? one + "\u001f" + other
                    : other + "\u001f" + one;
            }

            /// <summary>Whether a result's status means somebody has dealt with it.</summary>
            /// <param name="status">What the host says the result is.</param>
            private static bool Settled(string? status) =>
                status != null
                && (status.IndexOf("resolved", StringComparison.OrdinalIgnoreCase) >= 0
                    || status.IndexOf("approved", StringComparison.OrdinalIgnoreCase) >= 0);

            /// <summary>
            /// What each pair's test found, by the test's name, or empty when nothing has run.
            /// </summary>
            private readonly Dictionary<string, (int Found, int Open)> _found =
                new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Check every source against what this model actually has.</summary>
            private void Sanity()
            {
                var faults = MatrixSanity.Check(_board, _legend, SetNames(), Models());

                Quiet();

                if (faults.Count == 0)
                {
                    Did(Ui.Done("Every row and column points at something this model has, and "
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

                // Lists the planner's per-cell reasons for skipping a pair. The planner records
                // them cell by cell, but the only trace on screen was the word "skipped" in a
                // count on the strip. This is what explains the difference between the number of
                // tests and the number of cells.
                var plan = MatrixPlanner.Plan(_board, _legend, _pattern);

                if (plan.Skipped.Count == 0) return;

                Said(Ui.Eyebrow("Cells that build no test"), hold: true);

                foreach (var skip in plan.Skipped.Take(20))
                    Said(Ui.Small(skip.Cell + " — " + skip.Why), hold: true);

                if (plan.Skipped.Count > 20)
                    Said(Ui.Small(Ui.Count(plan.Skipped.Count - 20, "more") + " not listed."),
                         hold: true);
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

                var spare = lines.Where(l => l.Change == MatrixChange.Extra && l.TestId != null)
                                 .ToList();

                Said(Ui.Small("Nothing in the document has changed. Building applies only the "
                              + "first two of those; a test no cell asks for is left alone."),
                     hold: true);

                // Offers to delete the test belonging to a pair that has been ruled out.
                // Previously the test kept running, finding clashes and appearing in reports,
                // while the user believed the pair had been retired. Deleting automatically would
                // assume ownership of the whole clash detective; offering is the difference.
                if (spare.Count > 0)
                    Said(Ui.Info(Ui.Count(spare.Count, "test") + " in the document is for a pair "
                                 + "this matrix no longer asks for. They keep running until retired.",
                                 "Retire them", () => Retire(spare)),
                         hold: true);
            }

            /// <summary>
            /// Take out the tests the matrix has stopped asking for, once somebody has said so.
            ///
            /// Never as part of building. A federation carries tests nobody made from this matrix
            /// and a build that swept them up would be a matrix deciding it owns the document -
            /// which is why the difference reports them and stops. This is the same list, named,
            /// counted and asked about, and it is the only path that removes anything.
            /// </summary>
            /// <param name="spare">The lines the difference called extra.</param>
            private void Retire(IReadOnlyList<MatrixLineChange> spare)
            {
                var ids = spare.Where(line => line.TestId != null)
                               .Select(line => line.TestId!)
                               .Distinct()
                               .ToList();

                if (ids.Count == 0) return;

                if (!_session.Clash.CanEdit)
                {
                    Warned(Ui.Problem("This document will not take changes — it is open "
                                      + "read-only, or this seat has no licence to write them."));
                    return;
                }

                Parts.Sure(_grid,
                    Ui.Count(ids.Count, "test") + " will be deleted from this document, with "
                    + "their results and everything written against them. They are the tests for "
                    + "pairs this matrix no longer asks for — a pair ruled out, or a row taken "
                    + "away. Nothing else in the document is touched, and this cannot be undone "
                    + "from here.",
                    "Delete",
                    () =>
                    {
                        var gone = _session.Clash.DeleteTests(ids);

                        Quiet();

                        Said(gone
                            ? Ui.Done(Ui.Count(ids.Count, "test") + " retired. The matrix and the "
                                      + "document say the same thing again.")
                            : Ui.Problem("They could not be deleted: "
                                         + (_session.Clash.LastProblem ?? "the host refused it.")),
                             hold: true);

                        Recount();
                    });
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
                _ => "No change needed",
            };

            /// <summary>
            /// Builds the tests the matrix asks for, excluding those already correct.
            ///
            /// A difference rather than a rebuild. The results are the work: statuses,
            /// assignments, comment trails, groups. Deleting and recreating every test to move one
            /// cell discards all of it, so what is already correct is left untouched.
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

                if (!_session.Clash.CanEdit)
                {
                    Warned(Ui.Problem("This document will not take new tests — it is open "
                        + "read-only, or this seat has no licence to write them. The matrix is "
                        + "saved and exports as it is."));
                    return;
                }

                var faults = MatrixSanity.Check(_board, _legend, SetNames(), Models());
                var stopping = MatrixSanity.Stopping(faults);

                var lines = Compare();

                var making = lines.Where(l => l.Change == MatrixChange.Added
                                              || l.Change == MatrixChange.Altered).ToList();

                if (making.Count == 0)
                {
                    Did(Ui.Done("Nothing to do: the document already holds every test this "
                                 + "matrix asks for."));
                    return;
                }

                var open = 0;

                foreach (var row in _board.Rows)
                foreach (var column in _board.Columns)
                    if (_board.IsKeptHalf(row, column) && _board.At(row, column) == null) open++;

                Parts.Sure(_grid,
                    MatrixDiff.Count(lines, MatrixChange.Added) + " tests to write into this "
                    + "document, and "
                    + MatrixDiff.Count(lines, MatrixChange.Altered) + " already there to change "
                    + "where they stand, keeping their results. "
                    + MatrixDiff.Count(lines, MatrixChange.Unchanged) + " are already right and "
                    + "are left exactly as they are, with their results."
                    + (stopping == 0
                        ? string.Empty
                        : " " + Ui.Count(stopping, "row or column") + " points at something this "
                          + "model does not have, so its cells are skipped — check it first.")
                    + (open == 0
                        ? string.Empty
                        : " " + Ui.Count(open, "pair") + " is still undecided and makes no test either way. "
                          + "Building now builds the rest; build again once they are settled.")
                    + (_folder.Length == 0 ? string.Empty : " They go in " + Quoted(_folder) + "."),
                    "Build",
                    () => Write(making));
            }

            /// <summary>
            /// Applies the difference: creates what is new, and alters what has only changed.
            ///
            /// Every line the difference marked Altered previously went through Create, so
            /// changing one word of the naming pattern on a built matrix wrote a second full set
            /// of tests beside those carrying existing statuses and comments, with no warning.
            ///
            /// A line needing only a rename is renamed. A line whose tolerance or kind has changed
            /// is altered in place, results included. Only a line with no counterpart in the
            /// document is created.
            /// </summary>
            /// <param name="making">The lines to act on.</param>
            private void Write(IReadOnlyList<MatrixLineChange> making)
            {
                var made = 0;
                var altered = 0;
                var refused = new List<string>();

                var turned = 0;

                // The ids of everything this build named, so the project can be told which
                // pattern reads them. A test built from a matrix should never have to be asked
                // about: the pattern that wrote its name is known at the moment it is written.
                var named = new List<string>();

                void Refused(string name, string what)
                {
                    turned++;

                    // Named for the first three and counted for all of them. Three lines out of
                    // two hundred refusals reads as three refusals, which is the difference
                    // between "two tests went wrong" and "nothing was written".
                    if (refused.Count < 3)
                        refused.Add(name + ": " + what + " — "
                                    + (_session.Clash.LastProblem ?? "the host refused it"));
                }

                Ui.Working("Building the matrix…", job =>
                {
                    var at = 0;

                    foreach (var line in making)
                    {
                        if (!job.Step(++at, "tests")) return;

                        if (line.Plan == null) continue;

                        if (line.Change == MatrixChange.Added)
                        {
                            var spec = line.Plan.Spec(_folder.Length == 0 ? null : _folder);

                            var id = _session.Clash.Create(spec);

                            if (id != null)
                            {
                                made++;
                                named.Add(id.Value.Id);
                            }
                            else
                            {
                                Refused(line.Name, "could not be made");
                            }

                            continue;
                        }

                        if (line.TestId == null) continue;

                        var done = true;

                        if (line.Reshapes
                            && !_session.Clash.Retune(line.TestId, line.Plan.Symbol.Tolerance,
                                                      line.Plan.Symbol.Test))
                        {
                            Refused(line.Name, "its tolerance could not be changed");
                            done = false;
                        }

                        // The rename last, so a test that could not be retuned still carries the
                        // name it had while it is wrong, rather than the name of what it is not.
                        if (done && line.Renames
                            && !_session.Clash.RenameTest(line.TestId, line.Plan.Name))
                        {
                            Refused(line.Name, "could not be renamed");
                            done = false;
                        }

                        if (done)
                        {
                            altered++;

                            // A renamed test carries the same pattern as a new one: the name it
                            // now has was written from it.
                            if (line.Renames) named.Add(line.TestId);
                        }
                    }
                });

                Quiet();

                Binding(named);

                if (made + altered > 0)
                {
                    _session.Record(ActivityKind.Regroup,
                                    "built " + Ui.Count(made, "clash test") + " from the matrix"
                                    + (altered == 0
                                        ? string.Empty
                                        : " and changed " + Ui.Count(altered, "more")));

                    Said(Ui.Done(
                        (made == 0 ? string.Empty : Ui.Count(made, "test") + " written")
                        + (made > 0 && altered > 0 ? ", and " : string.Empty)
                        + (altered == 0
                            ? string.Empty
                            : Ui.Count(altered, "test") + " changed in place, results and all")
                        + "."), hold: true);

                    // Offers to run the tests here rather than directing the user to another
                    // tab. The host runs a test by id and this screen holds every id it has just
                    // written.
                    Said(Ui.Info("They are written but not run yet.", "Run them now",
                                 () => Running(making)),
                         hold: true);
                }

                foreach (var problem in refused) Said(Ui.Problem(problem), hold: true);

                if (turned > refused.Count)
                    Said(Ui.Problem(Ui.Count(turned, "test") + " in all was refused."
                                    + (made + altered == 0
                                        ? " Nothing was written."
                                        : string.Empty)),
                         hold: true);

                if (made + altered == 0 && turned == 0)
                    Said(Ui.Problem("Nothing was written."), hold: true);
            }

            /// <summary>
            /// Keep what the read decided about each test's naming.
            ///
            /// The read window is where somebody says which pattern reads which test, one test at
            /// a time over a hundred and sixty of them. Throwing that away at the end of the read
            /// and asking again on the Clash Manager would be asking a question that has just
            /// been answered.
            /// </summary>
            /// <param name="reading">What the read came back with.</param>
            private void Reading(MatrixReading reading)
            {
                if (reading.Bound.Count == 0) return;

                var section = _session.Store.Section(ProjectStore.ClashSection);
                var book = NamingBook.FromJson(section[NamingBook.Key]);

                book.Scheme.Absorb(reading.Board.Scheme);

                // The read's own positions are its own; the project's are the project's, so each
                // pattern is added by text and the binding follows where it landed.
                var where = reading.Patterns.Select(one => book.Add(one)).ToList();

                foreach (var one in reading.Bound)
                {
                    var at = one.Value - 1;

                    if (at >= 0 && at < where.Count) book.Bind(one.Key, where[at]);
                }

                section.Set(NamingBook.Key, book.ToJson() ?? JsonValue.Object());

                if (!_session.Store.Save())
                    Said(Ui.Note("The matrix was read, but this project would not record which "
                                 + "naming pattern reads each test."), hold: true);
            }

            /// <summary>
            /// Tell the project which pattern reads the tests this build just named.
            ///
            /// Without this the Clash Manager would have to ask about every test the matrix
            /// wrote, which is the one case where nobody needs to be asked: the pattern that
            /// produced the name is on this screen. The board's own fields go over with it, so a
            /// pattern naming {rowPhase} is readable on the manager as soon as it is bound.
            /// </summary>
            /// <param name="ids">The tests this build named.</param>
            private void Binding(IReadOnlyList<string> ids)
            {
                if (ids == null || ids.Count == 0) return;

                var section = _session.Store.Section(ProjectStore.ClashSection);
                var book = NamingBook.FromJson(section[NamingBook.Key]);

                book.Scheme.Absorb(_board.Scheme);

                var at = book.Add(_pattern.Length == 0 ? MatrixNaming.Default : _pattern);

                foreach (var id in ids) book.Bind(id, at);

                section.Set(NamingBook.Key, book.ToJson() ?? JsonValue.Object());

                // A failed save is worth a line rather than a dialogue: the tests are written and
                // the naming can be set up again on the manager.
                if (!_session.Store.Save())
                    Said(Ui.Note("The tests were built, but this project would not record which "
                                 + "naming pattern reads them. Set it on the Clash Manager's "
                                 + "naming window."), hold: true);
            }

            // ---------------------------------------------------------------------------
            // Keeping it
            // ---------------------------------------------------------------------------

            /// <summary>Save it, and say so.</summary>
            private void Keep() => Kept(quiet: false);

            /// <summary>Write the matrix into the project file.</summary>
            /// <param name="quiet">
            /// True for a save nobody asked for. It still reports a failure - a matrix that has
            /// silently stopped saving is worse than one that never saved - but it does not
            /// interrupt to say that it worked.
            /// </param>
            private void Kept(bool quiet)
            {
                var json = JsonValue.Object();

                json.Set("board", _board.ToJson());
                json.Set("pattern", _pattern);

                if (_folder.Length > 0) json.Set("folder", _folder);

                json.Set("legend", LegendJson());
                json.Set("style", _style.ToJson());

                try
                {
                    _session.Store.Section(ProjectStore.ClashSection).Set(Slot, json);

                    if (_session.Store.Save())
                    {
                        _saved = DateTime.Now;
                        _unsaved = null;

                        if (!quiet)
                            Did(Ui.Done("Saved. The matrix is here the next time this project "
                                         + "opens."));
                    }
                    else
                    {
                        Trouble(_session.Store.LastSaveProblem
                                ?? "The project file could not be written.");
                    }
                }
                catch (Exception e)
                {
                    Trouble(Ui.Describe(e));
                }

                RedrawStrip();
            }

            /// <summary>
            /// A save did not happen. Say so once, and keep saying so in the strip.
            ///
            /// Once, because the timer tries again in three seconds and a locked project file
            /// would otherwise fill the notices with the same line twenty times a minute.
            /// </summary>
            /// <param name="why">What went wrong.</param>
            private void Trouble(string why)
            {
                var first = _unsaved == null;

                _unsaved = why;

                if (first) Warned(Ui.Problem("The matrix is not being saved: " + why));
            }

            private void Load()
            {
                try
                {
                    var json = _session.Store.Section(ProjectStore.ClashSection)[Slot];

                    if (json == null || json.Kind != JsonKind.Object) return;

                    _board = MatrixBoard.FromJson(json["board"]);

                    var pattern = json["pattern"].AsString();

                    if (!string.IsNullOrWhiteSpace(pattern)) _pattern = pattern!;

                    _folder = json["folder"].AsString() ?? string.Empty;

                    _legend = LegendOf(json["legend"]);
                    _style = MatrixStyle.FromJson(json["style"]);

                    // The board carries the choice too, so undo and the spreadsheet round trip
                    // keep it; the style is what the window edits, and they are held together
                    // here rather than by remembering to set one whenever the other changes.
                    _board.UpperIsLive = _style.UpperIsLive;
                }
                catch (Exception)
                {
                    // A project that will not give up its matrix is an empty grid, which is what
                    // this would have been anyway.
                }
            }

            /// <summary>The two ways a matrix leaves here: for a person, and for a seat.</summary>
            private List<Parts.Entry> Exports()
            {
                var ranked = _board.Ranks().Any();
                var counted = !_counts.IsEmpty;

                var tabs = "the grid and its legend"
                           + (ranked && counted ? ", the priorities and the counts"
                               : ranked ? " and the priorities"
                               : counted ? " and the counts"
                               : string.Empty);

                return new List<Parts.Entry>
                {
                    Parts.Entry.Heading("For review"),

                    Parts.Entry.Saying("Spreadsheet\u2026", Glyphs.Table,
                        "A tab per matrix (" + tabs + "), formatted as shown on this screen",
                        Sheet),

                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("For a seat to run"),

                    Parts.Entry.Saying("Tests for Navisworks\u2026", Glyphs.Clash,
                        "An XML the Clash Detective imports, so the tests are made without "
                        + "opening this tab on that seat", TestsFile),
                };
            }

            /// <summary>
            /// Exports the matrix as a workbook that reproduces its formatting.
            ///
            /// The previous export carried the letters only: folder grouping, symbol colours, the
            /// mirror half and the diagonal were all lost, so the recipient had to interpret the
            /// grid from scratch. It also exported one of the three views; the counts are usually
            /// what a meeting wants.
            /// </summary>
            private void Sheet()
            {
                var path = Ui.AskToSave("Save the matrix", "Excel|*.xlsx", "clash-matrix.xlsx");

                if (string.IsNullOrWhiteSpace(path)) return;

                try
                {
                    System.IO.File.WriteAllBytes(path!,
                        XlsxWriter.Write(MatrixSheets.Book(_board, _legend, _counts, Washes())));

                    Did(Ui.Done("Saved. A tab per matrix, drawn the way this screen draws it, "
                                + "and the legend under them."));
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("It could not be written: " + Ui.Describe(e)));
                }
            }

            /// <summary>
            /// What colour each symbol's cells carry, for anything drawing this board elsewhere.
            ///
            /// A symbol with no colour of its own takes one from where it sits in the legend, and
            /// which colour that is belongs to this screen's palette rather than to the board. So
            /// it is resolved here, once, and handed over - rather than worked out a second time
            /// somewhere that would drift from this one.
            /// </summary>
            private Dictionary<string, string> Washes()
            {
                var washes = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var symbol in _legend.Symbols)
                {
                    if (!(Washed(symbol) is System.Windows.Media.SolidColorBrush solid)) continue;

                    washes[symbol.Symbol] =
                        "#" + solid.Color.R.ToString("X2", CultureInfo.InvariantCulture)
                            + solid.Color.G.ToString("X2", CultureInfo.InvariantCulture)
                            + solid.Color.B.ToString("X2", CultureInfo.InvariantCulture);
                }

                return washes;
            }

            /// <summary>
            /// Exports the matrix as an XML file the Clash Detective can import as tests.
            ///
            /// The matrix is often produced by someone other than the person who will run it, and
            /// the only route to tests was pressing Build inside the model. Exported, the matrix
            /// can be imported on the seat that has the licence.
            /// </summary>
            private void TestsFile()
            {
                var plan = MatrixPlanner.Plan(_board, _legend, _pattern);

                if (plan.Tests.Count == 0)
                {
                    Warned(Ui.Note("Nothing in this matrix becomes a test yet, so there is "
                                   + "nothing to write. Point its rows at saved sets and put a "
                                   + "symbol in some cells."));
                    return;
                }

                var path = Ui.AskToSave("Save the tests", "Clash tests|*.xml",
                                        "clash-tests.xml");

                if (string.IsNullOrWhiteSpace(path)) return;

                var written = MatrixTestsXml.Write(plan, new MatrixTestsXmlOptions
                {
                    SourceFile = System.IO.Path.GetFileName(path),
                    KeepFolders = true,
                });

                if (written.IsEmpty)
                {
                    // Every side of every test is something this file cannot name - which on a
                    // board pointed at whole models is every cell of it, and is worth saying
                    // rather than writing an empty file somebody imports and finds nothing in.
                    Warned(Ui.Problem("None of these tests could be written: this file names a "
                                      + "side by its saved set, and none of these rows points at "
                                      + "one. Point them at sets and it will carry the lot."));
                    return;
                }

                try
                {
                    System.IO.File.WriteAllText(path!, written.Text,
                                                new System.Text.UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    Warned(Ui.Problem("It could not be written: " + Ui.Describe(e)));
                    return;
                }

                Did(Ui.Done("Saved. " + Ui.Count(written.Written, "test")
                            + " for the Clash Detective to import."));

                // Said afterwards and in full, because a file that quietly carried nine tests of
                // a matrix of two hundred is worse than one that refused: the person importing it
                // has no way to know what is not in it.
                foreach (var why in written.Warnings.Take(6))
                    Warned(Ui.Note(why));

                if (written.Warnings.Count > 6)
                    Warned(Ui.Note(Ui.Count(written.Warnings.Count - 6, "more cell")
                                   + " could not be written either."));
            }

            // ---------------------------------------------------------------------------
            // What the document has, asked for without walking anything
            // ---------------------------------------------------------------------------

            /// <summary>One saved set, as the picker needs it.</summary>
            private sealed class SetLine
            {
                internal SetLine(string name, string folder, bool search, int members)
                {
                    Name = name; Folder = folder; IsSearch = search; Members = members;
                }

                internal string Name { get; }

                /// <summary>The folders it sits in, innermost last, or empty at the top.</summary>
                internal string Folder { get; }

                internal bool IsSearch { get; }
                internal int Members { get; }

                /// <summary>The set as somebody reads it: its folders, then its name.</summary>
                internal string Trail =>
                    Folder.Length == 0 ? Name : Folder + "/" + Name;
            }

            /// <summary>
            /// One saved set as the picker lists it: its location, and its kind.
            ///
            /// A search set and a selection set are distinguished. A selection set holds the
            /// elements present when it was saved; a search set holds whatever matches its query
            /// now. Pointing a discipline at the wrong one is the difference between a test that
            /// follows the model and one that stops seeing new work, and the two often appear
            /// under the same name in different folders.
            /// </summary>
            /// <param name="set">The set.</param>
            private static string Labelled(SetLine set) =>
                set.Trail + "  \u00b7  " + (set.IsSearch ? "search set" : "selection set");

            private List<SetLine> Sets()
            {
                try
                {
                    // The document's own list, which it answers instantly. A search set is one
                    // the host holds as a query rather than as a fixed list of items, and the
                    // difference matters: a search set re-runs every time the test does.
                    return _session.Sets()
                        .Select(held => new SetLine(
                            held.Name,
                            held.Folder,
                            held.IsSearch,
                            held.Members))
                        .Where(s => s.Name.Length > 0)

                        // Two sets with the same name in two folders are two sets, and this used
                        // to keep one of them. A document with "MEP" on every level offered one
                        // "MEP" to pick from, so nineteen of the twenty rows a matrix needed
                        // could not be pointed at anything.
                        .GroupBy(s => s.Trail, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderBy(s => s.Trail, StringComparer.CurrentCulture)
                        .ToList();
                }
                catch (Exception)
                {
                    return new List<SetLine>();
                }
            }

            /// <summary>
            /// What the sanity check compares a row's source against.
            ///
            /// Both forms: a row drawn since folders were kept names its set with them, and one
            /// drawn before that names it without. Neither should be reported as missing when
            /// the set is right there.
            /// </summary>
            private IReadOnlyCollection<string> SetNames() =>
                Sets().SelectMany(s => s.Folder.Length == 0
                        ? new[] { s.Name }
                        : new[] { s.Name, s.Trail })
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList();

            private List<string> Models()
            {
                try
                {
                    return _session.Models()
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

            /// <summary>
            /// Discards a confirmation message.
            ///
            /// Twenty-two actions on this screen ended by opening a notice to report success:
            /// cells pasted, rows added, a folder merged. Each pushed the grid down and needed
            /// dismissing, and each reported something the user had just watched happen.
            ///
            /// The grid itself is the confirmation, and the strip above it reports the last save.
            /// The notices carry what the user cannot see: failures, and reports requested by
            /// name.
            ///
            /// Kept as a call rather than removed at each site, so the decision that an action
            /// reports nothing is findable and reversible.
            /// </summary>
            /// <param name="note">What would have been reported.</param>
            private void Did(UIElement note) { _ = note; }

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
            /// The last cell of the header band: add a column.
            ///
            /// Shaped like the headers it continues, so it reads as the next position in the band.
            /// It was previously a plus at the top of a 160px header, aligned with nothing and on
            /// no background, discoverable only by hovering. The row equivalent is now a text
            /// field.
            /// </summary>
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
