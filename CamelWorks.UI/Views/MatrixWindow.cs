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

        /// <summary>
        /// Whether the seat's standing notice has been put away for this session.
        ///
        /// <b>It could not be.</b> A seat with no clash engine is told so every time the tab is
        /// opened, in a band across the top of the window, and there was no way to make it stop -
        /// which on such a seat is every session, for ever, about a fact its owner learned once.
        /// A notice nobody can dismiss is not information, it is furniture.
        ///
        /// Kept for as long as the application runs rather than written down: the fact is about
        /// the seat, and a seat that gains a clash engine is a different Navisworks edition and a
        /// different session.
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
        /// <b>The render harness could only ever photograph the empty screen.</b> A seat with no
        /// document has no board, so every picture taken of this tab was the four cards that
        /// offer to make one - and the grid, which is the whole tab and where every layout fault
        /// reported against it has been, was never in a single shot. Faults in it were found by a
        /// coordinator on a licensed seat and described to me in words, which is the slowest and
        /// least exact way a drawing bug can be reported.
        ///
        /// A board handed in here is not a pretended model. It is the product's own type, the
        /// same object a saved matrix is read back into, and it puts the tab in the state a
        /// coordinator is in for most of their day: a matrix open, being worked on.
        /// </param>
        public static UIElement Surface(ISeat seat, MatrixBoard? drawn)
        {
            if (drawn != null) _board = drawn;

            return new Editor(seat).View;
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

            /// <summary>Which symbol the cursor is currently carrying, so it is drawn once.</summary>
            private string _carrying = "\u0000";

            /// <summary>The strip over the grid: what is armed, where the cursor is, undo.</summary>
            /// <summary>
            /// The strip over the grid, which wraps rather than losing its end.
            ///
            /// A horizontal stack in a docked pane has no width to be too wide for: it simply
            /// draws past the edge. Its content already ended nine pixels short at working size,
            /// so a narrower pane, a longer symbol on the brush or a four-digit test count pushed
            /// undo, redo and - worst - "hide the options" off the right, which is the one control
            /// that gives the grid its width back.
            /// </summary>
            /// <summary>
            /// The band over the grid: what is going on, and what can be done about it.
            ///
            /// <b>Two panels, because they answer to different things.</b> One strip meant every
            /// control sat wherever the sentence in front of it happened to end - so arming a
            /// symbol, moving the cursor or picking a row slid Undo, Redo and the rest sideways
            /// under the hand that was already reaching for them. What the screen is saying
            /// changes; where a button is must not. The state reads from the left, the controls
            /// are held against the right-hand edge, and neither moves the other.
            /// </summary>
            private readonly WrapPanel _strip = new WrapPanel { Orientation = Orientation.Horizontal };

            private readonly WrapPanel _verbs = new WrapPanel { Orientation = Orientation.Horizontal };

            private readonly Border _grid = new Border();

            /// <summary>
            /// The blue button, kept so it can say what stands between here and a build.
            ///
            /// <b>It said "Build the tests" over a matrix with fifty-two pairs nobody had ruled
            /// on and two rows pointing at nothing.</b> Neither is a reason to stop somebody
            /// building - a matrix half decided is a matrix worth building the decided half of -
            /// but both are things they would want to know before they did, and the screen knew
            /// them and said neither.
            /// </summary>
            private readonly System.Windows.Controls.Button _build;

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

            /// <summary>
            /// The pencil beside the plus, kept so it can go grey when there is nothing to change.
            ///
            /// It acts on the symbol that is armed or shown, which is what "the one you mean"
            /// means on this screen - so with nothing armed it is a button with no subject, and
            /// says so by being off rather than by opening a window that explains itself.
            /// </summary>
            private System.Windows.Controls.Button? _editing;

            /// <summary>
            /// What can be done to it, on a strip of its own at the foot of the panel.
            ///
            /// <b>They used to sit wherever the page ended.</b> A row with three pairs decided
            /// put them high, a row with twenty put them low, and a row whose list of what it is
            /// checked against ran long put them off the bottom - so the five things you do to a
            /// row were in a different place on the screen for every row. Pinned above the
            /// palette they are one target, always the same distance from the hand, and the
            /// reading above them can be as long as it likes.
            /// </summary>
            private readonly StackPanel _acts = Ui.Stack();

            private readonly Border _actsPane = new Border();

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

            /// <summary>
            /// The headers, by the line they belong to, so a cell can light its own two up.
            ///
            /// <b>Tracking a row across a scrolling grid is the thing a triangle costs you.</b>
            /// Under a pointer, both ends of the pair say so - which answers "which two is this"
            /// without a tooltip and without reading two long names off two edges of the screen.
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
            /// The options panel, which gives its width back when it is asked to.
            ///
            /// <b>There were two panels, one down each side.</b> Four hundred and fifty pixels
            /// of them on a screen whose whole subject is a grid that never has enough width.
            /// One panel now, and it does not stand aside on its own any more: it holds the
            /// palette, and a window that hides the tools because it got narrow is a window
            /// arguing with the person using it. It goes when somebody says so and not before.
            /// </summary>
            private readonly Border _sidePane = new Border();

            /// <summary>Whether it is open.</summary>
            private bool _sideOpen = true;

            /// <summary>Where the keyboard is, or -1 for nowhere.</summary>
            private int _atRow = -1;
            private int _atColumn = -1;

            /// <summary>
            /// The other corner of the block, or -1 when the cursor is on its own.
            ///
            /// <b>The real work is blocks.</b> "Every MEP row against every structural column is
            /// a clearance" is one decision and thirty cells, and the only sizes this screen had
            /// were one cell, one whole line and the whole grid - so a decision that is neither
            /// was thirty presses, or a line fill and then corrections. Shift and an arrow, or a
            /// shift-click, take the block from the cursor to here; a symbol then goes in all of
            /// it at once.
            /// </summary>
            private int _markRow = -1;
            private int _markColumn = -1;

            /// <summary>The wash over the block, drawn over the cells like the cursor's ring.</summary>
            private readonly Border _range = new Border();

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
            private readonly Stack<Doing> _past = new Stack<Doing>();

            private readonly Stack<Doing> _future = new Stack<Doing>();

            /// <summary>
            /// One thing somebody did, and the matrix as it was before they did it.
            ///
            /// <b>Undo used to say nothing at all.</b> A press, the grid changes, and no word
            /// anywhere about what came back - so on a matrix of five hundred cells the only way
            /// to know whether the right thing was undone was to find the cell again and look at
            /// it. What it was is recorded when it happens, which is the only moment anybody
            /// knows.
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
            /// Saving, on its own, a few seconds after the last thing anybody did.
            ///
            /// <b>An afternoon's matrix was one closed window from gone.</b> The only save was a
            /// line in a menu called "Out", nothing said whether it had ever been used, and
            /// nothing asked on the way out - so a grid a kick-off meeting argued over for two
            /// hours lived in memory until somebody remembered a menu. A matrix is a document,
            /// and a document saves itself.
            ///
            /// Quiet on the way through: a save that announces itself twenty times an afternoon
            /// is a save nobody reads. The strip says when it last happened; only a failure
            /// interrupts.
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

            internal Editor(ISeat session)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));

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

                // <b>The strip reads in the order the work happens.</b> Where a matrix comes
                // from, then what is done to all of it, then how the tool behaves, then keeping
                // and sending it, then the two checks, then the build.
                //
                // Three things had drifted. "Start from" held four ways to make a matrix and
                // four ways to rearrange one, which are opposite acts on one menu - so somebody
                // looking for "put these in order" had no reason to open it. "Save it in this
                // project" was filed under "Out", and out is where work goes when it is over,
                // not where it is kept. And the settings were in three places, none of them
                // called settings: how the grid is drawn was a verb called "Look", how a test
                // is named was a menu called "Names and folder", and where tests are filed was
                // the last line of that menu.
                Add(Parts.MenuButton("Start from", Glyphs.Read, Starting()));
                Add(Parts.MenuButton("Arrange", Glyphs.Sort, Arranging()));
                Add(Parts.MenuButton("Settings", Glyphs.Tune, Settings()));

                Add(Ui.Verb(Glyphs.Save, "Save",
                            "Keep it in this project, so it is here next time this model is "
                            + "opened. It also saves itself as you work.", Keep));

                Add(Ui.Verb(Glyphs.Table, "Export",
                            "The grid and its legend as a spreadsheet, to send to whoever "
                            + "asked", Sheet));

                // <b>The two things to do before building were filed under "Out".</b> Checking
                // every source against the model and seeing what would change are the pre-flight
                // for the blue button beside them - and they were the top two lines of a menu
                // every user reads as "export", which is where things go when the work is over.
                // Beside the button they are for, they are what they are - and they say so in a
                // word, like every other button on this strip.
                Add(Ui.Verb(Glyphs.Verify, "Check",
                            "Check it against the model — every row and column, against what "
                            + "this model actually has", Sanity));

                Add(Ui.Verb(Glyphs.Changes, "Changes",
                            "See what would change — this matrix against the tests already in "
                            + "the document", Difference));

                _build.Margin = new Thickness(8, 0, 0, 0);

                row.Children.Add(Ui.Loose(_build));

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

                // <b>Floating it stopped it pushing the work and started it covering the
                // work.</b> Docked, a notice took its own height off the top of the grid and gave
                // it back when it went, so the cell somebody was aiming at moved out from under
                // the pointer twice for a sentence they did not ask for. Laid over the top-right
                // instead, it stopped moving anything and sat on the words underneath it -
                // squarely across the paragraph explaining what a matrix is, cut off
                // mid-sentence by a box explaining something else.
                //
                // Neither is acceptable and they were never the only two options. A notice here
                // is a standing fact about the seat - no clash engine, no document open, a
                // report asked for by name - and a standing fact belongs in the layout rather
                // than hovering over it. It takes a row of its own above the grid, which it is
                // welcome to, because it appears when the tab is opened and not in the middle of
                // somebody's thirtieth cell. The transient confirmations that used to fire on
                // every press, and that are what made a docked notice unbearable, are gone.
                //
                // <b>Docked to the top, said outright.</b> A DockPanel's default is Dock.Left,
                // so a notice added without one takes its width down the side and leaves the
                // grid whatever is left - which for a sentence this long is about seventy pixels
                // of a window eleven hundred wide. The whole matrix drew into that strip: no
                // headers, no cells, and the inspector squeezed against the right-hand edge. It
                // took a render to see, and nothing else could have: it compiles, it throws
                // nothing, and every test passes over a grid that is not on the screen.
                stack.Children.Add(Ui.Loose(_grid));

                // <b>Undo belongs to the screen, not to the grid.</b> It was bound to the cells'
                // own scroller, so it worked while the pointer had last touched a cell and did
                // nothing at all while somebody was in the inspector, the palette or the box
                // that makes rows - which is exactly where the edits it needs to take back are
                // made. On the bubbling event rather than the preview one, so a text box being
                // typed into still gets its own undo first and keeps it.
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

                // <b>Said, not inherited.</b> Whether the panel is on the screen was written
                // down in one place - the press that toggles it - so until somebody pressed
                // that, the panel's state was whatever a Border happens to default to rather
                // than what this screen believes. A screen that keeps a flag and never states
                // it is a screen that can disagree with itself, and the flag is what the strip
                // reports to the reader.
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
            /// <b>There were two of them, one down each side.</b> Four hundred and fifty pixels
            /// of options on a screen whose whole subject is a grid that never has enough width -
            /// and the two were doing one job between them, because choosing a symbol and saying
            /// what a row is are the same kind of act. Together they cost one panel instead of
            /// two, the grid gets the other side of the window back, and the eye stops crossing
            /// the matrix to get from a symbol to the row it is going in.
            ///
            /// Whatever was last pressed is at the top, where it changes; the palette is
            /// underneath it, where the hand can find it in the same place every time; and how
            /// much is left to decide is at the foot.
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
                var titled = Ui.Eyebrow("Palette");

                titled.Margin = new Thickness(10, 9, 10, 5);
                keys.Children.Add(titled);

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

                // <b>A notice that neither pushes the work nor covers it.</b> Above the grid it
                // took its own height off the matrix and gave it back when it went, so the cell
                // somebody was aiming at moved out from under the pointer twice. Laid over the
                // grid instead it stopped moving anything and sat on the words underneath. Both
                // were rejected, and rightly: the two were never the only options.
                //
                // It goes in the panel on the right, which is open at every moment of every
                // session, is already the answer to "what is this thing", and is not where the
                // work is. Whatever it says, the grid does not move by a pixel.
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
                var bar = new DockPanel { LastChildFill = true };

                _verbs.HorizontalAlignment = HorizontalAlignment.Right;
                _verbs.VerticalAlignment = VerticalAlignment.Center;
                _verbs.Margin = new Thickness(12, 0, 0, 0);

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

                // <b>The white below the last row was the pane, not the sheet.</b> A grid of six
                // rows in a window that holds thirty left two thirds of the screen the same
                // colour as the cells, with no edge anywhere, so the matrix looked like a table
                // that had failed to finish drawing rather than a small matrix. The desk behind
                // it is the app's own background: the sheet now has an edge and stops where it
                // stops, and the space beneath it reads as space.
                _scroll.Tint(Control.BackgroundProperty, "Bg");

                // <b>The bands have no scrollbars and the cells do.</b> A vertical bar takes
                // seventeen pixels off the right of the cells' viewport and nothing off the
                // band's, so the band went on showing a strip of a column whose cells had
                // already been cut off - a header with no rows under it, hard against the right
                // edge. The horizontal bar did the same thing at the bottom to the row names.
                // Whatever the cells lose to a bar, the band beside them gives back.
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
                    if (_board.Rows.Count > 0 && _board.Columns.Count > 0 && Fits()) Relaid();
                });

                Grid.SetRow(_scroll, 1);
                Grid.SetColumn(_scroll, 1);
                frame.Children.Add(Ui.Loose(_scroll));

                _ring.BorderThickness = new Thickness(2);
                _ring.IsHitTestVisible = false;
                _ring.Visibility = Visibility.Collapsed;
                _ring.Tint(Border.BorderBrushProperty, "Accent");

                // Over the cells rather than in them: a block is a shape, and a shape drawn by
                // tinting thirty backgrounds one at a time is thirty repaints and a border that
                // stops at every cell edge. One translucent pane spanning the rows and columns
                // it covers is one element, and the cells underneath keep the fills that say
                // what is in them.
                // <b>The block and the cursor were the same mark, and the cursor is always on a
                // corner of the block.</b> Two 2px accent rectangles, one inside the other and
                // sharing an edge, drew as one - so while a block was held there was no way to
                // see which end shift and the arrows would move. The block is a dashed edge and
                // a wash; the cursor stays the solid ring it has always been, and now reads on
                // top of it.
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
            /// Cell sizes, fixed, so nothing measures itself out of existence.
            ///
            /// <b>A column is as wide as a symbol, not as wide as a name.</b> The names went
            /// across the top and were cut off anyway - "WSC_Process st..." - while every column
            /// paid a hundred pixels for a letter. Stood on their end they cost height once, and
            /// every column of a twenty-two-discipline matrix fits on a screen instead of six.
            /// </summary>
            private const double LeastWide = 38;

            /// <summary>
            /// How big a cell is drawn: the smallest that reads, grown to fill what is there.
            ///
            /// <b>A six-row matrix in a window that holds thirty left two thirds of it empty.</b>
            /// Colouring that space did not use it. A grid that fits with room to spare has no
            /// reason to be drawn at the size a grid that does not fit has to be, so the cells
            /// take the room: bigger targets to click, more of the name in each header, and no
            /// dead half-window under a small matrix. Once it stops fitting the cells go back to
            /// the smallest size that still reads, and the sheet scrolls as before.
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
            /// How big the letter in a cell is drawn, which follows how big the cell is.
            ///
            /// <b>The cells grew and nothing in them did.</b> A thirteen-pixel letter centred in
            /// a hundred-and-ten by sixty-two cell is a small matrix stretched, not a big one:
            /// the room bought a larger target to click and nothing to look at. Between the
            /// smallest cell and the largest the letter goes from thirteen to twenty, which is
            /// the same letter at the size the cell is asking for.
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
            /// Work out the cell size for the room there is, and say whether it changed.
            ///
            /// <b>Growing only when the smallest size already fits</b> is what keeps this from
            /// oscillating. Filled exactly to the viewport a scrollbar appears, which takes the
            /// room away, which shrinks the cells, which takes the scrollbar away again. Every
            /// grown size is worked out from a viewport already known to be big enough and is
            /// rounded down, so the total always lands inside it and no bar is ever summoned.
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
            }

            /// <summary>
            /// How wide the names get, measured, before the gutters beside them.
            ///
            /// <b>A hundred and fifty pixels was a guess, and a guess truncates.</b> It cut
            /// "ARC_Ceilings and Soffits" down to "ARC_Ceilings and Sof...", and a discipline
            /// whose name is not readable is a row nobody can check the symbols against - which
            /// is the whole of what the band down the side is for. The number gutter and the code
            /// gutter beside it have been measured for exactly this reason since the day one of
            /// them cut "ARC" to "A...".
            ///
            /// Floored so a matrix of short names still has a band worth pointing at, and capped
            /// so one absurd name cannot take a third of the window from the grid it labels. The
            /// mark before the name is part of what a name costs, and it is reserved for the
            /// whole axis whenever any row needs it, so this asks for it on the same terms.
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
            /// The number gutter and the code gutter, each as wide as its longest entry.
            ///
            /// <b>Thirty-six pixels was a guess and it cut "ARC" down to "A...".</b> Every one of
            /// these is a short string whose length is known, so none of them is worth guessing
            /// at: measured, "01" costs what "01" costs and a four-digit number still fits.
            /// </summary>
            private double NumberWide => Widest(_board.Rows, 11, false) + 12;

            private double CodeWide => Widest(_board.Rows, 10, true, code: true) + 18;

            /// <summary>
            /// The shell a row header is built in: its edge, and the gutters it lays out across.
            ///
            /// <b>One declaration, built and measured.</b> A header used to be described twice -
            /// once here, where it is put together, and once again in a formula that worked out
            /// how wide the band holding it should be. Two descriptions of one box is a bug
            /// waiting for somebody to add a gutter to one of them, and a compiler cannot see it,
            /// because both are perfectly good arithmetic. So the band asks the header instead:
            /// this is the only place that says what a row header is made of, and
            /// <see cref="HeaderWide"/> measures one to find out how wide that came to.
            ///
            /// The number gutter and the code gutter are each as wide as their longest entry -
            /// thirty-six pixels was a guess and it cut "ARC" down to "A..." - and a gutter
            /// nothing is using is not drawn at all, so a matrix without numbers or codes is as
            /// narrow as it ever was.
            ///
            /// <b>The number used to be glued to the front of the name.</b> "04 ARC_Walls" is
            /// one string to a reader and two facts to the job, and run down twenty rows the
            /// names started at four different places because the numbers were not all the same
            /// width. Its own column lines them up and gives the name back its own left edge -
            /// and the code, which the test names are built out of, was not on the screen at all.
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
            /// Whether the numbers are worth a column of their own.
            ///
            /// <b>Not whether they exist - whether they say anything.</b> Rows made from saved
            /// sets are numbered 01, 02, 03 down the list, which is what the index gutter beside
            /// them already says, so the matrix paid thirty pixels a row to print the same number
            /// twice twenty-four pixels apart. The moment one of them is 19 against an ordinal of
            /// 20 - a row taken out, a package renumbered - they are saying two different things
            /// and the column comes back.
            /// </summary>
            /// <param name="lines">The rows or the columns.</param>
            /// <summary>Whether the number gutter is drawn, which the style can insist on either way.</summary>
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
            /// How tall the column band is: the longest name in it, and what sits under them.
            ///
            /// <b>A hundred and thirty pixels was a guess, and a guess is white space.</b> The
            /// names are turned and they end at the grid, so a band taller than the longest of
            /// them is empty across the whole width of the screen - forty or sixty pixels of
            /// nothing over every column, every time, on a matrix whose names happen to be
            /// short. Measured, the band is as tall as it has to be and no taller: nine
            /// characters cost nine characters. It is floored so a one-word matrix still reads
            /// as a header, and capped so one absurd name cannot take a third of the window.
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
            /// The shell a column header is built in: its padding, and the rows it stacks.
            ///
            /// <b>One declaration, built and measured.</b> This box used to be described twice.
            /// Here, where it is put together with six pixels of padding over its words and four
            /// under them - and again in a formula that worked out how tall the band holding it
            /// should be. The formula did not know the padding existed, so the band came out ten
            /// pixels shorter than the thing standing in it and every name in every column was
            /// cut off by exactly that. Nothing in a compiler can see two descriptions of one box
            /// disagree: both are good arithmetic, and only a picture shows which is right.
            ///
            /// So there is one description. The header is built from this shell and the band asks
            /// <see cref="HeaderHigh"/>, which measures the same shell. A row added here is a
            /// taller band with nothing else to remember.
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

                // <b>The name's own row, as long as the name is allowed to run.</b> Star would
                // have been the obvious thing and it cannot work here: a turned name is a text
                // block rotated for the drawing only, so what it asks the layout for is one line
                // tall and as wide as the words - the height it actually occupies on the screen
                // is a thing only this code knows. It is stated once, here, and measured
                // everywhere else.
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
            /// How tall the column band turned out to be, asked of a header rather than guessed.
            ///
            /// <b>A hundred and thirty pixels was a guess, and a guess is white space.</b> The
            /// names are turned and they end at the grid, so a band taller than the longest of
            /// them is empty across the whole width of the screen - forty or sixty pixels of
            /// nothing over every column, every time, on a matrix whose names happen to be short.
            /// Measured, the band is as tall as it has to be and no taller: nine characters cost
            /// nine characters. It is floored so a one-word matrix still reads as a header, and
            /// capped so one absurd name cannot take a third of the window - both in
            /// <see cref="NameRun"/>, which is the only number in it.
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

                    // <b>Room to spare, not room exactly.</b> Measured against the column to the
                    // pixel, a name that just fits is a name drawn hard against both edges of its
                    // cell - and one measured a shade short of what it draws at is a name with
                    // its tail cut off. A column narrower than this is not somewhere a horizontal
                    // name belongs anyway; below it they turn, which is what the turning is for.
                    if (CellWide < 64) return false;

                    foreach (var line in _board.Columns)
                        if (Measured(line.Name) + 18 > CellWide) return false;

                    return true;
                }
            }

            // <b>What sits above a column's name is reserved for all of them or for none.</b>
            // Sized per line, a column without a code got its fifteen pixels back and its name
            // started fifteen pixels higher than its neighbours' - precisely the ragged edge the
            // turning was meant to fix. So the rows are the same in every header, which is why
            // ColumnShell adds them for the whole board rather than for one line.
            //
            // There used to be a property here that added those three up, and its only reader was
            // a formula working out the band's height a second time. Both are gone: the shell
            // declares the rows and the band measures the shell, so there is nothing left for a
            // sum of them to disagree with.

            /// <summary>
            /// How far down from the top of a column header the number, the code and the mark
            /// reach.
            ///
            /// <b>A line of type, not the length of a word.</b> They were measured to the longest
            /// number and the longest code because they were turned, so their length ran down the
            /// column; upright they are one line each, whatever they say, and a matrix whose codes
            /// are four letters stops paying forty pixels off the top of every column for it.
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
            /// How tall one line of type is, at the size a header draws it.
            ///
            /// <b>Measured, because the last guess at it was four pixels short.</b> A header's
            /// height was a constant that had been read off a screenshot, and a line of eleven
            /// point type is not eleven pixels tall - so every upright name was drawn into a
            /// space too small for it and lost its feet. The ruler that already measures how
            /// wide a name is measures how tall a line is, and neither is guessed again.
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
            /// How wide the index gutter down the left is, and how tall the one across.
            ///
            /// <b>Twenty pixels, and a click in it writes twenty-two cells.</b> The band beside
            /// it is two hundred pixels of header that only selects, so the smallest target on
            /// the screen was the one with the largest consequence, flush against the largest
            /// target with the smallest. Twenty-four is a target, and the gutter is drawn on its
            /// own ground so the eye can see where one band stops and the other starts.
            /// </summary>
            private const double IndexWide = 26;
            private const double IndexHigh = 24;

            /// <summary>
            /// How much of each band a folder's own label takes, when there are any.
            ///
            /// <b>Perpendicular to the names it gathers.</b> A row's name reads across, so the
            /// folder holding eight rows reads down the band beside them, its length spanning
            /// exactly the rows it holds. A column's name reads down, so its folder reads across
            /// the top of them. Either way the label runs the length of what it covers, which is
            /// the whole of how a band says "these ones, and not those".
            ///
            /// Zero when nothing is filed, so a matrix that has never used a folder pays no width
            /// for the idea.
            /// </summary>
            /// <b>Wide enough for the letters, not for the idea of them.</b> Twenty-four pixels
            /// is narrower than a line of ten point type is tall, so a name turned into it is cut
            /// off along its whole length rather than at its ends - it reads as a smudge with two
            /// letters showing. The strip is as wide as the type in it needs, measured, plus the
            /// room either side of it.
            private double FolderWide =>
                _board.Folders(rows: true).Count == 0 ? 0 : LineHigh(10, bold: true) + 10;

            private double FolderHigh =>
                _board.Folders(rows: false).Count == 0 ? 0 : LineHigh(10, bold: true) + 8;

            // <b>How big the frozen frame is, is nobody's arithmetic any more.</b> The corner,
            // the band across the top and the band down the side have to agree to the pixel or
            // the headers stop sitting over the cells they name - and four places used to work
            // that sum out. Adding a strip for the folders taught the two that redraw and left
            // the two built once holding the old sum, so the frame disagreed with itself by
            // exactly the band: the row names clipped off the side, the turned column names
            // sliced, the corner a third of its size and nothing lining up with anything.
            //
            // There is no sum to get wrong now. The frame is a Grid whose first row and first
            // column are auto, so it is as tall as the band across the top and as wide as the
            // band down the side, worked out by the layout from what those bands actually
            // contain. A part added to either band is a bigger frame with nothing to be told.

            /// <summary>The folders shut, by name. Both axes at once, because a folder is one thing.</summary>
            private readonly HashSet<string> _shut =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Whether a line is inside a folder somebody has shut.</summary>
            /// <param name="line">The row or column.</param>
            private bool Hidden(MatrixLine line) =>
                line.Folder.Length > 0 && _shut.Contains(line.Folder) && !Folded(line);

            /// <summary>
            /// The one line a shut folder collapses to, and the line that speaks for it.
            ///
            /// <b>Every member given no height is a folder with no height, and a folder with no
            /// height cannot be pressed to open it again.</b> Shutting one made it vanish - band,
            /// name, chevron and all - and the only way back was undo, which does not take back a
            /// drawing state. A drawer that shuts has to leave its handle out.
            ///
            /// So the first member keeps its place and carries the folder on it: the name across
            /// the row, where a row's name goes, at the width a row header has - rather than
            /// turned inside a band twenty-four pixels wide and thirty tall, which is a name with
            /// three letters of it showing. Across the top the same line keeps its column and the
            /// name stays turned, because a column header is tall and that is where the room is.
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

                // <b>Shut is a drawing state, not a change to the matrix.</b> The lines are still
                // there, still in the same order, still carrying their symbols - they are given
                // no height and no width, so every index, every cell and every count stays what
                // it was and nothing that reads them has to learn about folders. Only the
                // keyboard needs telling, so the cursor steps over what it cannot see.
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
                _cells.Clear();

                var bare = _board.Rows.Count == 0 && _board.Columns.Count == 0;

                // <b>Twenty rows and nothing across the top is a dead end that looked like a
                // bug.</b> The typing box makes rows, so a matrix typed out by hand arrives here
                // with a column of names and no grid at all: no cells, no keyboard, no progress.
                // The one press that fixes it was the fifth line of a menu called "Start from",
                // beside four entries about where a matrix comes from and one that empties it.
                // It is on the screen now, where the grid would be.
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

                // <b>One shape for one act, at both ends of it.</b> Past the last column sat a
                // plus and under the last row a box you typed a name into - two different-looking
                // controls for the same thing, learned separately, and on a mirrored board one of
                // them a detour to the other. Both ends are the plus now, which is the one that
                // belongs in a grid: a cell shaped like the cells it continues, reading as the
                // next place rather than as a form somebody left in the frame.
                //
                // The name is typed where every other thing about a line is said - the panel on
                // the right, which is already open and already the answer to "what is this row".
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
                block.Tint(Border.BackgroundProperty, shut ? "Nav" : "Card2");

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

                // <b>Dropping a line on a folder files it.</b> The bands already carry a line
                // from one place in the order to another; a folder is a place in the order too,
                // and a hand that has just dragged a row past a band expects the band to take it.
                // Either axis, because the folder is one thing across both.
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
                    if (string.Equals(carried.Folder, folder, StringComparison.OrdinalIgnoreCase))
                        return;

                    Remember(what: "filing " + Quoted(carried.Titled) + " in " + Quoted(folder));

                    _board.File(carried, folder);
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

            /// <summary>The cells themselves, which are all that scrolls.</summary>
            private void RedrawCells()
            {
                _sheet.Children.Clear();
                _sheet.ColumnDefinitions.Clear();
                _sheet.RowDefinitions.Clear();

                _sheet.HorizontalAlignment = HorizontalAlignment.Left;
                _sheet.VerticalAlignment = VerticalAlignment.Top;

                // <b>The last row could not be scrolled clear of the horizontal bar.</b> The bar
                // is drawn inside the viewport and takes its seventeen pixels off the bottom of
                // it, so a sheet whose content ends exactly at its own last row has nowhere left
                // to scroll to: wound fully down, the row nearest the bar is still sitting under
                // it, cut in half. It is the box that makes rows that ends up there on a matrix
                // of any size, which is the one thing on that band somebody needs to reach.
                //
                // A bar's worth of room past the end, always. The cells and the band beside them
                // are slid by the same offset, so the extra range carries both: the row names
                // and the typing box come up with the cells rather than lagging a bar behind
                // them. Kept even when no bar is showing, because a grid wants a little space
                // after its last row anyway, and a margin that appears and disappears with the
                // bar is a layout pass that can ask for itself again.
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

                    // <b>A shut folder's line is the folder, not one of its members.</b> Drawing
                    // that member's own cells there would put one discipline's decisions on a
                    // line labelled with the folder's name, which is eight disciplines - so what
                    // sits across it is a closed strip, and no symbol on it is anybody's.
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

                // <b>The corner follows; it does not decide.</b> It sits in the same auto row
                // and auto column of the frame as the two bands, so whichever of the three asks
                // for the most room settles the size of all three - and the one whose size
                // matters is the headers, because they are the thing with contents that have to
                // fit over the cells they name. Its contents go in a panel that asks for
                // nothing, so a search box and a legend cannot quietly become the reason the
                // band across the top is taller than any header in it.
                corner.ClipToBounds = true;

                corner.Tint(Border.BorderBrushProperty, "Bd");
                corner.Tint(Border.BackgroundProperty, "Pane");

                var inside = new DockPanel { LastChildFill = false };

                // <b>The largest empty space on the screen, holding two lines of eight words.</b>
                // A hundred and ninety by two hundred and thirty pixels of corner, and the thing
                // a matrix of forty rows most needs - a way of getting to one of them by name -
                // did not exist anywhere. It does now, in the room that was already there.
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
            /// <b>It used to open a menu of fourteen lines</b>, four of which opened a dialogue
            /// of their own. The inspector says all of it at once and lets it be edited where it
            /// stands, so pressing a row is now how you read one as well as how you change it.
            /// The menu is still on the right button, where a menu belongs.
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
                head.Tint(Border.BackgroundProperty,
                          ReferenceEquals(_picked, line) && _pickedRows ? "NavActive" : "Pane");

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

                // <b>A mark on every row marked nothing.</b> Twenty-two identical glyphs down
                // the left edge said "this one is normal" twenty-two times, and the one that
                // meant something - a row pointing at nothing - was a shape change inside a
                // column of shapes. The warning is drawn and the ordinary row is not; the room
                // is reserved for the whole axis whenever any row needs it, so the names still
                // start on one line.
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
                head.Tint(Border.BackgroundProperty,
                          ReferenceEquals(_picked, line) && !_pickedRows ? "NavActive" : "Pane");

                // <b>A column header is a row header turned, and it has to turn the same way
                // round.</b> Everything in it is stood on its end - a turned name over an
                // upright number was two writing directions in one cell forty pixels wide - and
                // the pieces come in the order the row puts them in. Turned clockwise, what is
                // leftmost in the row is uppermost in the column, so both read number, code,
                // name: across in the row, down in the column. Turned the other way they came
                // out backwards against the rows they belong to, which is what this was.
                var stacked = (Grid)head.Child;

                var row = 0;

                // <b>Two characters and three do not need turning.</b> The name does - a name
                // turned is the whole reason twenty-two columns fit on a screen - but "10" and
                // "ME" fit upright inside a column thirty-eight pixels wide, which the row header
                // proves by drawing them that way. Turned, they cost legibility for a symmetry
                // nobody perceives: a reader tips their head to read a number they could have
                // read straight.
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

                // <b>Centred across the column, flush along it.</b> Two axes, and they do not
                // want the same answer. Across the column - the direction the letters are thick
                // in - everything in a header is centred, which is what the letter above, the
                // number and the code all do. Along the column, a name centred floats: twenty-two
                // of them at twenty-two different lengths start and end in twenty-two different
                // places, and the top edge of the grid stops being an edge. Flushed to the end
                // that meets the grid they all finish on one line, and what ellipsises is the far
                // end rather than the end that matters.
                // <b>Turning it is the answer to a width problem, and a wide column has not got
                // one.</b> At six disciplines a column is a hundred and ten pixels and the
                // longest name fits upright in it - so the band was spending a hundred and
                // seventy-seven pixels of height, and asking the reader to tilt their head, to
                // solve a problem that had gone away. Upright whenever the names fit, turned
                // whenever they do not.
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
            /// One piece of a column header, stood on its end and reading up the column.
            ///
            /// <b>Wrapping did not work</b>, and neither did turning it and letting the layout
            /// work the size out. A column had to be a hundred pixels wide to hold two words of
            /// a name, and the names were cut off anyway - "WSC_Process st...". Turned through
            /// ninety degrees with a LayoutTransform they came out cut off after three letters
            /// instead: a rotated child is measured against the inverse-transformed space it is
            /// given, and inside a cell forty pixels wide that arithmetic ends with the name's
            /// length limited by the column's width. Which is the constraint the rotation was
            /// there to escape. So the text is given the box it is to occupy - as long up the
            /// column as it is allowed, as thick as the column is wide - and turned about its
            /// own centre for the drawing only.
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

                    // <b>How long it may run, not how long it is.</b> Given this as a Width the
                    // block asked its parent for a box longer than the column is wide, and WPF
                    // cut it down to the slot it did not fit in - so a name reserved a hundred
                    // and sixty pixels of band, drew five characters of itself in the middle of
                    // it, and left the rest empty above and below. As a maximum it is the cap
                    // the design wants and nothing overflows anything.
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

                    // <b>Turned in the layout, not only in the drawing.</b> A RenderTransform
                    // moves pixels and tells the layout nothing, so the block still asked for a
                    // box as long as its words and as thick as one line - lying on its side
                    // across a column thirty-eight pixels wide. WPF clips a child to the slot it
                    // was given, so what got rotated was a name already cut down to the width of
                    // its own column: five characters out of the middle of every heading, in a
                    // band with room for all of them.
                    //
                    // A LayoutTransform is measured through: the block is offered the slot turned
                    // the other way round, so a column thirty-eight wide and a hundred and sixty
                    // tall offers it a hundred and sixty to write in and thirty-eight to be thick
                    // in. Which is the way round it is actually drawn, so what it asks for is
                    // what it gets and there is nothing left over to clip.
                    //
                    // Counter-clockwise, reading upwards, which is what a spreadsheet does and
                    // what anybody who has read a matrix before expects. Clockwise it was legible
                    // but upside down against every other tool they use - and a box's left-hand
                    // edge lands at the bottom this way round, so a name set to its left starts
                    // at the grid and runs up away from it, and what ellipsises is the far end
                    // rather than the end that matters.
                    LayoutTransform = new System.Windows.Media.RotateTransform(-90),
                };

            /// <summary>
            /// Make one part of the grid reachable without a mouse, and nameable by a reader.
            ///
            /// <b>Every header, every gutter mark and every key on the palette was a Border with
            /// a mouse handler.</b> A Border is not focusable, takes no part in the tab order and
            /// reports no name, so the keyboard model this screen is proud of stopped dead at the
            /// edge of the cells: you could type symbols into the grid all afternoon and never
            /// reach the palette to change which one, or press a row to say what it points at.
            /// And a narrator had nothing to read out but the tips, which need a hover the
            /// keyboard cannot produce.
            /// </summary>
            /// <param name="on">The part.</param>
            /// <param name="name">What it is, said as a reader would say it.</param>
            /// <param name="onPress">What Space or Return does, which is what a click does.</param>
            private static void Reachable(UIElement on, string name, Action? onPress)
            {
                System.Windows.Automation.AutomationProperties.SetName(on, name);

                if (onPress == null) return;

                on.Focusable = true;

                // <b>A Border has no theme style, so it has no focus visual.</b> Every header,
                // every gutter mark and every palette key was made reachable by the keyboard and
                // then drew nothing at all to say the keyboard was on it - so the model this
                // screen is proudest of could not be used without a mouse to remember where you
                // had got to.
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
                    Ui.Safely(() => Pick(line, rows));
                };

                // <b>Renaming was reachable from exactly one place.</b> The name of a row lived
                // in the inspector and nowhere else, so a panel that is shut - or, as it turned
                // out, one that is not on the screen at all - takes the only way of naming a row
                // with it, and the first row of a new matrix stays called "New row" with nothing
                // a person can press. The name is on the header; the header is what somebody
                // aims at to change it. It is how a test folder is already renamed here.
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

                Lighting(null);
                Editable();

                RedrawAcross();
                RedrawDown();
                RedrawInspector();
            }

            /// <summary>One cell: the symbol in it, and every way of changing it.</summary>
            private Border Cell(MatrixLine row, MatrixLine column, int down, int across)
            {
                // <b>No cursor of its own.</b> What the pointer looks like over a cell depends
                // on what is armed, which changes without the cells being rebuilt - so it is said
                // once, on the sheet they all sit in, and forced from there. Said here it would
                // be a thousand assignments that all have to be revisited on every press of the
                // palette, and were not.
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
            /// Put the cursor on one cell, the keyboard on the grid, and the cell in the
            /// inspector.
            ///
            /// <b>The inspector shows what was last touched, and a cell is a touch.</b> It did
            /// not let go of a row or a symbol when one was pressed, and nothing on the screen
            /// released either - so the first time somebody pressed a row header, the inspector
            /// stopped answering about cells for the rest of the session.
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
                if (_brush == null) return;

                var laying = Laying;

                // Dragging crosses the same cell many times; only a change is worth a repaint.
                if (string.Equals(_board.At(row, column), laying, StringComparison.Ordinal)) return;

                _board.Set(row, column, laying);
                Repaint(row, column);
            }

            /// <summary>
            /// Put the armed symbol down the empty cells of one row or column.
            ///
            /// <b>Empties only, and it says how many.</b> One click on a number used to overwrite
            /// every considered decision in that line with whatever happened to be on the brush,
            /// silently, and a line somebody had spent ten minutes on was gone with nothing on
            /// the screen to say it had happened. Filling what has not been decided is the thing
            /// people actually want from a gutter, and it cannot destroy anything.
            ///
            /// With nothing on the brush it is not a dead click: it puts the line in the
            /// inspector, which is what pressing a header does everywhere else.
            /// </summary>
            /// <param name="line">The row or the column.</param>
            /// <param name="rows">True for a row, false for a column.</param>
            private void Sweep(MatrixLine line, bool rows)
            {
                if (_brush == null)
                {
                    Pick(line, rows);
                    return;
                }

                Remember(what: "filling that " + (rows ? "row" : "column"));

                var laid = 0;

                foreach (var other in rows ? (IReadOnlyList<MatrixLine>)_board.Columns : _board.Rows)
                {
                    var row = rows ? line : other;
                    var column = rows ? other : line;

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
            /// Say what a bulk act did, in the notices, with what to press to take it back.
            ///
            /// <b>Not a confirmation.</b> A box asking "are you sure" before a fill is clicked
            /// through by lunchtime and teaches nothing; a count afterwards is read, believed,
            /// and can be acted on. The one act that still asks first is filling the whole grid,
            /// because that one is not a mistake anybody makes twice.
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

            /// <summary>Arm a symbol, or put the one already armed down.</summary>
            /// <param name="symbol">The symbol, or null for none.</param>
            /// <summary>
            /// Arm a symbol, or put the one already armed down.
            ///
            /// <b>Arming does not seize the panel any more.</b> A palette press used to arm the
            /// symbol and put it in the inspector, which closed whatever row page you were on -
            /// and the row page carries a fill button that is disabled until a brush is armed and
            /// whose tip tells you to go and arm one. So the button taught the one order in which
            /// it could not be used. It also lit the grid, so picking up a pencil greyed out
            /// ninety per cent of the cells with nothing on screen to say why.
            ///
            /// The two are separate facts. The brush is a tool in the hand; what the panel shows
            /// is what you last asked about. The pencil beside the plus opens the armed symbol
            /// when you want to see it.
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
            /// Put what is armed on the cursor, so the pointer says what a click will do.
            ///
            /// <b>The strip said it, forty rows away from the hand.</b> A click in the grid is
            /// either "select this cell" or "paint it with C", and which of the two it is was
            /// written along the top of the window - out of the eye's way at the moment it
            /// matters, and read once a session if at all. The rest of the time an armed brush
            /// was discovered by painting a cell nobody meant to paint.
            ///
            /// Now the pointer carries the symbol. It says both things at once and neither has to
            /// be looked up: that something is armed, and which one - so arming C and believing
            /// you armed B is a thing seen before the first cell rather than after the thirtieth.
            ///
            /// <b>Said once, on the sheet.</b> The cells do not carry a cursor of their own, and
            /// this forces the sheet's over all of them - because arming a symbol does not rebuild
            /// the cells, and a cursor written into each of a thousand of them would go on saying
            /// what was armed the last time they were drawn.
            /// </summary>
            private void Pointing()
            {
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
                var mirrored = !_board.IsDrawnHalf(row, column);

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
                    // <b>The echo is back, quietly - but not so quietly it is not there.</b>
                    // Drawn at full strength it was four hundred and eighty letters on a screen
                    // holding two hundred and thirty-one decisions, every one of them reading as
                    // something to check. Drawn not at all - which is what this was - a row could
                    // no longer be read across, and reading a row is what a matrix is for in
                    // front of a client. It was then drawn in the faintest ink the palette has,
                    // at fifty-five per cent on top of that: two to one against the sheet, which
                    // is a mark that survives neither a projector nor a laptop at an angle nor a
                    // printed page - so the argument for it and the drawing of it disagreed. The
                    // ink at ninety per cent is a shade over three to one on the sheet:
                    // subordinate to the semibold letters of the live half without being
                    // invisible. Measured, not guessed - eighty-five came out at 2.88 against
                    // the pane the sheet sits on, which is under the floor the argument is about.
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

                    // <b>The two states worth echoing are the two it threw away.</b> Reading a
                    // row across the diagonal is the whole argument for the echo, and it showed
                    // a turned-off symbol and a symbol nothing explains as ordinary decisions -
                    // so the half of the line that lives over there read as handled when it was
                    // not. The strike and the red come across; nothing else does.
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

                // <b>The diagonal is a discipline against itself, and it stays live.</b> Steel
                // against steel finds members modelled through each other; a row that merges two
                // suppliers has supplier-against-supplier clashes only here; a duplicates test is
                // nearly always a source against itself. So it takes a click like any other cell -
                // but no bulk fill writes it, because filling a grid never meant that, and it is
                // drawn as its own thing so a symbol sitting there reads as somebody's decision
                // rather than as something a sweep left behind.
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
                    // <b>A closed decision must not look like an open one.</b> This was a white
                    // cell carrying a five-pixel dash in the faintest ink on the screen, beside
                    // undecided cells that are white and carry nothing - the two states in the
                    // grid that mean opposite things, drawn as the least separable pair on it.
                    // A flat grey ground and a full-weight dash make the scan work the way the
                    // job does: white is what is left to do.
                    //
                    // Down the diagonal the spine's colour wins, because the line has to be
                    // followable through whatever state one of its cells happens to be in - and
                    // the dash on it says the rest.
                    // <b>Nav against white is 1.17 to one.</b> The ground was carrying almost
                    // none of the separation this branch exists to make, so the whole of it was
                    // still one six-pixel dash - on a grid scanned for what is left to do, which
                    // is the scan the dash was introduced for. BdStrong is 1.5, and the dash on
                    // top is full weight in Muted.
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
                    // <b>A symbol that builds nothing is the most dangerous state on the
                    // screen, and it was the least visible.</b> Off, a cell went from one pale
                    // fill to another one two per cent away from it: forty cells stopped meaning
                    // anything, the grid still read as handled, and nothing said so. Struck
                    // through, on the grey that carries every other "this makes no test", it is
                    // the same fact the tick in the inspector states - and on the spine's own
                    // colour where the spine runs, so the line stays one line. The strike is
                    // what says it builds nothing, on either ground.
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
                    // Its own wash, by where it sits in the legend. The letter stays, so the
                    // cell reads in greyscale, on a projector and to anybody who does not see
                    // these hues apart - colour is the channel that makes the pattern scannable,
                    // never the one that carries the meaning.
                    //
                    // <b>Except down the diagonal, which is one line and keeps one colour.</b>
                    // The spine is the axis the whole grid is mirrored about; broken into
                    // twenty-two different washes it stops being a line at all, and a self-pair
                    // is a different kind of decision from the two hundred around it. The letter
                    // still says which test it is, in italics, as it always did.
                    if (spine)
                    {
                        Ground(cell, _style.SpineColour, "Spine");
                        text.FontStyle = FontStyles.Italic;
                    }
                    else
                    {
                        if (self) text.FontStyle = FontStyles.Italic;

                        cell.Background = Washed(meaning);

                        // <b>A chosen colour does not follow the theme and the theme's ink
                        // does.</b> The ten washes are fixed - they belong to the matrix, not to
                        // the seat - so in the dark palette a near-white letter sat on a pale
                        // pastel at one to one and every coloured cell was a blank tile. Which
                        // ink to write in is a property of the colour underneath.
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

                Named(cell);
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
            /// The wash a symbol's cells carry: its own colour, or the one its place in the
            /// legend gives it.
            ///
            /// <b>Empty is not a missing colour.</b> It means "whatever the tool would pick",
            /// which is what every matrix starts as and most of them stay - so a legend of four
            /// symbols is four distinguishable colours without anybody choosing one, and the
            /// practice that has always drawn clearances green can say so.
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
            /// How strongly a cell is drawn while one symbol is being looked for.
            ///
            /// <b>"Eighteen cells carrying it" and no way to see them.</b> Pressing a symbol said
            /// how many cells used it and then left the reader to find them by eye on a grid of
            /// two hundred. Everything that is not the symbol on the panel drops back while it is
            /// showing, so the eighteen are the only thing at full strength - and nothing is
            /// hidden, moved or filtered out, so the shape of the matrix is still the matrix.
            ///
            /// <b>The content drops back, never the cell.</b> Set on the border, the fade took
            /// the rules and the grounds with it: every row line, every column line and the whole
            /// diagonal went to about one to one, so the eighteen lit cells floated on a blank
            /// field with nothing to read their coordinates against - which is the one thing the
            /// reader is doing. And three tenths rather than two: at two the shape of the grid
            /// was gone too.
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

                // <b>Whether the document will take a write at all.</b> The host says so, and
                // says so precisely for this - "the difference has to be visible before somebody
                // spends five minutes building a matrix that cannot be created". This screen was
                // asking only whether there is a clash engine, so on a document that refuses
                // writes the whole matrix got drawn, the blue button got pressed, and the answer
                // came back afterwards as three lines out of two hundred.
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
                var ruled = 0;
                var mute = 0;

                foreach (var row in _board.Rows)
                foreach (var column in _board.Columns)
                {
                    if (!_board.IsKeptHalf(row, column)) continue;

                    var held = _board.At(row, column);

                    // <b>A discipline against itself is not an open pair.</b> It was counted as
                    // one, and no bulk fill will ever write it - so on a twenty-discipline matrix
                    // the bar stopped at a hundred and ninety of two hundred and ten and would
                    // not move, and "go to the first open one" parked on the diagonal over and
                    // over. It counts once somebody has deliberately put something in it.
                    if (held == null && _board.IsSelfPair(row, column)) continue;

                    live++;

                    if (held == null) continue;

                    // A pair ruled out is decided. That is the whole point of being able to say
                    // so: the bar reaches the end when every pair has been ruled on, not when
                    // every pair has been given a test.
                    done++;

                    if (MatrixBoard.IsNotRequired(held)) { ruled++; continue; }

                    // <b>Counted apart, because the bar cannot tell the truth about them.</b> A
                    // cell whose symbol is turned off is decided - somebody put it there - and
                    // builds nothing, so a bar that counts it as done says the matrix is finished
                    // while forty pairs go untested. It stays in "decided" and is named here.
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

                // <b>A finished matrix stopped its own bar at sixty-nine per cent.</b> The fill
                // was a hard-coded hundred and sixty-eight pixels wide inside a track that
                // stretches to the panel - two hundred and forty-three of them. Two columns in
                // star widths take whatever the track turns out to be, at any panel size.
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
                    + (left == 0 ? ". All of them." : ", " + Ui.Count(left, "still open"))));

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

                        Remember(what: "moving that " + (rows ? "row" : "column"));

                        // With its twin on the other axis, so grouping a matrix is one job.
                        _board.MoveLine(moved, to);

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

            /// <summary>
            /// Add a category: a row, the column standing for it, and the cursor where its name
            /// goes.
            ///
            /// <b>There were two of these and they behaved differently.</b> "Add a discipline" made a
            /// row and left the columns alone; "add a column" made a column and left the rows
            /// alone; and they even ended differently, one in the typing box and one in the
            /// inspector. So a person building a twenty-discipline matrix set every discipline
            /// up twice and the two halves agreed only as long as they typed the same thing
            /// both times - and the moment they did not, the pair stopped being mirrored, both
            /// its cells went live, and the dead half grew a hole.
            ///
            /// A clash matrix is a list of disciplines against itself. One act, both axes, and
            /// the same ending wherever it is pressed from.
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
            /// A column, made and picked.
            ///
            /// <b>On a mirrored board this is not a column, it is a discipline.</b> Rows and
            /// columns are one list there - adding either adds both - so this goes to the same
            /// place every other way of adding one goes, and the name is typed as it is made.
            ///
            /// Where the axes are their own lists it really is a column, and used to add a row
            /// anyway: the method was a one-line alias written when there was no such board, and
            /// the only thing that called it was a menu on a mirrored one. Nothing noticed
            /// because nothing reached it.
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
            private List<Parts.Entry> LineMenu(MatrixLine line, int at, bool rows)
            {
                var lines = rows ? _board.Rows : _board.Columns;
                var what = rows ? "row" : "column";

                // <b>Name it, number it and code it are gone from here.</b> They were three
                // modal dialogues doing what the panel does in place - which is the panel's
                // whole stated reason for existing, "this is where four dialogues went" - and
                // they behaved differently from it, so the same edit had two outcomes depending
                // on which door it came through.
                // <b>One order, on every menu this screen has.</b> What it is, then what it
                // points at, then where it is filed, then where it sits, then the one act that
                // destroys something - last, behind a rule, in the danger colour. A reader who
                // has opened one of these menus knows where to look on the next.
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
            /// Point a line at something, and take the line standing for it on the other axis.
            ///
            /// <b>Saying what a row is was half a decision.</b> The column for that discipline
            /// went on pointing at nothing, so the pair built no test - and, worse, the two
            /// stopped being a mirror of each other, which quietly turned the triangle into a
            /// square with the dead half drawn as decisions nobody had made. One press says it
            /// once; a column somebody has deliberately pointed somewhere else is left alone.
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
            /// The cells one press of the menu acts on: the marked block, or the one under it.
            ///
            /// <b>A block was marked and the menu wrote one cell.</b> Marking thirty pairs and
            /// choosing a symbol for them put it in one - the one under the pointer - and left
            /// the other twenty-nine as they were, silently, with the block still drawn round
            /// them. The keyboard has always written the whole block; the menu is the same act
            /// with a different hand, and it has to mean the same thing.
            ///
            /// Only when the press is inside the block, because a right-click somewhere else is
            /// somebody asking about that cell, not about a selection they left behind.
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
            /// <b>Dissolve and delete are the two that have to be told apart.</b> A folder holds
            /// no source, no symbol and no decision of its own, so taking the folder away can
            /// never cost anybody a decision they made - and taking its disciplines away costs
            /// them every decision on eight rows and eight columns at once. One is tidying and
            /// one is destruction, and a menu that words them alike will have somebody press the
            /// wrong one.
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
                    "Agreed, and no test is made. Different from a cell nobody has got to yet.",
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
                        + "everything already decided. The diagonal is left alone - a discipline "
                        + "against itself is a test somebody chooses, not one a fill makes.",
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

                // <b>No style, so this drew in the default black.</b> There is no implicit
                // TextBlock style in the sheet, so a letter that asks for no ink gets Brushes.
                // Black - invisible in the light palette only by luck, and 1.5 to one on the
                // dark washes. Its ink comes from the wash under it, like the cells.
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
            /// The rubber, as something the brush can carry.
            ///
            /// <b>"Not decided" put the brush down instead of picking one up.</b> It was wired to
            /// arm nothing - which is what the palette does when you press the symbol already on
            /// the brush - so the key that says "empties the cell" disarmed you, and every click
            /// after it did nothing at all. Emptying a cell was reachable only from the Delete
            /// key, which is not what the palette said it was.
            ///
            /// The brush carries a symbol or nothing, and nothing already means "no brush", so
            /// the rubber needs a mark of its own to be carried by - exactly as the pair ruled
            /// out does. What it writes into a cell is nothing, which is the one thing about it
            /// that differs, and that happens in the one place every stroke goes through.
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
                    if (said is FrameworkElement held)
                    {
                        held.VerticalAlignment = VerticalAlignment.Center;
                        held.Margin = new Thickness(left, 0, 0, 0);
                    }

                    panel.Children.Add(said);
                }

                // What is going on, on the left, where a sentence is read from.
                void Say(UIElement said, double left = 0) => Into(_strip, said, left);

                // What can be done, against the right-hand edge, in the same place every time.
                void Does(UIElement said, double left = 12) => Into(_verbs, said, left);

                // <b>A strip that narrates every press is a strip nobody reads.</b> It carried
                // running commentary - how to use the brush, which row is in the panel, where
                // the cursor is - rewritten on every click, in the one band of the screen that
                // is supposed to be glanceable. All three said something already on the screen:
                // the panel is titled with the row, the cursor is drawn in the cell it is in,
                // and the brush is the chip that is lit on the palette. What stays here is
                // state that has a control attached to it, and nothing that is a lesson.
                // <b>A standing fact about the seat, in a row that is already there.</b> This
                // used to be a band across the top of the window: it pushed the matrix down by
                // its own height, said the same sentence every time the tab was opened, and on a
                // seat with no clash engine that is every session for ever, about something its
                // owner learned once. Made to float instead, it covered the words underneath.
                //
                // The strip is already drawn, at a height that does not change, and this is
                // exactly the kind of thing it carries. Six words here and the whole sentence on
                // the tip; a press puts it away for as long as the application runs, because the
                // fact is about the seat and a seat that gains a clash engine is a different
                // Navisworks and a different session.
                if (!_session.Clash.IsAvailable && !_hushed)
                {
                    // <b>Three words and a mark, because the strip has one line.</b> Said at
                    // length here - the sentence, and a Dismiss beside it - it pushed "Saving as
                    // you go" onto a second row, and a strip that grows a line has moved the
                    // matrix down by one. Which is the whole thing this was supposed to stop
                    // doing. The sentence is on the tip, where a sentence costs nothing, and
                    // pressing it is what puts it away.
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
                    told.Children.Add(Ui.Small("No clash engine"));

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

                if (_brush == null)
                {
                    Say(Ui.Small("No symbol selected"), _hushed || _session.Clash.IsAvailable ? 0 : 16);
                }
                else
                {
                    Say(Ui.Glyph(Glyphs.Pencil, 12, "Accent", new Thickness(0, 0, 8, 0)));

                    Say(new TextBlock
                    {
                        Text = Brushed + " selected",
                        FontWeight = FontWeights.SemiBold,
                    }.Wearing("Body"));

                    Say(Ui.Quiet("Clear selection", () => Arm(null)), 10);
                }

                // <b>Two of the five modes said nothing at all.</b> The picked line and the lit
                // symbol were both invisible - and the lit one greys nine cells in ten, which
                // with nothing to explain it reads as a fault rather than as a search.
                // Kept, alone of the three, because it is the one state that is not visible in
                // the thing it describes: nine cells in ten drawn faint reads as a fault unless
                // something says it is a search, and the way out of it is the button beside it.
                if (_lit != null)
                {
                    Say(Ui.Small("Showing only " + Quoted(_lit)), 16);
                    Say(Ui.Quiet("Show all", () => Lighting(null)), 6);
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
                                 + " — type a symbol for all of it"), 16);

                // The tally and whether it is saved are both state, and both belong with the
                // rest of the state - on the left, where a sentence is read from.
                Say(Ui.Loose(_count.Wearing("Hint")), 16);
                Say(Saying(), 16);

                // Everything from here is a control, and every one of them goes on the right.
                // The box that makes rows is under the last one, which on a matrix of any size
                // is off the bottom of the screen. This is how you get to it.
                // <b>There were two of these and they were different shapes.</b> One scrolled to
                // the box that names a discipline as you type it; the other made one called "New
                // category" and left you to find it. Both add the same thing, so there is one of
                // them, and it goes to the box - which is the editor, and the only place a
                // discipline gets a name at the moment it is made.
                Does(Ui.Quiet("Add discipline", Adding), 0);

                // <b>The twin the columns never got.</b> The plus that adds one sits past the
                // last column of the band, which on any matrix wide enough to scroll is off the
                // right-hand edge of the screen - the same fault the rows were given this link
                // to fix.
                // <b>Most of the keyboard was folklore.</b> The palette chips teach the digits
                // and the cell page mentions shift and an arrow; Alt with an arrow to move a
                // line existed only inside an accessibility name, so a screen reader taught it
                // and a sighted user never learned it. For a screen whose speed argument is that
                // a grid is filled from the keyboard, there was no reference anywhere.
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

                // <b>It used to collapse when it had nothing on it, and that moved the
                // palette.</b> The panel docks the palette above the progress and below this,
                // so a strip that comes and going between a cell and a row shifted the keys
                // thirty pixels every time somebody moved between the two - and the palette's
                // whole argument for living there is that the hand finds it in the same place.
                // Every page carries verbs now, so it is always there; the guard stays for the
                // one frame before the first draw.
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
            /// One thing that can be done, as a symbol rather than a sentence.
            ///
            /// <b>Five sentences down a panel two hundred and sixty pixels wide.</b> "Duplicate
            /// it, source and all" is a good sentence and a poor button: it wraps onto two
            /// lines, it costs a row of the panel each, and the five of them together pushed the
            /// thing the panel is about off the top of it. As symbols they are one row, they are
            /// in the same place every time, and the sentence is still there on the tip for
            /// anybody who wants it.
            /// </summary>
            /// <param name="glyph">The symbol.</param>
            /// <param name="tip">The sentence it replaces, which is what the tip says.</param>
            /// <param name="onPress">Does it.</param>
            /// <param name="danger">True for the ones that take something away.</param>
            private static System.Windows.Controls.Button Doable(string glyph, string tip,
                                                                 Action? onPress,
                                                                 bool danger = false) =>
                Ui.Icon(glyph, tip, onPress, danger ? "RedTx" : "Muted");

            /// <summary>
            /// The verbs for whatever the panel is showing, in one row that never rearranges.
            ///
            /// <b>The strip used to reorder itself under the hand.</b> The fill only appeared
            /// while a symbol was armed and the row skipped whatever was not there, so arming a
            /// brush moved clear, duplicate and - the one that matters - the bin one place to
            /// the right. A verb that is not available now is drawn in its own place, greyed;
            /// nothing shuffles, and the destroyer is behind a gap of its own so the press that
            /// misses it does nothing rather than something.
            ///
            /// And the row says what it acts on. Sitting between a hairline and the palette it
            /// read as palette furniture, which is the wrong owner for five verbs about a row.
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

                // <b>A full redraw, because a name is part of what the grid's shape is made
                // of.</b> Renaming one band and leaving the other alone drew the old shape over
                // the new matrix: the twin followed the name, the mirror moved with it, and the
                // cells went on being painted as they were before the edit.
                // <b>It redrew the whole screen on every letter.</b> Renaming rebuilt the grid
                // and then rebuilt the panel - including this very box - so the caret was thrown
                // out after each character and the name had to be re-clicked to type the next
                // one. The name shows in two places, the row header and the column header, and
                // those are what a rename has to repaint. The panel is already right: it is
                // where the letters are being typed.
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

                    line.Number = typed;

                    var twin = _board.TwinOf(line);

                    if (twin != null) twin.Number = typed;

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

                    line.Code = typed;

                    var twin = _board.TwinOf(line);

                    if (twin != null) twin.Code = typed;

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

                // <b>Every pair this line takes part in, not the half of them that this side of
                // the diagonal happens to hold.</b> Counted on the kept half, row 1 said "of 20"
                // and row 18 said "of 3" - and two inches below it the list of what the row is
                // checked against named nineteen disciplines for both. One line, one panel, two
                // totals. A line is in a pair with every line on the other axis, once.
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

                // <b>Red, because it is the button that takes an afternoon back.</b> Clearing a
                // whole line wipes every decision on it and was drawn as a neutral grey cross,
                // indistinguishable from duplicate beside it.
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

                // <b>Twenty disciplines are rarely twenty different things.</b> Half a matrix is
                // the same package split by level or by area, and typing the whole row again to
                // change one word in it is the sort of work a tool is supposed to remove.
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
            /// What a row or column is checked against, gathered under the test that checks it.
            ///
            /// <b>A mirrored matrix keeps each pair once</b>, so half of what a discipline is
            /// checked against sits on the other axis, and "what is ARC_Ceilings checked against"
            /// - the question a coordination meeting actually asks - means following a line that
            /// bends at the diagonal, across two scroll axes, in front of a client. So it is said
            /// here in words.
            ///
            /// <b>But twenty-one bullets in one weight is not an answer.</b> Every line said the
            /// same four things in the same type - a name, a symbol, a kind, a tolerance - so
            /// nothing stood out, nothing could be counted, and the shape of the row was buried
            /// in its own detail. The question is not "list the pairs"; it is "how is this
            /// checked, and against what". Grouped under the test, each group says what the test
            /// is once and then names its disciplines, so a row reads as three or four facts -
            /// seven hard, five clearances, three ruled out, three still open - instead of
            /// twenty-one sentences.
            ///
            /// And the two states it used to drop are here: a pair ruled out and a pair nobody
            /// has decided are both things somebody wants to see on this panel, and the open
            /// group is the one that says what is left to do on this line.
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

                // <b>No scroller of its own.</b> The list used to be capped and scrolled inside
                // the panel that was already scrolling, so a wheel over it moved one of two
                // things depending on where the pointer happened to be and the list could hide
                // its own last group. The panel scrolls; the verbs, the palette and the progress
                // are docked out of its way and cannot be pushed off by anything in it.
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

                // <b>One editor, on the palette, where the symbol is.</b> This used to be five
                // fields you could type into, which made a second place to change what a symbol
                // means - and a colour picker in a window about the whole grid made a third.
                // What a symbol is is one form, opened by the pencil beside the plus; this says
                // what it is now and what its cells are doing.
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

                // <b>The press that was missing.</b> A matrix is nearly always "this one thing,
                // except for the pairs we talked about", so it is filled exceptions first and
                // blanket last - and the blanket had two sizes: one line, or the whole grid over
                // everything already decided. Twenty gutter clicks was the answer and it is not
                // an answer. Empty pairs only, so it cannot take a decision back.
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

                // <b>"Eighteen cells carrying it" was a number and a dead end.</b> Every cell
                // that has it is already lit; this walks the cursor from one to the next, which
                // is the other half of finding something on a grid too big to see at once.
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

                // <b>The palette was drawn twice, two hundred pixels apart.</b> This page built
                // the same six keys, out of the same component, above the real palette that was
                // still on the screen underneath it - and the two behaved differently, one
                // setting this cell and one arming the brush. One key, one meaning: the palette
                // below is the palette, and this says what is in the cell and how to change it.
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
            /// Copy what is in the selected cells, and paste it somewhere else.
            ///
            /// <b>Half a matrix is the same answer as the other half.</b> Six disciplines that
            /// treat services the way the last six do is six columns of the same twelve marks,
            /// and typing them again is the work this screen exists to remove - the same
            /// argument that gave the grid a brush and a block in the first place.
            ///
            /// Tab-separated text, so it is the clipboard the rest of the world uses: a block
            /// lifted out of here drops into Excel as cells, and a block of letters copied out
            /// of somebody's own spreadsheet drops in here. An empty cell is an empty field,
            /// which is what keeps a paste aligned with the shape it was cut from.
            /// </summary>
            /// <param name="cut">True to empty the cells after taking what was in them.</param>
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
            /// Put what was copied down from the cursor.
            ///
            /// <b>What was copied is a shape, and the cursor is its top left.</b> It stops at
            /// the edges of the grid rather than wrapping or refusing - a block copied from the
            /// middle and pasted near the end puts down as much as fits, and says how much that
            /// was, which is more use than an error about a rectangle that does not fit.
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

                // <b>Alt and an arrow move the row itself.</b> Reordering was a drag and only a
                // drag, so the one gesture that changes what the matrix is rather than what is in
                // it was the one gesture nobody without a mouse could make.
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

                // <b>Shift and an arrow take the block out.</b> The cursor moves as it always
                // did; the corner it started from stays where it was, and everything between
                // them is what the next symbol goes into.
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
            /// The cursor has moved: draw it, scroll to it, and say where it is.
            ///
            /// <b>The panel follows the cursor, because there is only one selection.</b> It used
            /// to refuse to redraw while a row was picked, so pressing ROW 4 and then arrowing
            /// about the grid left the panel answering about row 4 while symbols went into cells
            /// four rows away - two cursors disagreeing, with eleven-pixel type on the strip as
            /// the only thing that knew. Moving the cursor is a touch like pressing a cell is,
            /// and it lets go of whatever was held.
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

                    // <b>Ctrl+A and one key used to overwrite every decision on the grid.</b>
                    // The same act from the menu asks first, and is described there as the one
                    // bulk act that does - so there were two doors to that room and a lock on
                    // one of them. Taking the whole grid from the keyboard fills what nobody has
                    // ruled on and leaves the rest, like every other bulk gesture here; the
                    // deliberate overwrite stays on the deliberate press.
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
            /// Keep what the matrix is now, so the next act can be taken back.
            ///
            /// <b>A run of keystrokes is one act.</b> Typing "150" into a tolerance is three
            /// changes to the legend and one thing somebody did, so an edit that names its run
            /// is only remembered the first time that run is seen. Anything else clears the
            /// name, so the next digit typed after some other act starts a new run.
            /// </summary>
            /// <param name="run">Names a run of edits that share one undo, or empty.</param>
            /// <param name="what">What is about to happen, for undo to name when it takes it back.</param>
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

                // <b>In its own place, not at the end.</b> The legend keeps the order its symbols
                // were made in, because that order is the number key beside each one on the
                // palette and the wash its cells are drawn in. Built by filtering the symbol out
                // and appending the new one, correcting a tolerance moved it to the bottom of the
                // list: its keystroke changed, every symbol under it shifted up one, and every
                // cell drawn in a colour nobody had chosen changed colour. In the middle of a
                // workshop, on a projector.
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
                        // <b>Inside undo, like every other edit.</b> The one panel with no Cancel
                        // was also the one panel with no take-back: it touched the save timer and
                        // never remembered anything, so a colour tried and disliked could only be
                        // undone by remembering what it had been.
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
            /// Give a symbol a different letter, and take its cells with it.
            ///
            /// <b>A symbol's name was the one thing about it that could not be changed.</b> The
            /// panel let you change what it means, how tight it is and whether it builds
            /// anything, but the letter itself was fixed at the moment it was made - so a legend
            /// that came out of a client's sheet as "1" and "2" stayed that way, and calling it
            /// "H" meant taking it out of the legend, which orphaned every cell using it, and
            /// then painting all of them again.
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
            /// Make a symbol, all five of it at once.
            ///
            /// <b>It used to ask for the letter and nothing else.</b> Every symbol was born a
            /// hard clash at the default tolerance, and the four decisions that were not asked
            /// about had to be found afterwards by somebody who knew where to look. What a
            /// symbol is is one thought, so it is one form.
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
            /// Everything that reorganises the whole grid, in one place.
            ///
            /// <b>These were filed under "Start from", and none of them starts anything.</b>
            /// Pointing every row at a set, putting both axes in code order, squaring the
            /// columns off and deciding whether the axes are one list are all things done to a
            /// matrix that already exists - offered on a menu whose other four lines make a new
            /// one and throw the old one away. A person looking for "put these in order" had no
            /// reason to open it, and a person opening it to start again had four ways to
            /// rearrange what they were about to discard.
            ///
            /// One verb, one menu: this is the whole grid at once, where the line's own menu is
            /// one line at a time.
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

                // <b>The shape of the thing, said out loud.</b> Both axes being one list is
                // what makes a clash matrix a clash matrix - it is why the grid mirrors, why
                // half of it is dead and why the diagonal is not a pair. It is the default and
                // it stays out of the way, but a matrix of one trade's sets against another's
                // is a real thing somebody builds, and this is where they say so.
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
            /// Every setting this screen has, under the word people look for.
            ///
            /// <b>They were in three places and none of them was called settings.</b> How the
            /// grid is drawn was a verb on the strip called "Look"; how a test is named was a
            /// menu called "Names and folder"; where the built tests are filed was the last line
            /// of that menu. Three answers to "where do I change how this behaves", none of them
            /// where anybody would look first, and one of them wearing a paint icon.
            ///
            /// Everything that changes how the tool behaves rather than what the matrix says is
            /// here. Nothing here edits the matrix.
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
            /// Point every row that has no source at the saved set of the same name, in one press.
            ///
            /// <b>A hundred and twenty presses were one flow repeated twenty times.</b> Saying
            /// what a row is takes a header press, a menu, a picker and a confirm, and twenty
            /// rows is that four times over per row - even though the picker underneath has
            /// always been a multi-select list and the rows are nearly always named after the
            /// sets they stand for. Ticked here, each set finds the row that shares its name.
            ///
            /// Nothing already pointed is touched, so this can be pressed again after rows are
            /// added, and rows it could not match are named rather than left to be found.
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

                            // <b>Exactly, then loosely.</b> A client's workbook is precisely
                            // where the names will not line up - "Mechanical Ductwork" against
                            // "MEP_HVA_Ductwork" - and an exact match there finds nothing and
                            // sends somebody round twenty rows by hand. Compared on their letters
                            // and digits alone, and then on one containing the other, those two
                            // meet.
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
            /// Put the disciplines in code order, both axes together.
            ///
            /// <b>A block is a rectangle, and a decision is a discipline against a discipline.</b>
            /// "Every MEP row against every structural column" is one rectangle only when the
            /// disciplines sit together - and a list typed in the order they came up in a meeting
            /// never does. The codes are already on the rows and they are already what the test
            /// names are built from, so they are what to sort by; the columns follow, because a
            /// rectangle needs both axes in one order.
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

                // <b>Grouping and folders were the same idea twice.</b> This put the rows in code
                // order and drew nothing to say why they were in that order; a folder says it,
                // and every one of them can then be named, shut, merged and used in a test name.
                // A line with no code stays loose rather than being filed under an empty name.
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

                Ui.Working("Reading the document's tests…", job =>
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

                Remember(what: "reading the matrix out of the model's tests");

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
            /// Run the tests this matrix just built, and say what came back.
            ///
            /// <b>The matrix was write-only.</b> It drew the pairs, wrote the tests, and then
            /// said "open the Clash Manager to run them" - so the screen that knows which pairs
            /// exist could never answer the question the pairs exist for. The host runs a test by
            /// id and hands back its results; both were there and neither was used.
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
            /// Read what the tests found back onto the grid, so a cell says how bad the pair is.
            ///
            /// <b>The question a matrix is asked in month three is not "which pairs are tested"
            /// - that is settled - it is "where are the clashes".</b> The screen that draws the
            /// pairs could never answer it, so the answer was read off another tool in another
            /// vocabulary. The count and how many are still open ride on the cell's tip and its
            /// automation name, and the panel says them in words.
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

                // <b>The best diagnostic in the product, computed and thrown away.</b> The planner
                // records why each cell it dropped makes no test, cell by cell; the only trace on
                // the screen was the word "skipped" inside a count on the strip. It is the list
                // that explains why "a hundred and ninety-eight tests" is not two hundred and
                // thirty-one, and it is the question somebody asks the moment they see both.
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

                // <b>A pair ruled out left its test running.</b> Changing a cell to a dash is the
                // deliberate, agreed gesture this tool is proudest of, and the test that pair
                // used to have went on running, finding clashes and appearing in reports - so the
                // person who retired the pair believed they had, and had not. Deleting on its own
                // would be a matrix deciding it owns the whole clash detective; offering to is
                // the difference.
                if (spare.Count > 0)
                    Said(Ui.Info(Ui.Count(spare.Count, "test") + " in the document is for a pair "
                                 + "this matrix no longer asks for. They keep running until "
                                 + "somebody says otherwise.",
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
                        : " " + Ui.Count(open, "pair") + " still has nobody's decision on it and "
                          + "makes no test either way — building now builds the rest, and it can "
                          + "be built again when they are settled.")
                    + (_folder.Length == 0 ? string.Empty : " They go in " + Quoted(_folder) + "."),
                    "Build",
                    () => Write(making));
            }

            /// <summary>
            /// Apply the difference: make what is new, and alter what is only different.
            ///
            /// <b>This used to make everything.</b> Every line the difference called Altered went
            /// through Create, so changing one word of the naming pattern on a built matrix
            /// wrote two hundred and thirty-one second tests beside the ones already carrying a
            /// fortnight of statuses, assignments and comment trails - and nothing said so. The
            /// whole reason the difference exists is to protect that work, and the button at the
            /// end of it was throwing the difference away.
            ///
            /// A line that only needs renaming is renamed. A line whose tolerance or kind has
            /// moved is changed where it stands, results and all. Only a line the document has
            /// nothing for is made.
            /// </summary>
            /// <param name="making">The lines to act on.</param>
            private void Write(IReadOnlyList<MatrixLineChange> making)
            {
                var made = 0;
                var altered = 0;
                var refused = new List<string>();

                var turned = 0;

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

                            if (_session.Clash.Create(spec) != null) made++;
                            else Refused(line.Name, "could not be made");

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

                        if (done) altered++;
                    }
                });

                Quiet();

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

                    // <b>Running them is the next act, and it was a sentence pointing at another
                    // tab.</b> The host will run a test and this screen has every id it just
                    // wrote, so the offer belongs here, where the person who pressed Build is
                    // still standing.
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

            private void Sheet()
            {
                var path = Ui.AskToSave("Save the matrix", "Excel|*.xlsx", "clash-matrix.xlsx");

                if (string.IsNullOrWhiteSpace(path)) return;

                try
                {
                    System.IO.File.WriteAllBytes(path!,
                        XlsxWriter.Write(new[] { MatrixSanity.AsSheet(_board, _legend) }));

                    Did(Ui.Done("Saved. It carries the grid and the legend under it."));
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
            /// One saved set as the picker lists it: where it lives, and which kind it is.
            ///
            /// <b>A search set and a selection set are not the same thing and the list said
            /// neither.</b> One holds the elements that were in it when somebody saved it; the
            /// other holds whatever answers a query today. Pointing a discipline at the wrong one
            /// is the difference between a test that follows the model and a test that quietly
            /// stops seeing new work - and the two are told apart nowhere on a list of names
            /// where both kinds sit together, often under the same name in different folders.
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
            /// Swallow a confirmation.
            ///
            /// <b>A banner after every press is a banner nobody reads.</b> Twenty-two acts on
            /// this screen ended by opening a strip across the top to say they had worked -
            /// cells pasted, rows added, a folder merged - each one pushing the grid down,
            /// each one needing a press to dismiss, and every one of them telling somebody
            /// something they had just watched happen.
            ///
            /// The grid is the confirmation. A pasted block is on it, a merged folder is on it,
            /// and the strip over it already says when the work was last saved. What is left in
            /// the notices is what a person cannot see for themselves: something that went
            /// wrong, and a report they asked for by name.
            ///
            /// Kept as a call rather than deleted at the call sites, because "this act says
            /// nothing" is a decision worth being able to find and reverse.
            /// </summary>
            /// <param name="note">What would have been said.</param>
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
