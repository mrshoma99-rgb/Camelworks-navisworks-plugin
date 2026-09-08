using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A fraction of whatever it is bound to.
    ///
    /// One use: giving a table a height budget taken from the pane it is in rather than from a
    /// number. WPF is resolution independent, so a pixel figure already survives a change of DPI -
    /// what it does not survive is a change of pane. The same 420 that overflowed a dock pane at
    /// its 340 minimum left two thirds of a tall monitor empty.
    /// </summary>
    internal sealed class Portion : IValueConverter
    {
        /// <summary>Make one.</summary>
        /// <param name="of">How much of the bound value to take, 0 to 1.</param>
        /// <param name="least">Never return less than this, so a collapsed pane still shows rows.</param>
        internal Portion(double of, double least)
        {
            Of = of;
            Least = least;
        }

        /// <summary>How much of the bound value to take.</summary>
        internal double Of { get; }

        /// <summary>The floor.</summary>
        internal double Least { get; }

        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // A ScrollViewer that has not been measured reports zero, and a zero-height table is a
            // table nobody can see. The floor is what it falls back to until layout has run.
            var whole = value is double number && number > 0 ? number : 0;

            return whole <= 0 ? Least : Math.Max(Least, whole * Of);
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// One table cell drawn as a symbol and its text.
    ///
    /// A converter rather than a factory tree because a glyph is a stroked path on its own
    /// 24-unit canvas inside a Viewbox, which <see cref="Ui.Glyph"/> already assembles; building
    /// the same three elements again as a FrameworkElementFactory would be a second definition of
    /// how a glyph is drawn.
    /// </summary>
    internal sealed class SymbolCell : IValueConverter
    {
        /// <summary>Make one.</summary>
        /// <param name="column">Which of the row's cells supplies the text.</param>
        internal SymbolCell(int column) => Column = column;

        /// <summary>Which of the row's cells supplies the text.</summary>
        internal int Column { get; }

        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is TableRow row)) return string.Empty;

            var text = new TextBlock
            {
                Text = row[Column],
                ToolTip = row[Column],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (string.IsNullOrEmpty(row.Symbol)) return text;

            var line = new StackPanel { Orientation = Orientation.Horizontal };

            line.Children.Add(Ui.Glyph(row.Symbol!, 14, "Muted", new Thickness(0, 0, 7, 0)));
            line.Children.Add(text);

            return line;
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// One cell of a table, drawn as the status its word names.
    ///
    /// Built as a visual for the same reason <see cref="SymbolCell"/> is: the chip is assembled
    /// once, in the place that owns how a status looks.
    /// </summary>
    internal sealed class StatusCell : IValueConverter
    {
        /// <summary>Make one.</summary>
        /// <param name="column">Which of the row's cells holds the status word.</param>
        internal StatusCell(int column) => Column = column;

        /// <summary>Which of the row's cells holds the status word.</summary>
        internal int Column { get; }

        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is TableRow row)) return string.Empty;

            var word = row[Column];

            // A cell that is not one of the five words is drawn as it is: a table may mix a
            // status column with a row that has none, and an empty cell is not an error.
            return CamelWorks.Core.Findings.FindingStatusLattice.TryParse(word, out var status)
                ? FindingStatusWords.Chip(status)
                : (object)new TextBlock
                {
                    Text = word,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// The heading over one band of a table's rows: what they are filed under, and how many.
    ///
    /// Built as a visual rather than a factory tree with the values bound, for the reason
    /// <see cref="SymbolCell"/> is: the folder mark is a path on its own canvas inside a Viewbox
    /// and <c>Ui.Glyph</c> already assembles that.
    /// </summary>
    internal sealed class BandHead : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is CollectionViewGroup group)) return string.Empty;

            var line = new DockPanel { LastChildFill = true };

            var tally = new TextBlock
            {
                Text = group.ItemCount.ToString(CultureInfo.CurrentCulture),
                VerticalAlignment = VerticalAlignment.Center,
            };

            tally.Wearing("Hint");
            DockPanel.SetDock(tally, Dock.Right);
            line.Children.Add(tally);

            // The chevron says the band folds, and which way it is. Bound to the toggle the
            // band sits inside, so it turns when the band does.
            var chevron = Ui.Glyph(Glyphs.Down, 11, "Muted", new Thickness(0, 0, 6, 0));

            chevron.SetBinding(UIElement.VisibilityProperty, new Binding("IsChecked")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor,
                                                    typeof(ToggleButton), 1),
                Converter = new Folded(false),
            });

            var closed = Ui.Glyph(Glyphs.Right, 11, "Muted", new Thickness(0, 0, 6, 0));

            closed.SetBinding(UIElement.VisibilityProperty, new Binding("IsChecked")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor,
                                                    typeof(ToggleButton), 1),
                Converter = new Folded(true),
            });

            line.Children.Add(chevron);
            line.Children.Add(closed);

            line.Children.Add(Ui.Glyph(Glyphs.Folder, 13, "Muted", new Thickness(0, 0, 7, 0)));

            var name = new TextBlock
            {
                Text = group.Name as string ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            name.Wearing("Hint");
            line.Children.Add(name);

            var band = new Border
            {
                Padding = new Thickness(10, 6, 12, 5),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = line,
            };

            band.Tint(Border.BackgroundProperty, "Card2");
            band.Tint(Border.BorderBrushProperty, "Bd");

            return band;
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Whether one of the band's two chevrons is the one to draw.
    ///
    /// Two glyphs, one shown at a time, rather than one glyph rotated: the chevrons in this
    /// product are separate paths and a rotation would be the only place a symbol is made by
    /// transforming another.
    /// </summary>
    internal sealed class Folded : IValueConverter
    {
        /// <summary>Say which of the two this is.</summary>
        /// <param name="whenShut">True for the chevron shown while the band is closed.</param>
        internal Folded(bool whenShut) => _whenShut = whenShut;

        private readonly bool _whenShut;

        /// <inheritdoc />
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var open = value as bool? ?? true;

            return open != _whenShut ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <inheritdoc />
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// One row of a plan: what it is now, what an action would make of it, and why it would not.
    /// </summary>
    public sealed class Planned
    {
        /// <summary>Name one row of the plan.</summary>
        /// <param name="was">What it is called now.</param>
        /// <param name="becomes">What it would become, or null when nothing would change.</param>
        /// <param name="refused">Why the action cannot touch it, or null when it can.</param>
        public Planned(string was, string? becomes = null, string? refused = null)
        {
            Was = was ?? string.Empty;
            Becomes = becomes;
            Refused = refused;
        }

        /// <summary>What it is called now.</summary>
        public string Was { get; }

        /// <summary>What it would become, or null.</summary>
        public string? Becomes { get; }

        /// <summary>Why the action cannot touch it, or null.</summary>
        public string? Refused { get; }

        /// <summary>Whether this row would actually change.</summary>
        public bool Changes => Refused == null && !string.IsNullOrEmpty(Becomes);
    }

    /// <summary>
    /// The state a screen is in, when it is in one worth saying.
    ///
    /// Four states, and each has one tone. A screen that cannot do its job because the seat will
    /// not let it is not in the same state as a screen whose results are older than the model.
    /// </summary>
    public enum Stand
    {
        /// <summary>Something outside this screen has to happen first: a model opened, a pack chosen.</summary>
        Blocked = 0,

        /// <summary>This edition or licence does not carry part of what the screen does.</summary>
        Limited = 1,

        /// <summary>Something failed. The screen says what, and what state the data is in.</summary>
        Wrong = 2,

        /// <summary>What is shown predates what it was read from.</summary>
        Stale = 3,
    }

    /// <summary>
    /// What a field asks of the person filling it in.
    ///
    /// Three states rather than two, because "the app fills this in" is a different promise from
    /// "you may leave this out": a cut elevation taken from a storey is not optional, it is
    /// already answered, and a box that says neither is a box that has to be guessed at.
    /// </summary>
    public enum Need
    {
        /// <summary>The field makes no claim. The default, so nothing changes where nobody has said.</summary>
        Unsaid = 0,

        /// <summary>Has to be filled in before the surface's action can run.</summary>
        Required = 1,

        /// <summary>The check runs without it.</summary>
        Optional = 2,

        /// <summary>Filled in from elsewhere, and overwritable by hand.</summary>
        Automatic = 3,
    }

    /// <summary>
    /// Where a screen's edits go, and when.
    ///
    /// Stated by the footer rather than by each screen in its own words, because the screens that
    /// most needed to say it were the ones that said nothing: the panels that write every settled
    /// keystroke straight into the project file carry no Save button and no mark, so the one
    /// contract a coordinator cannot guess is the one with no evidence on screen.
    /// </summary>
    public enum Keeping
    {
        /// <summary>The screen makes no claim. The default, so nothing changes where nobody has said.</summary>
        Unsaid = 0,

        /// <summary>Written to the project as each change is made.</summary>
        AsYouGo = 1,

        /// <summary>Held until the footer's action is pressed, and something is waiting.</summary>
        OnPress = 2,

        /// <summary>Held until the footer's action is pressed, and nothing is waiting.</summary>
        Settled = 3,
    }

    /// <summary>What a path field is asking for, and therefore which dialog it opens.</summary>
    public enum PathKind
    {
        /// <summary>A file that already exists.</summary>
        Open = 0,

        /// <summary>A file to be written, which need not exist yet.</summary>
        Save = 1,

        /// <summary>A folder.</summary>
        Folder = 2,
    }

    /// <summary>How a value reads: neutral, good, worth attention, or wrong.</summary>
    public enum Tone
    {
        /// <summary>No opinion.</summary>
        Plain = 0,

        /// <summary>Fine.</summary>
        Good = 1,

        /// <summary>Worth a look.</summary>
        Warn = 2,

        /// <summary>Wrong, or failed.</summary>
        Bad = 3,
    }

    /// <summary>
    /// One row of a table, plus the object it came from.
    ///
    /// The cells are indexed rather than named so a column can bind to <c>[0]</c> without a
    /// per-screen row class; <see cref="Source"/> keeps the original, so clicking a row can select
    /// elements, open a clash or edit a layer without the screen having to hold a parallel list.
    ///
    /// It is <c>Source</c> rather than the obvious <c>Item</c> because an indexer is already called
    /// Item as far as the CLR is concerned, and the two cannot both exist.
    /// </summary>
    public sealed class TableRow
    {
        private readonly string?[] _cells;

        /// <summary>Create a row.</summary>
        /// <param name="item">What the row is about. Comes back on selection.</param>
        /// <param name="cells">The cell text, in column order.</param>
        public TableRow(object? item, params string?[] cells)
        {
            Source = item;
            _cells = cells ?? Array.Empty<string?>();
        }

        /// <summary>What the row is about.</summary>
        public object? Source { get; }

        /// <summary>How the row reads — used to tint it.</summary>
        public Tone Tone { get; set; }

        /// <summary>
        /// The symbol drawn beside one of this row's cells, or null for none.
        ///
        /// Path data in a 24-unit box, as <c>Glyphs</c> writes it. Which column it lands in is the
        /// table's, not the row's: see <c>Symbolled</c>.
        /// </summary>
        public string? Symbol { get; set; }

        /// <summary>
        /// The band this row belongs under, or null when the table is not banded.
        ///
        /// Read by <c>Filed</c>, which groups the rows on it. Rows carrying the same text sit
        /// under one heading, so the value is not also worth a column of its own.
        /// </summary>
        public string? Filed { get; set; }

        /// <summary>Cell text by column. Out-of-range is empty rather than an exception.</summary>
        /// <param name="index">Column index.</param>
        public string this[int index] =>
            index >= 0 && index < _cells.Length ? _cells[index] ?? string.Empty : string.Empty;

        /// <summary>How many cells the row has.</summary>
        public int Count => _cells.Length;

        /// <summary>
        /// Whether this row's tick box is on, for a table with a ticked column.
        ///
        /// Written by the box itself through a two-way binding, which is why the setter is what
        /// tells the caller: a routed handler on a template factory is the other way to do this
        /// and it makes the row presenter throw while it measures.
        /// </summary>
        public bool? Ticked
        {
            get => _ticked;
            set
            {
                if (_ticked == value) return;

                _ticked = value;
                _onTicked?.Invoke(value == true);
            }
        }

        private bool? _ticked;
        private Action<bool>? _onTicked;

        /// <summary>
        /// Give this row a tick box, on a table whose column is drawn by <c>Ticking</c>.
        /// </summary>
        /// <param name="on">Where the box starts.</param>
        /// <param name="onTicked">What to do when somebody ticks or unticks it.</param>
        public TableRow Ticking(bool on, Action<bool> onTicked)
        {
            _ticked = on;
            _onTicked = onTicked;

            return this;
        }

        /// <inheritdoc />
        public override string ToString() => string.Join(" | ", _cells);
    }

    /// <summary>
    /// A long-running piece of work, and the mechanism this product uses to stay responsive during
    /// one.
    ///
    /// This is not a background thread and cannot be. The Navisworks API is main-thread-only:
    /// reading a property from another thread is undefined behaviour rather than a slower version
    /// of the same call. The work therefore runs on the UI thread and pumps the dispatcher between
    /// chunks, which is what lets the progress bar move and the Stop button respond. Cancellation
    /// happens between chunks, never inside one, so a chunk in progress delays Stop.
    ///
    /// Set <see cref="Total"/> whenever the count is known before the loop starts, so the bar can
    /// report "12,300 of 29,000" and an estimate rather than indeterminate progress.
    /// </summary>
    public sealed class Job
    {
        // Fast enough to read as motion, slow enough that pumping does not cost more than the work
        // it is reporting on. The old value was 250ms, which on a fast loop looked like a hang
        // between updates.
        private const int PumpEveryMs = 90;

        private readonly Dispatcher? _dispatcher;
        private readonly TextBlock? _status;
        private readonly ProgressBar? _bar;
        private readonly Panel? _output;
        private readonly System.Diagnostics.Stopwatch _sincePump = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _since = System.Diagnostics.Stopwatch.StartNew();

        private long _total;
        private long _done;
        private string _what = string.Empty;

        internal Job(Dispatcher? dispatcher, TextBlock? status, ProgressBar? bar,
                     Panel? output = null)
        {
            _dispatcher = dispatcher;
            _status = status;
            _bar = bar;
            _output = output;
        }

        /// <summary>
        /// A job with nothing to report to and no way to be cancelled.
        ///
        /// For work driven by something other than a button — a graph node calling a service that
        /// takes a job. The alternative is a second copy of every service that does not report
        /// progress, which is how two code paths start disagreeing.
        /// </summary>
        internal static Job Silent() => new Job(null, null, null, null);

        /// <summary>Set when the user has asked to stop.</summary>
        public bool IsCancelled { get; internal set; }

        /// <summary>How long this has been running.</summary>
        public TimeSpan Elapsed => _since.Elapsed;

        /// <summary>
        /// How many items there are in total, when that is known before the loop starts.
        ///
        /// Setting it turns the bar from "something is happening" into "this is how far through it
        /// is and roughly how much longer". Leave it at zero when the count genuinely is not known
        /// — a wrong total is worse than none, because a bar that reaches the end and keeps going
        /// tells the user the tool is lying to them.
        /// </summary>
        public long Total
        {
            get => _total;

            set
            {
                _total = value < 0 ? 0 : value;
                Paint(force: true);
            }
        }

        /// <summary>
        /// Report something that went wrong, where the job's own results are being written.
        ///
        /// The status line is a running commentary that the next line overwrites, so it is the
        /// wrong place for a reason somebody has to read after the job has finished. This puts it
        /// in the pane instead, where it stays.
        /// </summary>
        /// <param name="message">What went wrong, in words.</param>
        public void Trouble(string? message)
        {
            if (_output == null || string.IsNullOrWhiteSpace(message)) return;

            _output.Children.Add(Ui.Problem(message!));
        }

        /// <summary>Say what is happening. Repaints a few times a second, not on every call.</summary>
        /// <param name="message">One short line.</param>
        public void Say(string message)
        {
            _what = message ?? string.Empty;
            _done = 0;
            _total = 0;
            Paint(force: false);
        }

        /// <summary>
        /// Report progress and ask whether to keep going.
        /// </summary>
        /// <param name="done">How many items are finished.</param>
        /// <param name="what">What the items are, for the status line.</param>
        /// <returns>False once the user has cancelled.</returns>
        public bool Step(int done, string what)
        {
            _done = done;
            _what = what ?? string.Empty;
            Paint(force: false);
            return !IsCancelled;
        }

        /// <summary>
        /// Report progress against a known total, and ask whether to keep going.
        /// </summary>
        /// <param name="done">How many items are finished.</param>
        /// <param name="total">How many there are altogether.</param>
        /// <param name="what">What the items are, for the status line.</param>
        /// <returns>False once the user has cancelled.</returns>
        public bool Step(int done, int total, string what)
        {
            _total = total < 0 ? 0 : total;
            return Step(done, what);
        }

        /// <summary>
        /// Push whatever has been reported onto the screen, and let the pane breathe.
        ///
        /// Invoking an empty action at <see cref="DispatcherPriority.Background"/> is what drains
        /// everything above it first — layout, render, and input, which is the Stop button. It is
        /// the only reason any of this is clickable while work is running.
        /// </summary>
        private void Paint(bool force)
        {
            if (_status == null || _dispatcher == null) return;
            if (!force && _sincePump.ElapsedMilliseconds < PumpEveryMs) return;

            // Never while the host is mid-enumeration. Pumping runs a nested message loop, which
            // lets Navisworks act on a click while we are part way through walking its tree - and
            // a collection mutated under a native iterator takes the process with it. The text is
            // still updated; it simply does not reach the glass until the walk is over.
            if (CamelWorks.Nav.NavDocument.IsWalking)
            {
                _status.Text = Describe();
                return;
            }

            _sincePump.Restart();

            _status.Text = Describe();

            if (_bar != null)
            {
                if (_total > 0)
                {
                    _bar.IsIndeterminate = false;
                    _bar.Maximum = _total;
                    _bar.Value = Math.Min(_done, _total);
                }
                else
                {
                    _bar.IsIndeterminate = true;
                }
            }

            _dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
        }

        private string Describe()
        {
            var seconds = _since.Elapsed.TotalSeconds;
            var clock = seconds < 60
                ? ((int)seconds).ToString(CultureInfo.InvariantCulture) + "s"
                : ((int)(seconds / 60)).ToString(CultureInfo.InvariantCulture) + "m "
                  + ((int)(seconds % 60)).ToString(CultureInfo.InvariantCulture) + "s";

            if (_total <= 0)
                return string.IsNullOrEmpty(_what) ? "Working... " + clock : _what + "  ·  " + clock;

            var percent = (int)Math.Round(100.0 * _done / _total);

            var line = percent.ToString(CultureInfo.InvariantCulture) + "%  ·  "
                       + _done.ToString("N0", CultureInfo.InvariantCulture) + " of "
                       + _total.ToString("N0", CultureInfo.InvariantCulture)
                       + (string.IsNullOrEmpty(_what) ? string.Empty : " " + _what)
                       + "  ·  " + clock;

            // Only offered once there is enough of a rate to extrapolate from. A "2 minutes left"
            // computed off the first fifty elements is a number that will be wrong by an order of
            // magnitude, and being wrong about this is what makes people stop believing the bar.
            if (_done > 200 && seconds > 2)
            {
                var left = (_total - _done) * seconds / _done;
                if (left > 3) line += ",  about " + Rough(left) + " left";
            }

            return line;
        }

        private static string Rough(double seconds) =>
            seconds < 60 ? ((int)seconds).ToString(CultureInfo.InvariantCulture) + "s"
            : seconds < 3600 ? ((int)Math.Round(seconds / 60)).ToString(CultureInfo.InvariantCulture) + " min"
            : ((int)Math.Round(seconds / 3600)).ToString(CultureInfo.InvariantCulture) + " hr";
    }

    /// <summary>
    /// The widget vocabulary every screen is built from.
    ///
    /// Written once and shared so that sixteen screens cannot each invent their own spacing, their
    /// own idea of an empty state, and their own way of saying something went wrong. Built in code
    /// rather than XAML because almost every screen's content is driven by data the shell holds,
    /// and templates for that would be more markup than the code they replace — but every colour,
    /// font size and control shape comes from <see cref="Skin"/> by key, never resolved here, so
    /// that switching the theme repaints what is already on screen instead of requiring every
    /// screen to be rebuilt.
    /// </summary>
    public static class Ui
    {
        /// <summary>
        /// Point the host layer's long walks at somewhere they can be watched and stopped.
        ///
        /// Here rather than at a screen's start-up, because there is no one screen: the walk that
        /// needs this is set off by a press on any of twenty of them, and a screen that forgot to
        /// arrange it is a screen that hangs. Every one of them touches this class before it draws
        /// anything, so this runs first whichever one it is.
        /// </summary>
        static Ui()
        {
            try
            {
                CamelWorks.Nav.NavDocument.LongWork = Working;
            }
            catch (Exception)
            {
                // Without it the walk still happens; it simply has nowhere to report.
            }
        }

        /// <summary>The gap used between stacked things, everywhere.</summary>
        public const double Gap = 8;

        /// <summary>
        /// Which CamelWorks this is, read from the assembly rather than kept as a constant.
        ///
        /// A constant is a second place for the version to live and therefore a place for it to be
        /// wrong, and the one thing a report cover and a support bundle must not be wrong about is
        /// which build produced them.
        /// </summary>
        public static string Release { get; } = ReadRelease();

        /// <summary>
        /// Raised when a run starts or finishes, with how many are in flight.
        ///
        /// The shell listens so that a job started on one tab is still visibly running after you
        /// switch to another — otherwise the answer to "did I actually press it?" is to press it
        /// again, which is how two exports end up fighting over one file.
        /// </summary>
        public static event EventHandler<int>? Running;

        private static int _running;

        /// <summary>
        /// True while any CamelWorks job holds the host.
        ///
        /// The host has one thread and one document. Two of our operations running into each
        /// other - a compare walking the tree while a clash read starts on top of it - is not a
        /// race we can win by being careful inside each one.
        /// </summary>
        public static bool IsBusy => _running > 0;

        private static string ReadRelease()
        {
            try
            {
                var assembly = typeof(Ui).Assembly;

                var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                   ?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(info)) return info!;

                return assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch (Exception)
            {
                // A build with no version attributes at all is a development build, and saying so
                // is better than a report cover that claims a version nobody can look up.
                return "unknown";
            }
        }

        // -----------------------------------------------------------------------------------
        // Painting
        //
        // Everything below reaches the palette through these two. A brush resolved at build time
        // is a brush that stays light when the pane goes dark, which is the bug the exporter's
        // own pre-flight rows had to be rebuilt to work around.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Put one of the skin's two depths on an element.
        ///
        /// Five shadows were written by hand in three specs across the sheet and three screens,
        /// two of them leaving Direction unset, so a dropdown's shadow and a floating panel's fell
        /// different ways on one screen.
        /// </summary>
        /// <param name="element">What to raise.</param>
        /// <param name="floating">True for a panel over the work, false for a popover.</param>
        /// <typeparam name="T">Anything that can carry an effect.</typeparam>
        public static T Raised<T>(this T element, bool floating = false) where T : UIElement
        {
            var key = floating ? "Shadow.Floating" : "Shadow.Raised";

            if (Skin.Sheet[key] is System.Windows.Media.Effects.Effect depth)
                element.Effect = depth;

            return element;
        }

        /// <summary>Bind one property to a palette key, so it repaints when the theme changes.</summary>
        /// <param name="element">What to paint.</param>
        /// <param name="property">Which property.</param>
        /// <param name="key">The palette key.</param>
        /// <typeparam name="T">Anything that can hold a resource reference.</typeparam>
        public static T Tint<T>(this T element, DependencyProperty property, string key)
            where T : FrameworkElement
        {
            element.SetResourceReference(property, key);
            return element;
        }

        /// <summary>Give something one of the skin's named styles.</summary>
        /// <param name="element">What to style.</param>
        /// <param name="key">The style key.</param>
        /// <typeparam name="T">Anything that can hold a style.</typeparam>
        public static T Wearing<T>(this T element, string key) where T : FrameworkElement
        {
            element.SetResourceReference(FrameworkElement.StyleProperty, key);
            return element;
        }

        // -----------------------------------------------------------------------------------
        // Type
        // -----------------------------------------------------------------------------------

        /// <summary>A screen or card title.</summary>
        /// <param name="text">The title.</param>
        public static TextBlock Heading(string text) =>
            new TextBlock
            {
                Text = text,

                // Wrapping is set here as well as in the style on purpose. It is the one property
                // whose absence is not cosmetic: a skin that fails to load would otherwise give a
                // pane whose every line runs off the right edge, unreadable rather than plain.
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2),
            }.Wearing("Head");

        /// <summary>
        /// A screen's own title, above everything on it.
        ///
        /// One step above <see cref="Heading"/>, which titles a card or a pane. The two were the
        /// same size: the 20px step existed in the sheet with no callers, so the ramp was four
        /// sizes inside five points and a screen's name carried the same weight as the third
        /// card down it.
        /// </summary>
        /// <param name="text">The title.</param>
        public static TextBlock Titled(string text) =>
            new TextBlock
            {
                Text = text,

                // As in Heading: the one property whose absence is not cosmetic if the skin
                // fails to load.
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2),
            }.Wearing("Title");

        /// <summary>The line under a title that says what the screen is for.</summary>
        /// <param name="text">The line.</param>
        public static TextBlock Sub(string text) =>
            new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            }.Wearing("Lbl");

        /// <summary>
        /// An ordinary line of text.
        ///
        /// The <paramref name="opacity"/> argument is honoured as intent rather than as a number:
        /// anything below full reads as "secondary", and secondary text is drawn in the palette's
        /// muted ink at full strength. Grey text is legible; black text at 70% is a smear, and at
        /// the small sizes this pane uses the difference is the whole of it.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="opacity">How prominent it is.</param>
        /// <param name="bold">Whether to emphasise it.</param>
        public static TextBlock Line(string text, double opacity = 1, bool bold = false)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, bold ? 6 : 1, 0, 0),
            };

            block.Wearing("Body");
            if (opacity < 0.95) block.Tint(TextBlock.ForegroundProperty, opacity < 0.7 ? "Faint" : "Muted");

            return block;
        }

        /// <summary>Small print: the sentence under a control that explains it.</summary>
        /// <param name="text">The text.</param>
        public static TextBlock Small(string text) =>
            new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            }.Wearing("Hint");

        /// <summary>
        /// The count beside a table's verbs — "none picked", "3 picked".
        ///
        /// Its own shape rather than <see cref="Small"/>, because it sits on a line of 26-pixel
        /// symbols and a hint carries a three-pixel top margin meant for prose under a field. On
        /// that line it rode above the buttons and above the title, which is what somebody sees
        /// when they say the header does not line up.
        /// </summary>
        /// <param name="text">What it says.</param>
        public static TextBlock Tally(string text) =>
            new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Gap, 0),
            }.Wearing("Hint");

        /// <summary>An identifier, a path or a hash — anything that must not be read as prose.</summary>
        /// <param name="text">The text.</param>
        public static TextBlock Code(string text) =>
            new TextBlock { Text = text }.Wearing("Mono");

        /// <summary>
        /// A small label above a group of things.
        ///
        /// Sentence case, as written. This upper-cased its text until every section heading in
        /// the product read as shouting, and a heading and a pane title were indistinguishable
        /// because both arrived here. The "Caps" style still sets the size, weight and colour;
        /// only the case is the caller's.
        /// </summary>
        /// <param name="text">The label.</param>
        public static TextBlock Eyebrow(string text) =>
            new TextBlock { Text = text ?? string.Empty }.Wearing("Caps");

        /// <summary>
        /// A group's heading: what the fields under it are for.
        ///
        /// Above the labels it governs, not below them. As an eyebrow it was 11px bold against
        /// 12px labels and 13px values, so the heading was the smallest type in its own group and
        /// a panel of four groups read as one undifferentiated column.
        /// </summary>
        /// <param name="text">The heading.</param>
        public static TextBlock Grouping(string text) =>
            new TextBlock { Text = text ?? string.Empty }.Wearing("Section");

        /// <summary>A number and its label, for a scoreboard.</summary>
        /// <param name="value">The number, already formatted.</param>
        /// <param name="label">What it counts.</param>
        /// <param name="tone">How it reads.</param>
        public static UIElement Figure(string value, string label, Tone tone = Tone.Plain)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 18, 6), MinWidth = 64 };

            var number = new TextBlock
            {
                Text = value,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Skin.Ink(tone),
            };

            // Digits in a scoreboard have to line up column to column; proportional figures make
            // three numbers of the same magnitude look like three different magnitudes.
            TextOptions.SetTextFormattingMode(number, TextFormattingMode.Ideal);
            number.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);

            panel.Children.Add(number);
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }.Wearing("Hint"));
            return panel;
        }

        /// <summary>
        /// A figure that is also the control for it.
        ///
        /// A board that prints "12 new" over a list, and separately offers a dropdown with "New"
        /// in it, has said the same thing twice and made the reader work out that the two are
        /// connected. The number somebody is looking at when they decide to narrow the list is the
        /// number they should be able to press.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <param name="label">What it counts.</param>
        /// <param name="tone">How it reads.</param>
        /// <param name="onClick">What pressing it does.</param>
        /// <param name="active">Whether the board is already narrowed to this one.</param>
        public static UIElement Figure(string value, string label, Tone tone, Action onClick,
                                       bool active = false)
        {
            if (onClick == null) throw new ArgumentNullException(nameof(onClick));

            var frame = new Border
            {
                Child = Figure(value, label, tone),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Thickness(8, 4, 2, 0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(active ? 1 : 0),
            };

            if (active)
            {
                frame.Tint(Border.BackgroundProperty, "Card2");
                frame.Tint(Border.BorderBrushProperty, "BdStrong");
            }

            frame.MouseLeftButtonUp += (s, e) => onClick();

            // Hover only when it is not already the one showing: a lit control that lights further
            // on hover reads as two states of the same thing rather than one state and a hint.
            if (!active)
            {
                frame.MouseEnter += (s, e) => frame.Tint(Border.BackgroundProperty, "Card2");
                frame.MouseLeave += (s, e) => frame.Background = Brushes.Transparent;
            }

            return frame;
        }

        /// <summary>A small labelled pill, for status and counts.</summary>
        /// <param name="text">What it says.</param>
        /// <param name="tone">How it reads.</param>
        public static UIElement Pill(string text, Tone tone = Tone.Plain)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Skin.Ink(tone),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var pill = new Border
            {
                Background = Skin.Wash(tone),
                BorderBrush = Skin.Edge(tone),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 5, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
            };

            if (tone == Tone.Plain)
            {
                pill.Tint(Border.BackgroundProperty, "Nav");
                pill.Tint(Border.BorderBrushProperty, "Bd");
                label.Tint(TextBlock.ForegroundProperty, "Muted");
            }

            return pill;
        }

        // -----------------------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------------------

        /// <summary>Stack things vertically.</summary>
        /// <param name="children">What to stack.</param>
        public static StackPanel Stack(params UIElement?[] children)
        {
            var panel = new StackPanel();

            // Loose on the way in.
            //
            // A screen keeps a control in a field when it must outlive a redraw, and every redraw
            // hands it to a fresh panel through here. Clearing or discarding the old panel does
            // not detach what was inside it, so the second draw would otherwise be told the
            // element is already the logical child of another element - which is a screen that
            // works once. Doing it here rather than at each call site is the difference between
            // a rule and a mechanism; this one has come back four times as a rule.
            foreach (var child in children) if (child != null) panel.Children.Add(Loose(child));

            return panel;
        }

        /// <summary>Lay things out left to right, wrapping when the pane is narrow.</summary>
        /// <param name="children">What to lay out.</param>
        public static Panel Across(params UIElement?[] children)
        {
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (var child in children)
            {
                if (child == null) continue;
                if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 6, 5);
                panel.Children.Add(Loose(child));
            }

            return panel;
        }

        /// <summary>
        /// A field that takes the width of the panel it is in.
        ///
        /// One rule, in one place. Four set-up panels each had their own: two set a fixed Width
        /// and aligned left, one capped the width and stretched, one capped a grid column. So the
        /// same form drawn on four tabs had four different field widths, and a report about one of
        /// them fixed one of them.
        ///
        /// A cap plus the default stretch is the trap worth naming: WPF centres a stretched
        /// element that carries a MaxWidth, so a capped field starts wherever the leftover space
        /// puts it rather than where the heading does. A field takes what the panel gives it, and
        /// a pair of fields splits that through <see cref="Evenly"/>.
        /// </summary>
        /// <typeparam name="T">The field's type, so a caller keeps it.</typeparam>
        /// <param name="field">The field.</param>
        public static T Filled<T>(T field) where T : FrameworkElement
        {
            if (field == null) throw new ArgumentNullException(nameof(field));

            field.HorizontalAlignment = HorizontalAlignment.Stretch;

            // Any cap an earlier hand left on it comes off. A field that fills has no ceiling,
            // and a leftover one puts it back in the middle of the panel.
            field.MaxWidth = double.PositiveInfinity;
            field.Width = double.NaN;

            return field;
        }

        /// <summary>
        /// Two or more controls sharing a row in equal columns.
        ///
        /// Not <see cref="Across"/>, which wraps and gives each child its desired width: a pair of
        /// fields laid out that way keeps the width it was written with however wide the pane is,
        /// and a pane wider than the pair leaves the rest of the row white.
        /// </summary>
        /// <param name="children">The controls, one per column. Nulls are skipped.</param>
        public static Panel Evenly(params UIElement?[] children)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };

            var kept = children.Where(one => one != null).ToList();

            for (var i = 0; i < kept.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });

                var child = Loose(kept[i]!);

                if (child is FrameworkElement element)
                {
                    element.HorizontalAlignment = HorizontalAlignment.Stretch;

                    // A gutter between the columns, and none outside them, so a pair lines up with
                    // the full-width field above it at both edges.
                    element.Margin = new Thickness(i == 0 ? 0 : Gap, 0,
                                                   i == kept.Count - 1 ? 0 : 0, 0);
                }

                Grid.SetColumn(child, i);
                grid.Children.Add(child);
            }

            return grid;
        }

        /// <summary>
        /// A row of actions, with the important one first and the rest quieter.
        ///
        /// Given as one call rather than three buttons in an <see cref="Across"/> because the point
        /// is the ranking: on the screens this replaces, Run, Save and Publish were three
        /// identical grey rectangles and the user had to read all three every time.
        /// </summary>
        /// <param name="first">The action the screen is for. Rendered as the primary.</param>
        /// <param name="rest">Everything else, rendered quieter.</param>
        public static Panel Actions(UIElement? first, params UIElement?[] rest)
        {
            // Gap below as well as above. The five-pixel per-child bottom margin is the gutter
            // between two buttons that wrap onto a second line; using it as the gutter under the
            // whole row welded every action row to whatever came next — most often the fold of
            // options it is supposed to sit above.
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Gap, 0, Gap) };

            if (first is FrameworkElement lead)
            {
                lead.Margin = new Thickness(0, 0, 8, Gap);

                // It used to restyle the first child as the primary, whatever the caller had
                // asked for. That was meant to stop a row coming out as three identical grey
                // rectangles, and it did - by taking the decision away from the one place that
                // knows the answer. A row reading "Keep" then "Forget" made Keep the loudest
                // thing on the card because it was written first, and "Add a row" outshouted the
                // rule being written above it.
                //
                // The ranking is the caller's now: Primary for the thing the card is for, Quiet
                // for the rest, Danger for what cannot be undone. Position means nothing.
                panel.Children.Add(lead);
            }
            else if (first != null)
            {
                panel.Children.Add(first);
            }

            foreach (var child in rest)
            {
                if (child == null) continue;
                if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 6, Gap);
                panel.Children.Add(child);
            }

            return panel;
        }

        /// <summary>Wrap content in a vertical scroller.</summary>
        /// <param name="content">What to scroll.</param>
        public static ScrollViewer Scroll(UIElement content) => new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // Four at the bottom, not sixteen. The last card carries its own ten, and the two
            // together left every panel with twice the air below it that it has above.
            Padding = new Thickness(12, 12, 12, 4),
            Background = Brushes.Transparent,
            Content = Loose(content),
        };

        /// <summary>A titled block of related things.</summary>
        /// <param name="title">The block's title.</param>
        /// <param name="subtitle">One line saying what it is for, or null.</param>
        /// <param name="body">The contents.</param>
        public static Border Card(string title, string? subtitle, params UIElement?[] body) =>
            Build(null, title, subtitle, body);

        /// <summary>
        /// A titled block with a mark on it.
        ///
        /// The mark is not decoration. A screen of five white rectangles with five bold lines on
        /// them gives the eye nothing to sort by, so finding the card you were just looking at
        /// means reading all five titles again. A glyph is what makes a card findable without
        /// reading, which is the whole of "do not depend on the text".
        /// </summary>
        /// <param name="glyph">One of <see cref="Glyphs"/>.</param>
        /// <param name="title">The block's title.</param>
        /// <param name="subtitle">One line saying what it is for, or null.</param>
        /// <param name="body">The contents.</param>
        public static Border Marked(string glyph, string title, string? subtitle, params UIElement?[] body) =>
            Build(glyph, title, subtitle, body);

        // Deliberately not an overload of Card. A trailing params array absorbs anything, so
        // Card("A", "B", null, body) fits the three-parameter form as readily as the four and the
        // compiler is entitled to refuse to choose between them. One name, one shape.
        private static Border Build(string? glyph, string title, string? subtitle, params UIElement?[] body)
        {
            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(title))
            {
                if (glyph == null)
                {
                    panel.Children.Add(Heading(title));
                }
                else
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
                    row.Children.Add(Glyph(glyph, 17, "Accent", new Thickness(0, 0, 9, 0)));

                    var caption = Heading(title);
                    caption.Margin = new Thickness(0);
                    caption.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(caption);

                    panel.Children.Add(row);
                }
            }

            if (subtitle != null) panel.Children.Add(Sub(subtitle));
            if (!string.IsNullOrEmpty(title)) panel.Children.Add(HeaderRule());

            foreach (var child in body) if (child != null) panel.Children.Add(child);

            var card = new Border { Child = panel };
            card.Wearing("CardStyle");
            return card;
        }

        private static UIElement HeaderRule() => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 7, 0, 10),
        }.Tint(Border.BackgroundProperty, "Bd");

        /// <summary>
        /// A card that is one step of an ordered sequence.
        ///
        /// Where a screen is a pipeline — suppress, then group, then assign — the order is the
        /// thing somebody has to understand first, and a sentence at the top saying so is the
        /// weakest way to say it. A number on each card says it without being read, and then the
        /// sentence can go.
        /// </summary>
        /// <param name="step">Which step this is, from one.</param>
        /// <param name="title">The step's title.</param>
        /// <param name="subtitle">One line saying what it does, or null.</param>
        /// <param name="body">The contents.</param>
        public static Border Numbered(int step, string title, string? subtitle, params UIElement?[] body)
        {
            var badge = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                Child = new TextBlock
                {
                    Text = step.ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            badge.Tint(Border.BackgroundProperty, "Accent");

            var panel = new StackPanel();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };

            row.Children.Add(badge);

            var caption = Heading(title);
            caption.Margin = new Thickness(0);
            caption.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(caption);

            panel.Children.Add(row);
            if (subtitle != null) panel.Children.Add(Sub(subtitle));
            panel.Children.Add(HeaderRule());

            foreach (var child in body) if (child != null) panel.Children.Add(child);

            var card = new Border { Child = panel };
            card.Wearing("CardStyle");
            return card;
        }

        /// <summary>
        /// How much of a list is on the screen.
        ///
        /// A table that shows five rows of a hundred and says nothing about the other ninety-five
        /// is a table somebody will read as the whole answer. Cheap to say, and the one number
        /// that stops a screen quietly lying about its own completeness.
        /// </summary>
        /// <param name="shown">How many rows are on the screen.</param>
        /// <param name="total">How many there are altogether.</param>
        /// <param name="unit">What they are, singular.</param>
        public static UIElement Tally(int shown, int total, string unit)
        {
            var text = shown >= total
                ? Count(total, unit)
                : "Showing " + shown.ToString("N0", CultureInfo.InvariantCulture) + " of " + Count(total, unit);

            return new TextBlock { Text = text, Margin = new Thickness(0, 5, 0, 0) }.Wearing("Hint");
        }

        /// <summary>
        /// The surface a card's inputs sit on.
        ///
        /// Without it a card is a title and then a column of labelled boxes, which is a
        /// spreadsheet with a heading on it. Putting the inputs on their own tinted panel is what
        /// separates "the part you fill in" from "the part that tells you things" — and it is what
        /// lets the action underneath read as the end of the card rather than one more row.
        /// </summary>
        /// <param name="fields">The inputs.</param>
        public static Border Form(params UIElement?[] fields)
        {
            var panel = new StackPanel();
            var real = 0;

            foreach (var child in fields)
            {
                if (child == null) continue;

                // A Field carries its own bottom margin; a bare line of prose between two of them
                // does not, and comes out welded to the label underneath it. Given the same
                // spacing here rather than at every call site that puts a note in a form.
                if (child is TextBlock note && note.Margin.Bottom == 0)
                    note.Margin = new Thickness(0, 0, 0, 10);
                else
                    real++;

                panel.Children.Add(child);
            }

            var frame = new Border { Child = panel };

            // A grey well around one input is decoration, and at 470 pixels it is decoration that
            // costs 26 of them — inside a card that has already spent 24 on its own padding. One
            // field gets the card's surface; two or more get a surface of their own.
            if (real > 1) frame.Wearing("FormStyle");

            return frame;
        }

        /// <summary>
        /// A block that starts folded up, with its state on the header line.
        ///
        /// This is the answer to a pane where every option of every tool was on screen at once. A
        /// section that is closed still says what it is set to, so folding something away never
        /// costs you the ability to see what it will do — which is the only reason folding is
        /// acceptable in a tool people have to trust before pressing Run.
        /// </summary>
        /// <param name="title">The section's title.</param>
        /// <param name="summary">What it is currently set to, shown while it is folded.</param>
        /// <param name="open">Whether it starts open.</param>
        /// <param name="body">The contents.</param>
        public static Border Section(string title, string? summary, bool open, params UIElement?[] body) =>
            Section(title, () => summary ?? string.Empty, open, body);

        /// <summary>
        /// A disclosure whose header line is recomputed every time it closes.
        ///
        /// The whole argument for folding an option away is that the closed header still says what
        /// it is set to. A summary captured once, when the panel was built, says what it was set
        /// to before anybody touched it — which on the BCF card meant picking 3.0, folding it, and
        /// reading "BCF 2.1" on the header of the thing you had just changed.
        /// </summary>
        /// <param name="title">What is inside.</param>
        /// <param name="summary">What it is currently set to, asked again on every collapse.</param>
        /// <param name="open">Whether it starts open.</param>
        /// <param name="body">The contents.</param>
        public static Border Section(string title, Func<string> summary, bool open, params UIElement?[] body)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            var chevron = Glyph(open ? Glyphs.Down : Glyphs.Right, 14, "Muted");

            var caption = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 10, 0),
            }.Wearing("Body");

            var said = summary();

            var state = new TextBlock
            {
                Text = said,

                // NoWrap as well as trimming: the Hint style wraps, and wrapping beats trimming,
                // so a long summary broke onto a second right-aligned line instead of ending in an
                // ellipsis. A folded header is one line by definition.
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(12, 0, 0, 0),
                Visibility = open || string.IsNullOrEmpty(said) ? Visibility.Collapsed : Visibility.Visible,
            }.Wearing("Hint");

            var header = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            header.Children.Add(chevron);
            Grid.SetColumn(caption, 1);
            header.Children.Add(caption);
            Grid.SetColumn(state, 2);
            header.Children.Add(state);

            var contents = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = open ? Visibility.Visible : Visibility.Collapsed,
            };

            foreach (var child in body) if (child != null) contents.Children.Add(child);

            var shown = open;

            header.MouseLeftButtonUp += (s, e) =>
            {
                shown = !shown;
                contents.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

                if (!shown) state.Text = summary();

                state.Visibility = shown || string.IsNullOrEmpty(state.Text)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                chevron.Child = Arrow(shown ? Glyphs.Down : Glyphs.Right, "Muted");
            };

            var card = new Border { Child = Stack(header, contents) };
            card.Wearing("SectionStyle");
            return card;
        }

        /// <summary>
        /// Mark a disclosure as holding something that destroys data.
        ///
        /// Five folds in a column, one of which deletes the user's viewpoints and four of which
        /// rename or re-file them, is five identical grey bars. The fold still folds — the act
        /// inside it is not one anybody performs by accident once it is open — but it stops
        /// looking exactly like the four that cannot hurt anybody.
        /// </summary>
        /// <param name="section">A section from <see cref="Section(string, string?, bool, UIElement?[])"/>.</param>
        public static Border Grave(this Border section)
        {
            section.BorderThickness = new Thickness(3, 1, 1, 1);
            section.Tint(Border.BorderBrushProperty, "RedBd");
            return section;
        }

        /// <summary>
        /// One thing a check found, weighted by how badly it came out.
        ///
        /// A left edge rather than a whole tinted box. A screen that answers with eleven amber
        /// cards is shouting at somebody who has one real problem to fix, and the point of ranking
        /// findings is lost the moment they all look equally loud.
        /// </summary>
        /// <param name="stop">Whether this one has to be settled before the work is worth doing.</param>
        /// <param name="check">Which check found it.</param>
        /// <param name="headline">The finding in one line.</param>
        /// <param name="detail">Why it matters, or null.</param>
        /// <param name="fix">What to do about it, or null when there is nothing to do but know.</param>
        /// <param name="examples">A handful of the things concerned, already joined, or null.</param>
        /// <param name="go">
        /// What to do about it, if the finding is about something that can be gone to. Rendered
        /// under the examples. A finding whose elements are still in the model is a place, not just
        /// a sentence, and the difference between the two is whether this is null.
        /// </param>
        public static UIElement Finding(bool stop, string check, string headline,
                                        string? detail = null, string? fix = null,
                                        string? examples = null, UIElement? go = null)
        {
            var body = Stack(
                Across(Pill(stop ? "Settle this" : "Worth a look", stop ? Tone.Bad : Tone.Warn), Small(check)),
                Line(headline, 1, true));

            if (detail != null) body.Children.Add(Line(detail, 0.8));

            // The fix reads at full weight and the reason does not. They were both Line(…, 0.8),
            // which put "nobody is told when this happens" and "put it back, then run the tests
            // again" in the same voice — and only one of them is something to go and do.
            if (fix != null)
            {
                var todo = Line(fix, 1);
                todo.Margin = new Thickness(0, 3, 0, 0);
                body.Children.Add(todo);
            }

            if (examples != null) body.Children.Add(Small("e.g. " + examples));

            if (go != null) body.Children.Add(go);

            var frame = new Border
            {
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 2, 0, 2),
                Margin = new Thickness(0, Gap, 0, 0),
                Child = body,
            };

            return frame.Tint(Border.BorderBrushProperty, stop ? "RedBd" : "AmberBd");
        }

        /// <summary>
        /// A path, and the dialog that saves somebody alt-tabbing to Explorer to copy one.
        ///
        /// Sixteen fields in this product asked for a path and offered a bare text box. Typing an
        /// absolute path from memory is not something anybody does; what they actually do is open
        /// Explorer, navigate to the file, copy the address bar and come back — which is the whole
        /// task, done by hand, in front of a tool that could have asked.
        /// </summary>
        /// <param name="label">What the path is for.</param>
        /// <param name="box">The text box holding it. Its width is handed to the row.</param>
        /// <param name="kind">Whether it is a file to open, a file to write, or a folder.</param>
        /// <param name="filter">A dialog filter, as "What it is|*.ext". Ignored for a folder.</param>
        /// <param name="hint">The line under it, or null.</param>
        public static Panel PathField(string label, TextBox box, PathKind kind,
                                      string filter = "All files|*.*", string? hint = null)
        {
            return Field(label, PathRow(box, kind, filter), hint);
        }

        /// <summary>
        /// A path box and its Browse, and nothing else.
        ///
        /// <see cref="PathField"/> puts a label over it and a gap under it, which is right in a
        /// stacked form and wrong in a row of a table: the label - even an empty one - is a line
        /// of height the cells beside it do not have, so the box sits lower than everything on
        /// its row. A table cell wants the control alone.
        /// </summary>
        /// <param name="box">The text box holding the path.</param>
        /// <param name="kind">Whether it is a file to open, a file to write, or a folder.</param>
        /// <param name="filter">A dialog filter, as "What it is|*.ext". Ignored for a folder.</param>
        public static Grid PathRow(TextBox box, PathKind kind, string filter = "All files|*.*")
        {
            var row = new Grid { VerticalAlignment = VerticalAlignment.Center };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The box asked for a comfortable width when it was made. In a 470-pixel pane with a
            // button beside it there is no such width, and a MinWidth the column cannot honour
            // pushes the button off the edge.
            box.MinWidth = 90;
            row.Children.Add(Loose(box));

            var browse = Button("Browse", () => Pick(box, kind, filter));
            browse.Margin = new Thickness(6, 0, 0, 0);
            browse.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);

            return row;
        }

        /// <summary>
        /// Ask for a path to write to, or null when the user cancels.
        ///
        /// Every file dialog in this product goes through here or its counterpart. A file dialog is
        /// a native modal, and one shown with no owner disables every top-level window on the
        /// thread and then re-enables them from an owner it does not have. The result is a closed
        /// dialog and a Navisworks that never regains input — the same fault
        /// <see cref="Shell.HostWindow"/> was written to prevent, by another route.
        ///
        /// Setting the owner at each call site is a convention that fails on the first call site
        /// that forgets, so there is one entry point and a repository check that keeps it the only
        /// one.
        /// </summary>
        /// <param name="title">What the dialog is called.</param>
        /// <param name="filter">The file-type filter.</param>
        /// <param name="suggested">The name to offer, or null.</param>
        /// <param name="extension">The extension to add when the user types none.</param>
        public static string? AskToSave(string title, string filter, string? suggested = null,
                                        string? extension = null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = suggested ?? string.Empty,
                AddExtension = true,
            };

            if (!string.IsNullOrWhiteSpace(extension)) dialog.DefaultExt = extension;

            return Asked(dialog) ? dialog.FileName : null;
        }

        /// <summary>Ask for a path to read, or null when the person changed their mind.</summary>
        /// <param name="title">What the dialog is called.</param>
        /// <param name="filter">The file-type filter.</param>
        /// <param name="suggested">The name to offer, or null.</param>
        public static string? AskToOpen(string title, string filter, string? suggested = null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = suggested ?? string.Empty,
                CheckFileExists = true,
                CheckPathExists = true,
            };

            return Asked(dialog) ? dialog.FileName : null;
        }

        /// <summary>Ask for several paths to read, empty when the person changed their mind.</summary>
        /// <param name="title">What the dialog is called.</param>
        /// <param name="filter">The file-type filter.</param>
        public static IReadOnlyList<string> AskToOpenMany(string title, string filter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = filter,
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
            };

            return Asked(dialog) ? dialog.FileNames : Array.Empty<string>();
        }

        /// <summary>
        /// Show a file dialog owned by the window it was asked for from.
        ///
        /// The owner is what stops the host being disabled and left that way; see
        /// <see cref="AskToSave"/>. With no window of ours on screen there is nothing to own it
        /// to, and an unowned dialog is still better than no dialog — so it is shown, and that is
        /// the one path where the old behaviour remains.
        /// </summary>
        /// <param name="dialog">The dialog.</param>
        internal static bool Asked(Microsoft.Win32.CommonDialog dialog)
        {
            try
            {
                var owner = Shell.HostWindow.Active;

                return (owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
            }
            catch (Exception)
            {
                // A dialog that will not open is not a reason to take the pane down with it.
                return false;
            }
        }

        /// <summary>
        /// The dialog itself.
        ///
        /// Nothing is written here and nothing is read: it puts a path in a box, and the button
        /// that does the work still does the work. So a save dialog does not warn about
        /// overwriting — it has not been asked to overwrite anything yet, and the tool that will
        /// says so itself when it runs.
        /// </summary>
        private static void Pick(TextBox box, PathKind kind, string filter)
        {
            var typed = box.Text.Trim();

            try
            {
                if (kind == PathKind.Save)
                {
                    var save = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = filter,
                        OverwritePrompt = false,
                        AddExtension = true,
                    };

                    Start(save, typed);

                    if (Asked(save)) box.Text = save.FileName;
                    return;
                }

                var folder = kind == PathKind.Folder;

                // WPF on .NET Framework has no folder dialog, and pulling WinForms into a
                // Navisworks add-in for one is a poor trade. A file dialog with a filter that
                // matches nothing shows folders and nothing else, which is the dialog we wanted;
                // the title says what to do with it rather than leaving somebody hunting for a
                // file that is not there.
                var open = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = folder ? "Folders|*.camelworks-no-such-file" : filter,
                    CheckFileExists = !folder,
                    CheckPathExists = true,
                    ValidateNames = !folder,
                    FileName = folder ? "(this folder)" : string.Empty,
                    Title = folder ? "Open the folder you want, then press Open" : "Pick a file",
                };

                Start(open, typed);

                if (!Asked(open)) return;

                box.Text = folder
                    ? System.IO.Path.GetDirectoryName(open.FileName) ?? typed
                    : open.FileName;
            }
            catch (Exception)
            {
                // A dialog that will not open is not a reason to take the pane down with it, and
                // the box is still there to be typed into.
            }
        }

        /// <summary>Open the dialog where the box is already pointing, if that is anywhere real.</summary>
        private static void Start(Microsoft.Win32.FileDialog dialog, string typed)
        {
            if (typed.Length == 0) return;

            try
            {
                var directory = System.IO.Directory.Exists(typed)
                    ? typed
                    : System.IO.Path.GetDirectoryName(typed);

                if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory))
                    dialog.InitialDirectory = directory!;

                if (!System.IO.Directory.Exists(typed))
                {
                    var name = System.IO.Path.GetFileName(typed);
                    if (name.Length > 0) dialog.FileName = name;
                }
            }
            catch (Exception)
            {
                // A path the OS will not even parse. The dialog opens wherever it likes.
            }
        }

        /// <summary>A hairline between two groups of things.</summary>
        public static UIElement Rule() => new Border
        {
            Height = 1,
            Margin = new Thickness(0, Gap, 0, Gap),
        }.Tint(Border.BackgroundProperty, "Bd");

        /// <summary>
        /// The same hairline stood on end, for a bar that runs left to right.
        ///
        /// Two link verbs side by side have no chrome between them and read as one long phrase —
        /// "Resolve the conflict…Say why…". A rule between them separates the two presses without
        /// giving either a border.
        /// </summary>
        public static UIElement Vrule() => new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(Gap, 0, Gap, 0),
            VerticalAlignment = VerticalAlignment.Center,
        }.Tint(Border.BackgroundProperty, "Bd");

        /// <summary>A label on the left and its value on the right, as one row.</summary>
        /// <param name="label">What it is.</param>
        /// <param name="value">What it says.</param>
        /// <param name="tone">How the value reads.</param>
        public static UIElement Split(string label, string value, Tone tone = Tone.Plain)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }.Wearing("Lbl"));

            var right = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            }.Wearing("Body");

            if (tone != Tone.Plain) right.Foreground = Skin.Ink(tone);

            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            return grid;
        }

        // -----------------------------------------------------------------------------------
        // Controls
        // -----------------------------------------------------------------------------------

        /// <summary>A button.</summary>
        /// <param name="caption">What it says.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Button(string caption, Action onClick) =>
            Press(caption, onClick, null);

        /// <summary>
        /// The one action a screen is for.
        ///
        /// There should be at most one of these visible at a time. Two primaries is the same as
        /// none: it puts the decision of which one matters back on the reader.
        /// </summary>
        /// <param name="caption">What it says.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Primary(string caption, Action onClick) =>
            Press(caption, onClick, "PrimaryBtn");

        /// <summary>
        /// The primary a screen cannot let anybody press yet, with why on it.
        ///
        /// The button alone, for a screen that builds its own footer row: <see cref="Foot"/> reads
        /// the reason off the primary's tooltip, so the reason has to travel on the button rather
        /// than beside it. <see cref="Blocked"/> is this in a footer row of its own.
        /// </summary>
        /// <param name="caption">What the press would do.</param>
        /// <param name="why">Why it cannot be pressed. Stated by the footer, not by the caller.</param>
        public static System.Windows.Controls.Button Cannot(string caption, string why)
        {
            var go = new System.Windows.Controls.Button
            {
                Content = caption ?? string.Empty,
                IsEnabled = false,
                ToolTip = why,
            };

            return go.Wearing("PrimaryBtn");
        }

        /// <summary>
        /// The screen's main action as it is drawn when it cannot be pressed: the caption greyed,
        /// and the reason on the same line.
        ///
        /// A filled button drawn live over a notice saying the work is impossible is a press that
        /// reports its own failure, and the reader has to press it to find that out. The reason
        /// belongs beside the control, not under the screen.
        ///
        /// Only for an action that is blocked by the seat rather than by what has been filled in.
        /// A form that is merely incomplete says so where the field is.
        /// </summary>
        /// <param name="caption">What the button would say.</param>
        /// <param name="why">Why it cannot be pressed, in the user's terms.</param>
        public static Panel Blocked(string caption, string why)
        {
            var row = Foot(Cannot(caption, why));

            // Inline on a stage rather than in a footer band, so the gap above it is this
            // caller's to set. A footer band has its own padding.
            row.Margin = new Thickness(0, Gap, 0, 0);

            return row;
        }

        /// <summary>
        /// A screen's footer: the reason, the secondary action, the primary hard right.
        ///
        /// One anatomy for every screen's last row, for one reason: the primary must be in the
        /// same place before and after it can be pressed. The footers this replaces put the
        /// disabled button and its reason in a wrap panel, so the button moved when the reason
        /// arrived or went, and on a narrow pane the reason pushed it onto a second line.
        ///
        /// The reason is read off the primary rather than passed in, so a screen that keeps the
        /// button in a field and enables it later has nothing to redraw: it sets the tooltip and
        /// the footer states it. An enabled primary has no tooltip, so the line is empty and takes
        /// no room; a disabled one without a tooltip is a press with no stated reason, which is
        /// the defect this exists to make visible.
        /// </summary>
        /// <param name="primary">
        /// The screen's one action, or the row a <see cref="RunParts"/> returns. Where it is a
        /// panel, the first button in it is the primary the reason is read from.
        /// </param>
        /// <param name="secondary">A supporting action, drawn to the left of the primary.</param>
        /// <param name="aside">
        /// What the screen is set up with — the windows that hold the settings the primary spends.
        /// Drawn hard left, away from the actions, or null on a footer with none.
        /// </param>
        public static Panel Foot(UIElement primary, UIElement? secondary = null,
                                 UIElement? aside = null) =>
            Foot(primary, secondary, aside, Keeping.Unsaid);

        /// <summary>
        /// The same footer, saying where this screen's edits go.
        ///
        /// Five surfaces in the clash manager had four save contracts between them and two of
        /// them said so. A coordinator could edit a clearance height on Create (written as they
        /// typed, with nothing saying so), a rule on Filter (held until a press) and a status on
        /// Manage (written, under a Save button that implied otherwise) inside two minutes. The
        /// line is part of the footer rather than each screen's own sentence, so a screen that
        /// says nothing is visibly a screen that has not answered.
        /// </summary>
        /// <param name="primary">The one action this surface is for.</param>
        /// <param name="secondary">A second action, or null.</param>
        /// <param name="aside">Something to sit at the far left, or null.</param>
        /// <param name="keeping">Where this screen's edits go, and when.</param>
        public static Panel Foot(UIElement primary, UIElement? secondary, UIElement? aside,
                                 Keeping keeping)
        {
            if (primary == null) throw new ArgumentNullException(nameof(primary));

            var row = new DockPanel { LastChildFill = false };

            var kept = Kept(keeping);

            if (kept != null)
            {
                kept.Margin = new Thickness(Gap, 0, 0, 0);

                DockPanel.SetDock(kept, Dock.Left);
                row.Children.Add(kept);
            }

            if (primary is FrameworkElement main)
            {
                main.VerticalAlignment = VerticalAlignment.Center;
                main.Margin = new Thickness(Gap, 0, 0, 0);
            }

            // Docked in this order, so the primary is against the right edge whatever else the
            // row is holding, and nothing to its left can displace it.
            DockPanel.SetDock(primary, Dock.Right);
            row.Children.Add(primary);

            if (secondary != null)
            {
                if (secondary is FrameworkElement other)
                {
                    other.VerticalAlignment = VerticalAlignment.Center;
                    other.Margin = new Thickness(Gap, 0, 0, 0);
                }

                DockPanel.SetDock(secondary, Dock.Right);
                row.Children.Add(secondary);
            }

            if (aside != null)
            {
                if (aside is FrameworkElement side) side.VerticalAlignment = VerticalAlignment.Center;

                DockPanel.SetDock(aside, Dock.Left);
                row.Children.Add(aside);
            }

            var said = Small(string.Empty);

            said.Margin = new Thickness(0, 0, Gap, 0);
            said.VerticalAlignment = VerticalAlignment.Center;
            said.TextWrapping = TextWrapping.Wrap;
            said.TextAlignment = TextAlignment.Right;

            // Bound rather than copied: the reason is whatever the primary says it is at the
            // moment it is read, and an enabled primary carries none, so the line empties itself.
            var button = Pressed(primary, 3);

            if (button != null)
                said.SetBinding(TextBlock.TextProperty,
                                new Binding("ToolTip")
                                {
                                    Source = button,
                                    TargetNullValue = string.Empty,
                                    FallbackValue = string.Empty,
                                });

            DockPanel.SetDock(said, Dock.Right);
            row.Children.Add(said);

            return row;
        }

        /// <summary>
        /// The button a footer's primary slot is actually built around.
        ///
        /// The slot takes a runner as readily as a button, and a runner is a column holding a row
        /// holding the button, so the search goes down rather than only at the element handed in.
        /// </summary>
        /// <param name="element">The primary slot's content.</param>
        /// <param name="depth">How many panels to look through.</param>
        private static System.Windows.Controls.Button? Pressed(UIElement element, int depth)
        {
            if (element is System.Windows.Controls.Button button) return button;
            if (depth <= 0 || !(element is Panel panel)) return null;

            foreach (var child in panel.Children.OfType<UIElement>())
            {
                var found = Pressed(child, depth - 1);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>A supporting action, with no border until it is hovered.</summary>
        /// <param name="caption">What it says.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Quiet(string caption, Action onClick) =>
            Press(caption, onClick, "QuietBtn");

        /// <summary>An action that removes or overwrites something.</summary>
        /// <param name="caption">What it says.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Danger(string caption, Action onClick) =>
            Press(caption, onClick, "DangerBtn");

        /// <summary>Something that reads as a link rather than a button.</summary>
        /// <param name="caption">What it says.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Link(string caption, Action onClick) =>
            Press(caption, onClick, "LinkBtn");

        /// <summary>
        /// A square action on a row: remove it, move it up, move it down.
        ///
        /// These were text buttons reading "x", "up" and "down". Three words of prose per row, on
        /// a screen with twenty rows, is sixty words of chrome competing with the content.
        /// </summary>
        /// <param name="glyph">One of <see cref="Glyphs"/>.</param>
        /// <param name="tip">What it does, as a tooltip. Row buttons have no caption to read.</param>
        /// <param name="onClick">What it does.</param>
        /// <param name="tint">
        /// Which ink the symbol is drawn in. Muted for nearly all of them - a row of icons is a
        /// row of equals - and a warning colour for the one that takes something away, because a
        /// row of identical symbols where one of them deletes is a row where the wrong one gets
        /// pressed, and a tip only helps somebody who waited for it.
        /// </param>
        public static System.Windows.Controls.Button Icon(string glyph, string tip,
                                                          Action? onClick, string tint = "Muted")
        {
            var button = new System.Windows.Controls.Button
            {
                // Through Glyph, not Arrow: a raw path draws at its own 24-unit size, which inside
                // a 26-pixel button is a cross that fills the row it is meant to sit beside.
                Content = Glyph(glyph, 17, tint),
                ToolTip = tip,
            };

            button.Wearing("RowBtn");

            // Room between symbols. A row of twelve icons with a pixel between them reads as one
            // striped object rather than twelve buttons, and the wrong one gets pressed - which is
            // worse for a row where two of the twelve delete things.
            button.Margin = new Thickness(3, 2, 3, 2);

            if (onClick != null) button.Click += (s, e) => Busy(onClick);
            return button;
        }

        /// <summary>
        /// Build the element index here, inside a job that is already running, when the rows this
        /// job produced will need it.
        ///
        /// A row verb — focus, isolate, a marker sphere, a snapshot — turns a row's element keys
        /// back into elements. A key that is an instance GUID goes through the host's native search
        /// and is cheap; anything else has no native search behind it, so the first key resolved
        /// builds an index of every geometry leaf in the federation. Inside a button press that is
        /// minutes of work with no progress and no cancel.
        ///
        /// It lives here rather than on a screen because it was written twice as a private method
        /// on the screen that needed it and removed twice by later rewrites of that screen, taking
        /// the fix with it. One shared entry point and a check that it is used keeps the fix in
        /// place.
        ///
        /// Nothing is built when the rows all carry GUIDs, so a federation that resolves natively
        /// never pays for the walk. Stopping leaves the index unbuilt rather than partial: a
        /// partial index would report the unindexed elements as missing from the model.
        /// </summary>
        /// <param name="job">The job already running, for its progress line and its Stop.</param>
        /// <param name="about">The keys the rows this job produced carry.</param>
        public static void Ready(Job job, IEnumerable<ElementKey>? about)
        {
            if (job == null || job.IsCancelled || about == null) return;

            if (!(Host.Current?.Model is CamelWorks.Nav.NavDocument document)) return;

            if (document.IsIndexed) return;

            if (!about.Any(k => !k.IsEmpty && k.Rung != KeyRung.InstanceGuid)) return;

            job.Say("Indexing the federation, so the row verbs are instant...");

            document.Index(seen => job.Step(seen, "elements indexed"), () => !job.IsCancelled);
        }

        /// <summary>
        /// Run work that a press did not expect to be long, with a progress bar and a Stop.
        ///
        /// Some presses are cheap almost always and expensive once: the first Focus on a federation
        /// whose elements carry no GUIDs builds an index of every geometry leaf in it. A wait
        /// cursor suits a second of work; over a minute it is indistinguishable from a hang and
        /// offers no way to cancel.
        ///
        /// So the work gets a small owned window reporting what it is doing and offering to stop.
        /// It runs on this thread: the job's own progress reporting pumps the message loop, which
        /// is what lets the window paint and Stop stay clickable while the work continues.
        ///
        /// <c>NavDocument.LongWork</c> is pointed here once, so every screen gets this without
        /// referencing it. A stop is remembered there: the caller is usually resolving a list, and
        /// without that the next item would reopen this window and Stop would look like a restart.
        /// </summary>
        /// <param name="what">What is happening, on the window's title and its first line.</param>
        /// <param name="work">The work, given a progress callback and a keep-going question.</param>
        public static void Working(string what, Action<Action<int>, Func<bool>> work)
        {
            if (work == null) return;

            Working(what, job => work(seen => job.Step(seen, "elements"), () => !job.IsCancelled));
        }

        /// <summary>
        /// The same watched window, handing the work the job itself.
        ///
        /// For work that already speaks <see cref="Job"/>, which is most of what this product does
        /// long enough to be worth watching. Without it a caller either re-wraps the two callbacks
        /// or - as the baseline export did - passes null for the job and runs a whole-board pass
        /// under nothing but a wait cursor: no progress, no Stop, and a host that looks hung.
        /// </summary>
        /// <param name="what">What to say while it runs.</param>
        /// <param name="work">The work, given the job to report through and to ask about stopping.</param>
        public static void Working(string what, Action<Job> work)
        {
            if (work == null) return;

            var status = new TextBlock
            {
                Text = what,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 8, 0, 0),
            }.Wearing("Hint");

            var bar = new ProgressBar { IsIndeterminate = true, Height = 6 };

            var body = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            body.Children.Add(new TextBlock { Text = what, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new Border { Height = 10 });
            body.Children.Add(bar);
            body.Children.Add(status);

            var window = new Window
            {
                Title = "CamelWorks",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var job = new Job(window.Dispatcher, status, bar);

            System.Windows.Controls.Button? stop = null;

            stop = Quiet("Stop", () =>
            {
                job.IsCancelled = true;

                // Said and shown. The work asks whether to keep going between batches rather
                // than on every item, so there is a moment between the press and the window
                // going - and a button that stays live through it invites a second press at
                // something that is already stopping.
                if (stop != null)
                {
                    stop.IsEnabled = false;
                    stop.Content = "Stopping...";
                }
            });

            stop.HorizontalAlignment = HorizontalAlignment.Right;
            stop.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(stop);

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            // Closing it is asking to stop, not a way to leave the work running unwatched.
            window.Closing += (s, e) => job.IsCancelled = true;

            try
            {
                // Inside the guard with the work. Owning a window and applying a skin both talk
                // to the host's own frame, and either can refuse - which left the two lines that
                // are hardest to reason about as the two lines with nothing around them.
                Skin.Apply(window);
                Shell.HostWindow.Own(window);

                window.Show();
                work(job);
            }
            catch (Exception)
            {
                // The work's own failure is the caller's to report; this window only watches it.
            }
            finally
            {
                try
                {
                    window.Close();
                }
                catch (Exception)
                {
                    // A window already gone is the outcome wanted.
                }
            }
        }

        /// <summary>
        /// Run a row verb with the wait cursor up.
        ///
        /// A symbol under a table does its work on the UI thread — that is the right shape for
        /// something that is usually instant, and the wrong one on the day it is not: the first
        /// verb after a model change pays for a walk of the federation, and a window that stops
        /// repainting with an ordinary arrow over it reads as a plug-in that has crashed. The
        /// cursor is the cheapest honest signal, and it costs nothing on the ordinary press.
        /// </summary>
        /// <param name="act">What the verb does.</param>
        public static void Busy(Action act)
        {
            var was = Mouse.OverrideCursor;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                Safely(act);
            }
            finally
            {
                Mouse.OverrideCursor = was;
            }
        }

        private static System.Windows.Controls.Button Press(string caption, Action onClick, string? style)
        {
            var button = new System.Windows.Controls.Button { Content = caption };

            if (style != null) button.Wearing(style);
            else button.MinWidth = 88;

            if (onClick != null) button.Click += (s, e) => Safely(onClick);
            return button;
        }

        /// <summary>
        /// A text box that reports every change.
        ///
        /// <paramref name="width"/> is a floor rather than a fixed size: in a row it holds the
        /// old width, and in a stacked field it grows to the pane. A dock pane is 470 device
        /// pixels wide at its narrowest, and a fixed 180-pixel box beside a 120-pixel label wasted
        /// a third of that on nothing.
        /// </summary>
        /// <param name="value">Starting value.</param>
        /// <param name="onChanged">Called on each keystroke.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static TextBox Text(string? value, Action<string>? onChanged, double width = Wide.Name)
        {
            var box = new TextBox
            {
                Text = value ?? string.Empty,
                MinWidth = width,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (onChanged != null) box.TextChanged += (s, e) => onChanged(box.Text);
            return box;
        }

        /// <summary>A text box with grey example text in it until something is typed.</summary>
        /// <param name="value">Starting value.</param>
        /// <param name="placeholder">What to show while it is empty. An example, not an instruction.</param>
        /// <param name="onChanged">Called on each keystroke.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static TextBox Text(string? value, string placeholder, Action<string>? onChanged,
                                   double width = Wide.Name)
        {
            var box = Text(value, onChanged, width);

            // The skin's TextBox template shows Tag as the placeholder, so every text field in the
            // product gets one the same way and none of them can drift out of position.
            box.Tag = placeholder;
            return box;
        }

        /// <summary>
        /// How wide a field should be, by what goes in it.
        ///
        /// The product used eleven widths across thirty controls - 90, 128, 130, 140, 150, 180,
        /// 190, 200, 220, 240, 250, 260, 320 - each chosen at its own call site, so three
        /// dropdowns on one strip were three lengths and none of them related to the length of
        /// what they held. These five steps are the content classes the product actually has.
        ///
        /// The number is a minimum, not a fixed size: a field in a stretching column still fills
        /// it. The class is about how much can be read at rest.
        /// </summary>
        public static class Wide
        {
            /// <summary>A single character or a two-digit figure: a symbol, a separator, a floor.</summary>
            public const double Tight = 70;

            /// <summary>A figure and its unit: 250, 16 mm, 3.</summary>
            public const double Number = 100;

            /// <summary>One word from a known list: a status, a discipline, a level.</summary>
            public const double Word = 150;

            /// <summary>A name somebody typed: a test, a person, a saved set.</summary>
            public const double Name = 200;

            /// <summary>A phrase: a naming pattern, a sentence fragment, a search.</summary>
            public const double Phrase = 260;

            /// <summary>A file path or a file-name template.</summary>
            public const double Path = 340;
        }

        /// <summary>
        /// A search field: the box that narrows the list under it.
        ///
        /// Six screens searched with a plain text box and six different placeholders — "Search",
        /// "Search tests", "Find a test", "part of a name", "a test or a folder", "search the
        /// register" — so nothing distinguished a filter from a field you fill in until something
        /// had been typed. This carries the magnifier inside its own bounds and clears on Escape.
        ///
        /// The placeholder is what is being searched, not the word "search": the magnifier says
        /// that part.
        /// </summary>
        /// <param name="placeholder">What can be typed in, e.g. "a test or a folder".</param>
        /// <param name="onChanged">Called on each keystroke, including on the clear.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static TextBox Find(string placeholder, Action<string>? onChanged, double width = Wide.Name)
        {
            var box = Text(string.Empty, placeholder, onChanged, width);

            box.Wearing("Find");

            // Escape clears it, because a filter with no way out leaves a list that looks empty
            // and a box the eye has stopped seeing. Handled, or the key travels on and closes the
            // window the search is in.
            box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape || box.Text.Length == 0) return;

                box.Clear();
                e.Handled = true;
            };

            return box;
        }

        /// <summary>
        /// A text box that reports what was typed once, when the caret leaves it or Enter is
        /// pressed — not on every keystroke.
        ///
        /// For a box whose panel is rebuilt by what it reports. <see cref="Text"/> reports per
        /// keystroke, so a panel that redraws itself in response replaces the box mid-word: the
        /// first letter lands, the box it landed in is thrown away, and the caret is left nowhere.
        /// Every field in the product that sat on a self-redrawing panel behaved that way, which
        /// on a form of them reads as the keyboard being broken.
        ///
        /// Escape puts back what was there, so a box abandoned halfway is abandoned cleanly.
        /// </summary>
        /// <param name="value">Starting value.</param>
        /// <param name="placeholder">What to show while it is empty.</param>
        /// <param name="onSettled">Called once, with what the box now holds.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static TextBox Settled(string? value, string placeholder,
                                      Action<string>? onSettled, double width = Wide.Name)
        {
            var was = value ?? string.Empty;
            var box = Text(was, placeholder, null, width);

            void Report()
            {
                if (string.Equals(box.Text, was, StringComparison.Ordinal)) return;

                was = box.Text;
                onSettled?.Invoke(box.Text);
            }

            box.LostFocus += (s, e) => Safely(Report);

            box.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    Safely(Report);
                    return;
                }

                if (e.Key != System.Windows.Input.Key.Escape) return;

                e.Handled = true;
                box.Text = was;
                box.CaretIndex = box.Text.Length;
            };

            return box;
        }

        /// <summary>
        /// A folder: the ones that exist, and room to type one that does not.
        ///
        /// One control, because there were three. Filing a test as it is made was a bare text
        /// box, changing the folder of one that exists was a different bare text box, and "Move
        /// these..." was the only place that offered the folders already on the job - so the two
        /// places somebody actually files a test were the two that made them remember its name
        /// and spell it the same way twice. A folder that exists is a pick; a new one is typed
        /// over the top, which is what an editable dropdown is for.
        ///
        /// Empty means the root. The placeholder says so rather than the list carrying a "none"
        /// row that reads as a folder called None.
        /// </summary>
        /// <param name="value">The folder now, or null for the root.</param>
        /// <param name="folders">The folders this check already has.</param>
        /// <param name="onSettled">Called with the folder, or empty for the root.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static ComboBox Folder(string? value, IEnumerable<string> folders,
                                      Action<string>? onSettled, double width = Wide.Name)
        {
            var was = value ?? string.Empty;

            var box = new ComboBox
            {
                IsEditable = true,
                MinWidth = width,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Text = was,
            };

            foreach (var one in Listed(folders))
                if (!string.IsNullOrWhiteSpace(one)) box.Items.Add(one);

            void Report()
            {
                var now = (box.Text ?? string.Empty).Trim();

                if (string.Equals(now, was, StringComparison.Ordinal)) return;

                was = now;
                onSettled?.Invoke(now);
            }

            // Both, because either can be the last thing that happens: picking off the list
            // never loses focus if the field is the only control being used, and typing a new
            // name never raises a selection.
            box.LostFocus += (s, e) => Safely(Report);
            box.SelectionChanged += (s, e) => Safely(Report);

            box.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    Safely(Report);
                    return;
                }

                if (e.Key != System.Windows.Input.Key.Escape) return;

                e.Handled = true;
                box.Text = was;
            };

            return box;
        }

        /// <summary>A dropdown of fixed options.</summary>
        /// <param name="options">What can be picked.</param>
        /// <param name="selected">What is picked now, or null.</param>
        /// <param name="onChanged">Called with the new pick.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static ComboBox Choice(IEnumerable<string> options, string? selected,
                                      Action<string>? onChanged, double width = Wide.Name)
        {
            var box = new ComboBox
            {
                MinWidth = width,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };

            foreach (var option in options) box.Items.Add(option);

            if (selected != null && box.Items.Contains(selected)) box.SelectedItem = selected;

            if (onChanged != null)
                box.SelectionChanged += (s, e) =>
                {
                    if (box.SelectedItem is string pick) onChanged(pick);
                };

            return box;
        }

        /// <summary>
        /// Two to four exclusive choices, all visible at once.
        ///
        /// A dropdown hides the alternatives until it is opened, which for a choice of three is
        /// one click and one guess too many. Above four options a dropdown is still right, and
        /// this falls back to one.
        /// </summary>
        /// <param name="options">The choices, in order.</param>
        /// <param name="selected">Which is picked now, or null for the first.</param>
        /// <param name="onChanged">Called with the new pick.</param>
        public static UIElement Segmented(IReadOnlyList<string> options, string? selected, Action<string>? onChanged)
        {
            if (options == null || options.Count == 0) return Stack();

            // Beyond six the row is a list wearing a row's clothes, whatever it wraps onto.
            if (options.Count > 6) return Choice(options, selected, onChanged);

            // Wraps rather than running off the edge. A horizontal StackPanel measures its
            // children with infinite width, so a fourth segment inside a capped card was drawn
            // past the card's own edge with nothing saying it was there - which is what a source
            // picker offering "Everything loaded" looked like.
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            var buttons = new List<ToggleButton>();

            foreach (var option in options)
            {
                var button = new ToggleButton
                {
                    Content = option,
                    IsChecked = string.Equals(option, selected, StringComparison.Ordinal)
                                || (selected == null && ReferenceEquals(option, options[0])),
                };

                button.Wearing("Segment");

                buttons.Add(button);
                row.Children.Add(button);
            }

            // Settle which one is on BEFORE anything is listening.
            //
            // This line used to run after the handlers were wired, so a control built with a
            // value that matched none of its options turned the first one on and told the caller
            // somebody had picked it. Nobody had - the control was being drawn. On the clash
            // board's source segment that callback re-opens the whole app, so opening the
            // settings window silently rebuilt the screen and took every filter with it.
            if (!buttons.Any(b => b.IsChecked == true)) buttons[0].IsChecked = true;

            for (var i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                var pick = options[i];

                button.Checked += (s, e) =>
                {
                    foreach (var other in buttons)
                        if (!ReferenceEquals(other, button)) other.IsChecked = false;

                    onChanged?.Invoke(pick);
                };

                // Clicking the one already on must not turn everything off and leave no choice
                // made at all.
                button.Unchecked += (s, e) =>
                {
                    if (!buttons.Any(b => b.IsChecked == true)) button.IsChecked = true;
                };
            }

            var frame = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = row,
            };

            frame.Tint(Border.BackgroundProperty, "Nav");
            frame.Tint(Border.BorderBrushProperty, "Bd");
            return frame;
        }

        /// <summary>A checkbox.</summary>
        /// <param name="caption">What it says.</param>
        /// <param name="value">Whether it starts ticked.</param>
        /// <param name="onChanged">Called with the new state.</param>
        public static CheckBox Check(string caption, bool value, Action<bool>? onChanged)
        {
            var box = new CheckBox
            {
                Content = caption,
                IsChecked = value,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4),
            };

            if (onChanged != null)
            {
                box.Checked += (s, e) => onChanged(true);
                box.Unchecked += (s, e) => onChanged(false);
            }

            return box;
        }

        /// <summary>
        /// An on/off setting, drawn as a switch rather than a tick.
        ///
        /// A tick means "include this one" in a list; a switch means "this behaviour is on". They
        /// were the same control, and a column of them read as a list of things to select from.
        /// </summary>
        /// <param name="caption">What it says.</param>
        /// <param name="value">Whether it starts on.</param>
        /// <param name="onChanged">Called with the new state.</param>
        public static CheckBox Toggle(string caption, bool value, Action<bool>? onChanged)
        {
            var box = Check(caption, value, onChanged);
            box.Wearing("Switch");
            return box;
        }

        /// <summary>
        /// A control with its label above it.
        ///
        /// Above rather than in front: the pane is 470 device pixels at its narrowest, and a
        /// 120-pixel label column in front of a 180-pixel box left the control with less room than
        /// the word describing it.
        /// </summary>
        /// <param name="label">The label.</param>
        /// <param name="control">The control.</param>
        public static Panel Field(string label, UIElement control) => Field(label, control, null);

        /// <summary>A control with its label above it and a sentence of explanation below.</summary>
        /// <param name="label">The label.</param>
        /// <param name="control">The control.</param>
        /// <param name="hint">One line saying what to put in it, or null.</param>
        public static Panel Field(string label, UIElement control, string? hint) =>
            Field(label, control, hint, Need.Unsaid);

        /// <summary>
        /// A control with its label, an optional line of explanation, and what the field asks of
        /// the person filling it in.
        ///
        /// The last part is the one the product did not have. Nothing was ever marked required,
        /// the word "optional" appeared once in the whole of Views, and a value filled in from
        /// somewhere else - a cut elevation taken from a storey - was the same box as one typed by
        /// hand. So a form could only be learned by pressing its action and reading what it
        /// refused.
        /// </summary>
        /// <param name="label">The label.</param>
        /// <param name="control">The control.</param>
        /// <param name="hint">One line saying what to put in it, or null.</param>
        /// <param name="need">What the field asks of the person filling it in.</param>
        public static Panel Field(string label, UIElement control, string? hint, Need need)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            Free(control);

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = false };

            var name = new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            }.Wearing("Lbl");

            DockPanel.SetDock(name, Dock.Left);
            head.Children.Add(name);

            var asked = Asked(need);

            if (asked != null)
            {
                // Hard right, not next to the label. Docked left it sat wherever the label ended,
                // so a column of fields carried its badges down a ragged edge; against the field's
                // own right edge they line up with each other.
                DockPanel.SetDock(asked, Dock.Right);
                head.Children.Add(asked);
            }

            panel.Children.Add(head);

            // Deliberately not touching the control's alignment. Text and Choice already ask to
            // stretch; Segmented asks not to, because a three-way switch pulled across a whole
            // pane stops reading as a switch. Overriding here took that decision away from both.
            panel.Children.Add(control);

            if (!string.IsNullOrEmpty(hint)) panel.Children.Add(Small(hint!));

            return panel;
        }

        /// <summary>
        /// The line that says where a screen's edits go, or null when the screen makes no claim.
        ///
        /// Separate from <see cref="Foot"/> because several screens build their own footer row and
        /// would otherwise each write the sentence themselves, which is how four contracts came to
        /// be worded three ways.
        /// </summary>
        /// <param name="keeping">Where this screen's edits go, and when.</param>
        public static TextBlock? Kept(Keeping keeping)
        {
            if (keeping == Keeping.Unsaid) return null;

            var said = Small(keeping == Keeping.AsYouGo
                ? "Saved as you go"
                : keeping == Keeping.OnPress
                    ? "Not saved yet"
                    : "Saved");

            said.VerticalAlignment = VerticalAlignment.Center;

            said.ToolTip = keeping == Keeping.AsYouGo
                ? "Every change on this screen is written to the project as it is made."
                : keeping == Keeping.OnPress
                    ? "Changes on this screen are held until the action on the right is pressed."
                    : "Nothing is waiting to be written.";

            if (keeping == Keeping.OnPress) said.Tint(TextBlock.ForegroundProperty, "AmberTx");

            return said;
        }

        /// <summary>
        /// The mark that says what a field asks for, or null when it asks nothing in particular.
        /// </summary>
        /// <param name="need">What the field asks.</param>
        private static UIElement? Asked(Need need)
        {
            if (need == Need.Unsaid) return null;

            var said = new TextBlock
            {
                Text = need == Need.Required ? "Required"
                     : need == Need.Optional ? "Optional"
                     : "Automatic",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Required is the dark neutral, not amber. Amber is the warning tone in this palette,
            // and a form of six fields would have carried four warnings before anything was wrong.
            said.Tint(TextBlock.ForegroundProperty,
                      need == Need.Required ? "Text" : need == Need.Optional ? "Faint" : "BlueTx");

            var mark = new Border { Child = said };

            mark.Wearing(need == Need.Required ? "BadgeReq"
                       : need == Need.Optional ? "BadgeOpt" : "BadgeAuto");

            mark.ToolTip = need == Need.Required
                ? "This has to be filled in before the test can run."
                : need == Need.Optional
                    ? "The check runs without this."
                    : "Filled in from elsewhere. Typing over it keeps what you type.";

            return mark;
        }

        /// <summary>
        /// Take a control off whatever it is under now, so it can go somewhere else.
        ///
        /// A panel that redraws keeps some of its controls between draws - a box somebody is
        /// halfway through typing in has to survive the redraw or the typing is lost. Clearing the
        /// panel drops the wrapper those controls sat in but leaves each one still claiming that
        /// wrapper as its parent, and handing it to a second one is the exception that reads
        /// "already the logical child of another element" - a crash on the second press of a
        /// button that worked the first time.
        /// </summary>
        /// <param name="control">The control to disconnect. Anything not under a parent is left alone.</param>
        public static void Free(UIElement? control)
        {
            if (!(control is FrameworkElement element)) return;

            switch (element.Parent)
            {
                case Panel panel: panel.Children.Remove(control); break;
                case Border border: border.Child = null; break;
                case ContentControl holder: holder.Content = null; break;
            }
        }

        /// <summary>One step of a wizard: what it is called, and the surface it shows.</summary>
        public sealed class Step
        {
            /// <summary>Name one.</summary>
            /// <param name="title">What the step is called, in two or three words.</param>
            /// <param name="body">Its surface.</param>
            /// <param name="ready">
            /// Null when the step is finished, or the reason it is not, which is shown beside a
            /// disabled Next. A step with no check is always finished.
            /// </param>
            public Step(string title, Func<UIElement> body, Func<string?>? ready = null)
            {
                Title = title;
                Body = body;
                Ready = ready;
            }

            /// <summary>What the step is called.</summary>
            public string Title { get; }

            /// <summary>Its surface.</summary>
            public Func<UIElement> Body { get; }

            /// <summary>Why it is not finished, or null when it is.</summary>
            public Func<string?>? Ready { get; }
        }

        /// <summary>
        /// Several decisions taken in order, where each depends on the last.
        ///
        /// Use only where the dependency is real: a spreadsheet's columns cannot be mapped before
        /// the spreadsheet is read, and a write cannot be previewed before they are mapped. Where
        /// the decisions are independent, a form is the better control.
        ///
        /// The irreversible action belongs on the last step and nowhere else, so that "Write it
        /// into the model" is not sitting beside a file path still being typed.
        /// </summary>
        /// <param name="steps">The steps, in order.</param>
        /// <param name="finish">
        /// The caption of the main action on the last step, or null when the last step's own
        /// surface already carries it. A last step holding a two-stage commit does not want a third
        /// button above it repeating the same caption.
        /// </param>
        /// <param name="onFinish">What the main action does.</param>
        public static UIElement Steps(IReadOnlyList<Step> steps, string? finish, Action? onFinish)
        {
            if (steps == null || steps.Count == 0) return Stack();

            // Opened on the first thing still to be decided, not always on step one. A step that
            // carries a check and passes it has already been answered - by what the project
            // remembers, or by what is selected in the model - and making somebody press Next past
            // an answer they did not have to give is the same discourtesy as asking for it twice.
            //
            // Only a step that carries a check counts. One with no check is "always finished" by
            // definition, which is not evidence of anything, so the walk stops there rather than
            // running to the end of a wizard whose steps happen not to gate.
            var at = 0;

            while (at < steps.Count - 1 && steps[at].Ready != null && steps[at].Ready!() == null) at++;

            // The steps: every one across the top with its state visible, the way an export
            // suite draws them — the shape of the whole job before the first decision is taken.
            // The current step's surface takes the data area, and the way forward sits
            // bottom-right in the footer, where the one action always is.
            var spine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var body = new ContentControl();
            var why = Small(string.Empty);
            why.TextWrapping = TextWrapping.Wrap;
            var back = Quiet("Back", () => { });
            var next = Primary("Next", () => { });

            void Draw()
            {
                spine.Children.Clear();

                for (var i = 0; i < steps.Count; i++)
                {
                    var here = i;
                    var done = i < at;
                    var now = i == at;

                    var pip = new Border
                    {
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(11),
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = 11,
                            Foreground = now || done
                                ? (System.Windows.Media.Brush)System.Windows.Media.Brushes.White
                                : null,
                        },
                    };

                    pip.Tint(Border.BackgroundProperty, now || done ? "Accent" : "Track");

                    var label = new TextBlock
                    {
                        Text = steps[here].Title,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal,
                    };

                    if (!now) label.Wearing("Hint");

                    var row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 18, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    row.Children.Add(pip);

                    // A step already finished can be gone back to; one not reached yet cannot be
                    // jumped to, because the step after it is built out of the answer to this one.
                    if (done)
                    {
                        var jump = new System.Windows.Controls.Button { Content = label };

                        jump.Wearing("LinkBtn");
                        jump.Click += (s, e) => { at = here; Draw(); };
                        row.Children.Add(jump);
                    }
                    else
                    {
                        row.Children.Add(label);
                    }

                    spine.Children.Add(row);
                }

                // Detached before the next one goes in. A step's surface usually holds panels the
                // window keeps between steps, and handing one to a second parent while the first
                // still claims it is the "already the logical child" exception.
                body.Content = null;
                body.Content = steps[at].Body();

                var last = at == steps.Count - 1;
                var stop = steps[at].Ready?.Invoke();
                var offers = !last || finish != null;

                back.Visibility = at == 0 ? Visibility.Collapsed : Visibility.Visible;
                next.Visibility = offers ? Visibility.Visible : Visibility.Collapsed;
                next.Content = last ? finish : "Next";
                next.IsEnabled = stop == null;
                why.Text = stop ?? string.Empty;
                why.Visibility = stop == null ? Visibility.Collapsed : Visibility.Visible;
            }

            back.Click += (s, e) => { if (at > 0) { at--; Draw(); } };

            next.Click += (s, e) =>
            {
                if (at < steps.Count - 1) { at++; Draw(); return; }

                onFinish?.Invoke();

            };

            Draw();

            // The strip rides over the step's own surface; Back and Next go to the footer,
            // reading left to right the way the journey does.
            var forward = new StackPanel { Orientation = Orientation.Horizontal };
            why.VerticalAlignment = VerticalAlignment.Center;
            why.Margin = new Thickness(0, 0, 10, 0);
            why.MaxWidth = 300;
            back.Margin = new Thickness(0, 0, 8, 0);
            forward.Children.Add(why);
            forward.Children.Add(back);
            forward.Children.Add(next);

            return Bench.Surface(null, forward, Stack(spine, body));
        }

        /// <summary>
        /// Say something to the user that has nowhere else to go.
        ///
        /// A ribbon button that cannot open its tool has no screen to write the reason on yet, so
        /// it says it here rather than doing nothing - a button that appears to do nothing is the
        /// one failure a user cannot report.
        /// </summary>
        /// <param name="message">What went wrong, in words.</param>
        public static void Told(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Owned, like every other modal here. A native box with no owner disables every
            // top-level window on the thread - the host's own frame included - and re-enables
            // them from the owner it does not have, so the one line explaining why a tool did
            // not open would otherwise be the thing that killed Navisworks.
            var owner = Shell.HostWindow.Active;

            if (owner != null)
            {
                MessageBox.Show(owner, message, "CamelWorks", MessageBoxButton.OK,
                                MessageBoxImage.Information);
                return;
            }

            var frame = Shell.HostWindow.Frame();

            if (frame != null)
            {
                System.Windows.Forms.MessageBox.Show(frame, message, "CamelWorks",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(message, "CamelWorks", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Run something set off by a user press or a window closing, and survive it throwing.
        ///
        /// A WPF handler that throws does not fail locally: the exception leaves the handler,
        /// reaches the dispatcher and terminates the Navisworks process. The ribbon entry point has
        /// always been guarded, but everything reached after the first screen is up — a symbol
        /// under a table, a redraw run from a window's Closed event — had no guard, so one
        /// re-parented text box in one screen's redraw was enough to close the host. A fault in one
        /// screen should cost a message, not the session.
        /// </summary>
        /// <param name="act">What to run.</param>
        public static void Safely(Action? act)
        {
            if (act == null) return;

            try
            {
                act();
            }
            catch (Exception e)
            {
                // Report the exception type as well as its message. A message on its own — "the
                // given key was not present" — does not say where to look, and a host crash report
                // carries the type and nothing else. Both together make a fault reportable and
                // locatable.
                Told("Something went wrong: " + Describe(e)
                     + "\n\n" + e.GetType().Name + Where(e));
            }
        }

        /// <summary>The first frame of a fault that is ours, so a report says where to look.</summary>
        /// <param name="error">What was thrown.</param>
        private static string Where(Exception error)
        {
            var trace = error.StackTrace;

            if (string.IsNullOrEmpty(trace)) return string.Empty;

            foreach (var line in trace!.Split('\n'))
            {
                var said = line.Trim();

                if (said.IndexOf("CamelWorks", StringComparison.Ordinal) < 0) continue;

                var at = said.IndexOf(" in ", StringComparison.Ordinal);

                return "  ·  " + (at > 0 ? said.Substring(0, at) : said);
            }

            return string.Empty;
        }

        /// <summary>
        /// Take an element off whatever is holding it, so it can be put somewhere else.
        ///
        /// A screen that keeps a control in a field - a search box that must not lose what was
        /// typed in it, or the focus - and rebuilds the row around it hits
        /// "Specified element is already the logical child of another element" the second time it
        /// draws: clearing the panel's children detaches the row, not the row's contents. That
        /// throw used to arrive from inside a window's Closed handler, which is to say it closed
        /// Navisworks. Pass the kept control through here on the way back in.
        /// </summary>
        /// <param name="element">The element to free.</param>
        /// <returns>The same element, with no parent.</returns>
        public static T Loose<T>(T element) where T : UIElement
        {
            switch (LogicalTreeHelper.GetParent(element))
            {
                case Panel panel:
                    panel.Children.Remove(element);
                    break;
                case Decorator decorator when ReferenceEquals(decorator.Child, element):
                    decorator.Child = null;
                    break;
                case ContentControl holder when ReferenceEquals(holder.Content, element):
                    holder.Content = null;
                    break;
                case ItemsControl items:
                    items.Items.Remove(element);
                    break;
            }

            return element;
        }

        /// <summary>
        /// One tool in a list of them: what it is called, what it is for, and what it will do.
        ///
        /// The whole card is the button, because a row with a small button on the end makes
        /// somebody aim at the small button. The main action is shown rather than offered - it is
        /// there to answer "what happens if I open this", which is the question a list of tools
        /// exists to answer and the one a row of tab names never could.
        /// </summary>
        /// <param name="title">The tool's name.</param>
        /// <param name="purpose">What it is trying to do, in one sentence.</param>
        /// <param name="mainAction">The caption of its one main action, or null when it has none.</param>
        /// <param name="onOpen">Open it.</param>
        public static UIElement Row(string title, string purpose, string? mainAction, Action onOpen)
        {
            var text = new StackPanel();

            text.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });

            text.Children.Add(new TextBlock
            {
                Text = purpose,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            }.Wearing("Hint"));

            if (!string.IsNullOrEmpty(mainAction))
                text.Children.Add(new TextBlock
                {
                    Text = mainAction + " \u2192",
                    Margin = new Thickness(0, 6, 0, 0),
                }.Wearing("Lbl"));

            var card = new System.Windows.Controls.Button
            {
                Content = text,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };

            card.Wearing("RowCard");

            // Run through Safely, like every other menu line. This is the card an empty screen
            // offers its entry points on — "read the tests", "open a workbook", "pick the sets" —
            // so it is the first thing pressed, and it previously ran its action unguarded. An
            // exception under one of these was an unhandled dispatcher exception, which shows no
            // message and takes Navisworks down with a CLR error naming only the type.
            card.Click += (s, e) => Safely(onOpen);

            return card;
        }

        /// <summary>Two fields side by side, so a narrow pane is not one column of boxes.</summary>
        /// <param name="left">The first field.</param>
        /// <param name="right">The second field.</param>
        public static Panel Pair(UIElement left, UIElement right)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
            return grid;
        }

        // -----------------------------------------------------------------------------------
        // States
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// An empty result, said as a result.
        ///
        /// "No clashes matched these rules" is an answer; a blank panel is a bug report. Every
        /// screen that can come back with nothing uses this rather than showing nothing.
        /// </summary>
        /// <param name="what">What was looked for.</param>
        /// <param name="why">Why there is nothing, or what to try.</param>
        public static UIElement Empty(string what, string why) => Empty(what, why, null, null);

        /// <summary>
        /// An empty result, with the thing to do about it.
        ///
        /// "No clashes matched these rules" tells somebody what happened. A button that clears the
        /// rules tells them what to do about it, which is the difference between a dead end and a
        /// screen. Where there is an obvious next step, it belongs here rather than in a sentence
        /// telling them where to find it.
        /// </summary>
        /// <param name="what">What was looked for.</param>
        /// <param name="why">Why there is nothing, or what to try.</param>
        /// <param name="caption">What the way out says, or null for none.</param>
        /// <param name="onClick">The way out.</param>
        public static UIElement Empty(string what, string why, string? caption, Action? onClick) =>
            Empty(what, why, caption, onClick, null, null);

        /// <summary>
        /// The same, where the screen has two honest ways out rather than one.
        ///
        /// A first run of the Create tab is the case: a project's tests are made either one at a
        /// time or twenty at a time from the matrix, and offering only the first sends every new
        /// user down the slow road because it is the one with a button.
        /// </summary>
        /// <param name="what">What was looked for.</param>
        /// <param name="why">Why there is nothing, or what to try.</param>
        /// <param name="caption">What the way out says, or null for none.</param>
        /// <param name="onClick">The way out.</param>
        /// <param name="second">What the other way out says, or null for none.</param>
        /// <param name="onSecond">The other way out.</param>
        public static UIElement Empty(string what, string why, string? caption, Action? onClick,
                                      string? second, Action? onSecond)
        {
            var text = Stack(
                new TextBlock
                {
                    Text = what,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold,
                }.Wearing("Body"),
                new TextBlock
                {
                    Text = why,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                }.Wearing("Lbl"));

            // A Grid, not a horizontal StackPanel. A stack offers its children infinite width, so
            // wrapping never engages and the second sentence of an empty state runs off the right
            // of the pane - which is how "Loosen one, or clear the rules" became "Loosen one, o".
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // The glyph size and gutter a banner uses, so the text columns agree. An empty state
            // and a notice are one device — an icon and a sentence — and a 22-pixel glyph over a
            // 12-pixel gutter put this one's words nine pixels right of every banner under it.
            //
            // Top, not centre. Glyph centres itself, which is right beside one wrapped paragraph
            // and wrong beside a heading and a line under it: at 16 pixels it settled between the
            // two and read as belonging to the second one.
            var mark = Glyph(Glyphs.Empty, 16, "Faint", new Thickness(0, 3, 9, 0));
            mark.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(mark);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            // Centred in whatever it is dropped into, and never wider than a paragraph. Stretched
            // across an empty pane the card read as a banner over content that was not there, and
            // a sentence three feet wide is one nobody reads to the end of.
            //
            // Centred vertically as well as horizontally. A pane with nothing in it put the card
            // against the top edge and left the rest of the screen blank, which on the import tab
            // was 78% of it.
            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 16, 0, 4),
                MaxWidth = 460,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row,
            };

            // The primary, not a secondary. This is the only thing to do on a screen with nothing
            // on it, and it was drawn quieter than the disabled press in the footer below it.
            if (caption != null && onClick != null)
            {
                var ways = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0),
                };

                ways.Children.Add(Primary(caption, onClick));

                if (second != null && onSecond != null)
                {
                    var other = Quiet(second, onSecond);

                    other.Margin = new Thickness(Gap, 0, 0, 0);
                    other.VerticalAlignment = VerticalAlignment.Center;

                    ways.Children.Add(other);
                }

                text.Children.Add(ways);
            }

            frame.Tint(Border.BorderBrushProperty, "Bd");
            frame.Tint(Border.BackgroundProperty, "Card2");
            return frame;
        }

        /// <summary>Something failed, and this says what.</summary>
        /// <param name="message">The failure, in the user's terms.</param>
        public static UIElement Problem(string message) =>
            Banner(message, Glyphs.Error, "RedBg", "RedBd", "RedTx");

        /// <summary>Something worth knowing before pressing the button.</summary>
        /// <param name="message">The note.</param>
        public static UIElement Note(string message) =>
            Banner(message, Glyphs.Warn, "AmberBg", "AmberBd", "AmberTx");

        /// <summary>Something that went right, said once.</summary>
        /// <param name="message">What happened.</param>
        public static UIElement Done(string message) =>
            Banner(message, Glyphs.Tick, "OkBg", "OkBd", "Ok");

        /// <summary>Background worth having, that nobody has to act on.</summary>
        /// <param name="message">The note.</param>
        public static UIElement Info(string message) =>
            Banner(message, Glyphs.Info, "BlueBg", "BlueBd", "BlueTx");

        /// <summary>
        /// What a bulk write would do, drawn before it is done.
        ///
        /// Generalised from the rename preview, which is the only surface in the product that
        /// gets this right, and which every other bulk write needs: applying a returned pack to
        /// the board, writing an export, setting a status on picked rows.
        ///
        /// Seven properties, and each is load-bearing:
        ///
        /// - Every affected row is drawn, not only the changing ones, and the counts sum to the
        ///   total. A preview that lists only changes leaves the reader unable to tell "not
        ///   listed" from "not considered".
        /// - Before and after on one line, so the comparison needs no memory and no scrolling.
        /// - The reason a row is skipped is written in the row, in words.
        /// - A skipped row is muted, not red: a preview of forty skipped items must not read as
        ///   forty errors.
        /// - The caller's action carries the count this returns, so the button says what it will
        ///   do rather than "Apply".
        /// - A cap with an explicit remainder line, rather than a silent truncation.
        /// - Cause and effect near each other: the caller redraws this in place as its settings
        ///   change.
        /// </summary>
        /// <param name="rows">Every row the action would consider, in the order it considers them.</param>
        /// <param name="unit">What one row is, singular.</param>
        /// <param name="done">What a changed row has had done to it, past tense.</param>
        /// <param name="cannot">Why a refused row is refused, as a group.</param>
        /// <param name="cap">How many rows to draw before saying how many are left.</param>
        public static UIElement Plan(IReadOnlyList<Planned> rows, string unit, string done,
                                     string cannot, int cap = 200)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var changing = rows.Count(one => one.Changes);
            var refused = rows.Count(one => one.Refused != null);
            var same = rows.Count - changing - refused;

            var counts = new List<string> { Count(rows.Count, unit), changing + " " + done };

            if (same > 0) counts.Add(same + " already right");
            if (refused > 0) counts.Add(refused + " " + cannot);

            var held = Stack(Small(string.Join("  ·  ", counts)));

            var drawn = Stack();

            drawn.Margin = new Thickness(0, 8, 0, 0);

            foreach (var row in rows.Take(cap)) drawn.Children.Add(PlannedRow(row));

            if (rows.Count > cap)
            {
                var more = Small("and " + Count(rows.Count - cap, "more " + unit)
                                 + ", not drawn here.");

                more.Margin = new Thickness(0, 6, 0, 0);
                drawn.Children.Add(more);
            }

            held.Children.Add(drawn);

            return held;
        }

        /// <summary>How many rows of a plan would change, for the action's caption.</summary>
        /// <param name="rows">The plan.</param>
        public static int Changing(IReadOnlyList<Planned> rows) =>
            rows == null ? 0 : rows.Count(one => one.Changes);

        /// <summary>One row of a plan: what it is now, an arrow, and what it would become.</summary>
        /// <param name="row">The row.</param>
        private static UIElement PlannedRow(Planned row)
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 4) };

            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var was = PlannedCell(row.Was, "Muted");

            var arrow = PlannedCell("→", "Faint");

            arrow.HorizontalAlignment = HorizontalAlignment.Center;

            // Muted for a row this run leaves alone, accent for one it changes. Red is what the
            // product uses for something that went wrong, and a skipped row is not that.
            var becomes = PlannedCell(row.Refused ?? (row.Changes ? row.Becomes! : "unchanged"),
                                      row.Changes ? "Accent" : "Muted");

            Grid.SetColumn(arrow, 1);
            Grid.SetColumn(becomes, 2);

            line.Children.Add(was);
            line.Children.Add(arrow);
            line.Children.Add(becomes);

            return line;
        }

        /// <summary>One name in a plan, trimmed to its column and carried in full on the tip.</summary>
        /// <param name="said">The text.</param>
        /// <param name="ink">Which palette key it takes.</param>
        private static TextBlock PlannedCell(string said, string ink)
        {
            var made = new TextBlock
            {
                Text = said,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = said.Length == 0 ? null : said,
            }.Wearing("Body");

            // After the style, which turns wrapping on: with it on, a long name wraps to a second
            // line and the trimming never comes into it.
            made.TextWrapping = TextWrapping.NoWrap;
            made.Tint(TextBlock.ForegroundProperty, ink);

            return made;
        }

        /// <summary>
        /// The state a screen is in, said the same way on every screen that is in it.
        ///
        /// "No model is open" was rendered five ways across the clash manager: an empty-state card
        /// on Report, a footer sentence on Create, a yellow strip on the new-clash form, a body
        /// paragraph on Filter and a red strip at the top of the matrix. Four of the five were
        /// choosing their own tone, and one of them was an empty-collection component carrying an
        /// error condition.
        ///
        /// The tone comes from the state rather than from the screen, so a licence limit cannot be
        /// drawn as a failure and a stale board cannot be drawn as a note.
        /// </summary>
        /// <param name="stand">Which state the screen is in.</param>
        /// <param name="says">What is true, in one sentence.</param>
        /// <param name="caption">What the one thing to do about it is called, or null.</param>
        /// <param name="onClick">What that thing does.</param>
        public static UIElement Standing(Stand stand, string says, string? caption = null,
                                         Action? onClick = null)
        {
            var glyph = stand == Stand.Wrong ? Glyphs.Warn
                      : stand == Stand.Stale ? Glyphs.Clock
                      : stand == Stand.Limited ? Glyphs.Warn
                      : Glyphs.Info;

            var bg = stand == Stand.Wrong ? "RedBg" : stand == Stand.Blocked ? "BlueBg" : "AmberBg";
            var bd = stand == Stand.Wrong ? "RedBd" : stand == Stand.Blocked ? "BlueBd" : "AmberBd";
            var tx = stand == Stand.Wrong ? "RedTx" : stand == Stand.Blocked ? "BlueTx" : "AmberTx";

            return caption == null || onClick == null
                ? Banner(says, glyph, bg, bd, tx)
                : Banner(says, glyph, bg, bd, tx, caption, onClick);
        }

        /// <summary>Background worth having, with the one thing you can do about it.</summary>
        /// <param name="message">The note.</param>
        /// <param name="caption">What the button says.</param>
        /// <param name="onClick">What it does.</param>
        public static UIElement Info(string message, string caption, Action onClick) =>
            Banner(message, Glyphs.Info, "BlueBg", "BlueBd", "BlueTx", caption, onClick);

        /// <summary>
        /// Why a write did less than it promised, or nothing when it did what it said.
        ///
        /// Every tool that writes into the model ends the same way: a count, and then this. A
        /// count on its own is the message a user cannot act on - "wrote 0 of 412" reads as a
        /// broken tool, when almost always it means the document is read-only and the same press
        /// on the NWF would have worked.
        /// </summary>
        /// <param name="write">The transaction that was just committed.</param>
        public static UIElement? Refused(CamelWorks.Core.Abstractions.IModelWriteTransaction? write)
        {
            var problem = write?.LastProblem;

            return string.IsNullOrWhiteSpace(problem) ? null : Problem(problem!);
        }

        /// <summary>Put a write's refusal on a panel, when there was one.</summary>
        /// <param name="panel">Where the message goes.</param>
        /// <param name="write">The transaction that was just committed.</param>
        public static void Refused(Panel? panel,
                                   CamelWorks.Core.Abstractions.IModelWriteTransaction? write)
        {
            var said = Refused(write);

            if (panel != null && said != null) panel.Children.Add(said);
        }

        /// <summary>Say why a write did less than it promised, into a running job's pane.</summary>
        /// <param name="job">The job doing the writing.</param>
        /// <param name="write">The transaction that was just committed.</param>
        public static void Refused(Job? job,
                                   CamelWorks.Core.Abstractions.IModelWriteTransaction? write) =>
            job?.Trouble(write?.LastProblem);

        private static UIElement Banner(string message, string glyph, string wash, string edge,
                                        string ink, string? caption = null, Action? onClick = null)
        {
            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                LineHeight = 17,
            }.Tint(TextBlock.ForegroundProperty, ink);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(Glyph(glyph, 16, ink, new Thickness(0, 1, 9, 0)));

            UIElement said = text;

            if (caption != null && onClick != null)
            {
                // The button sits beside the sentence rather than under it. A strip is one line
                // tall and has to stay that way: it is above the screen's own content, and a
                // banner that grows pushes the work down the page every time it appears.
                var pair = new Grid { VerticalAlignment = VerticalAlignment.Center };

                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var press = Button(caption, onClick);
                press.Margin = new Thickness(12, 0, 0, 0);
                press.VerticalAlignment = VerticalAlignment.Center;

                Grid.SetColumn(press, 1);
                pair.Children.Add(text);
                pair.Children.Add(press);

                said = pair;
            }

            Grid.SetColumn(said, 1);
            row.Children.Add(said);

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11, 9, 11, 9),
                Margin = new Thickness(0, 5, 0, 5),
                Child = row,
            };

            frame.Tint(Border.BackgroundProperty, wash);
            frame.Tint(Border.BorderBrushProperty, edge);
            return frame;
        }

        // -----------------------------------------------------------------------------------
        // Glyphs
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// One drawn glyph, in a box of the size asked for.
        ///
        /// Drawn from path data rather than loaded from a file because these appear inside panes the
        /// user can theme: a PNG is one colour permanently, and a grey glyph on a dark card looks
        /// unfinished.
        ///
        /// Every glyph is drawn on its whole 24-unit grid rather than on the part of it the path
        /// touches. A path with no stretch reports the box from the origin to its far edge as its
        /// size, so a chevron between y 9 and y 15 measured as 18 wide by 15 tall: the box was not
        /// square, the empty top-left counted and the empty bottom-right did not, and scaling that
        /// to fit left the mark low in its button. This was reported on the export button's arrow
        /// and applied to every glyph, each off by a different amount because each path covers a
        /// different part of the grid. Placing the path on a 24 by 24 canvas gives them all the box
        /// they were drawn in.
        /// </summary>
        /// <param name="data">Path data, in a 24-unit box. One of <see cref="Glyphs"/>.</param>
        /// <param name="size">How big to draw it.</param>
        /// <param name="key">Which palette key to draw it in.</param>
        /// <param name="margin">Space around it.</param>
        public static Viewbox Glyph(string data, double size, string key, Thickness margin = default)
        {
            var grid = new Canvas
            {
                Width = 24,
                Height = 24,
                Background = System.Windows.Media.Brushes.Transparent,
            };

            grid.Children.Add(Arrow(data, key));

            return new Viewbox
            {
                Width = size,
                Height = size,
                Margin = margin,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Child = grid,
            };
        }

        private static System.Windows.Shapes.Path Arrow(string data, string key)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),

                // Two units on a 24-unit canvas is a hairline: at thirteen pixels that is 1.08
                // device pixels, anti-aliased to about half coverage, so a row of symbols came out
                // visibly lighter than the twelve-pixel type beside it and Muted's 5.5:1 contrast
                // reached the screen at about 3:1.
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.None,
            };

            path.Tint(System.Windows.Shapes.Shape.StrokeProperty, key);
            return path;
        }

        // -----------------------------------------------------------------------------------
        // Tables
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A button with a symbol and its caption, for a strip with room for both.
        ///
        /// On a row where every button is a symbol they read as a set to be learned. On the top
        /// strip, beside three buttons that carry their own captions, an unlabelled one reads as a
        /// symbol that failed to draw. The tooltip still carries the full sentence.
        /// </summary>
        /// <param name="glyph">The symbol.</param>
        /// <param name="caption">The word.</param>
        /// <param name="tip">The sentence, for the tip.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Verb(string glyph, string caption, string tip,
                                                          Action? onClick)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            row.Children.Add(Glyph(glyph, 15, "Muted", new Thickness(0, 0, 6, 0)));

            row.Children.Add(new TextBlock
            {
                Text = caption ?? string.Empty,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new System.Windows.Controls.Button
            {
                Content = row,
                ToolTip = tip,
                Padding = new Thickness(9, 4, 10, 5),
            };

            if (onClick != null) button.Click += (s, e) => Busy(onClick);

            return button;
        }

        /// <summary>
        /// Set on a container whose tables are the whole of it, so they grow to the room they get.
        ///
        /// Inherited, so a pane sets it once and every table drawn inside it afterwards is told -
        /// including the ones that replace the first when a screen redraws. The alternative, and
        /// what this replaced, was walking the pane at build time and clearing the cap on the
        /// tables that happened to exist by then: correct for exactly as long as nobody pressed
        /// anything.
        /// </summary>
        public static readonly DependencyProperty FillsProperty =
            DependencyProperty.RegisterAttached(
                "Fills",
                typeof(bool),
                typeof(Ui),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// Let the wheel reach this scroller from content that cannot use it.
        ///
        /// A table inside a scrolling pane is measured with unbounded height, so its own scroller
        /// has nothing to scroll — and a WPF scroller with nothing to scroll still marks the wheel
        /// handled, so the pane never sees it. The Create tab's list of tests did not respond to
        /// the wheel at all for this reason.
        ///
        /// Tunnelling, so it runs before the table's scroller does. Any scroller between the
        /// pointer and this one that can still move in the direction asked for keeps the wheel:
        /// a pane whose body scrolls itself is untouched.
        /// </summary>
        /// <param name="outer">The pane's scroller.</param>
        public static void WheelThrough(ScrollViewer outer)
        {
            if (outer == null) throw new ArgumentNullException(nameof(outer));

            outer.PreviewMouseWheel += (sender, args) =>
            {
                if (args.Handled || args.Delta == 0) return;

                var at = args.OriginalSource as DependencyObject;

                while (at != null && !ReferenceEquals(at, outer))
                {
                    if (at is ScrollViewer inner && Moves(inner, args.Delta)) return;

                    at = at is Visual || at is System.Windows.Media.Media3D.Visual3D
                        ? VisualTreeHelper.GetParent(at)
                        : LogicalTreeHelper.GetParent(at);
                }

                outer.ScrollToVerticalOffset(outer.VerticalOffset - args.Delta);
                args.Handled = true;
            };
        }

        /// <summary>Whether a scroller can still move the way the wheel was turned.</summary>
        /// <param name="inner">The scroller.</param>
        /// <param name="delta">The wheel, positive when scrolling up.</param>
        private static bool Moves(ScrollViewer inner, int delta) =>
            inner.ScrollableHeight > 0
            && (delta < 0 ? inner.VerticalOffset < inner.ScrollableHeight
                          : inner.VerticalOffset > 0);

        /// <summary>
        /// A list of values with the empties, the repeats and the disorder taken out.
        ///
        /// Written out thirty-two times across fifteen files, each time as
        /// <c>Where(non-empty).Distinct(OrdinalIgnoreCase).OrderBy(OrdinalIgnoreCase)</c>, and
        /// each time a chance to leave one of the three out. Every picker filled from what a
        /// document or a board actually holds comes through here.
        /// </summary>
        /// <param name="values">What was found, in any order, with anything in it.</param>
        public static IReadOnlyList<string> Listed(IEnumerable<string?>? values) =>
            (values ?? Array.Empty<string?>())
                .Where(one => !string.IsNullOrWhiteSpace(one))
                .Select(one => one!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(one => one, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Say that this element's tables should fill the height they are given.</summary>
        /// <param name="element">The container.</param>
        /// <param name="on">True to let its tables grow.</param>
        public static void SetFills(DependencyObject element, bool on) =>
            element.SetValue(FillsProperty, on);

        /// <summary>Whether this element's tables should fill the height they are given.</summary>
        /// <param name="element">The element to ask.</param>
        public static bool GetFills(DependencyObject element) =>
            (bool)element.GetValue(FillsProperty);

        /// <summary>
        /// Where a table is scrolled to, so a rebuild can put it back.
        ///
        /// A screen that rebuilds its table to show a change hands the list a new set of rows,
        /// and a new set starts at the top: delete a test four hundred rows down and the list
        /// returns to the first one, with the row that was under the pointer nowhere near it.
        /// The board solved this for its own list; every ListView on a bench has the same fault,
        /// so the pair lives here.
        ///
        /// Returns zero for a list with no scroller yet, which restores as no scroll.
        /// </summary>
        /// <param name="list">The table, or null.</param>
        public static double Scrolled(ItemsControl? list) => Scroller(list)?.VerticalOffset ?? 0;

        /// <summary>Put a rebuilt table back where the old one was.</summary>
        /// <param name="list">The table, or null.</param>
        /// <param name="offset">What <see cref="Scrolled"/> read before the rebuild.</param>
        public static void ScrollTo(ItemsControl? list, double offset)
        {
            if (list == null || offset <= 0) return;

            // After the layout pass, not during it. A list handed a new set of rows has not been
            // measured, so its ScrollableHeight is still zero and scrolling now clamps to the
            // top - which is the fault this is meant to fix, arriving a frame later.
            list.Dispatcher.BeginInvoke(new Action(() =>
            {
                var scroll = Scroller(list);

                if (scroll == null) return;

                scroll.ScrollToVerticalOffset(
                    Math.Min(offset, Math.Max(0, scroll.ScrollableHeight)));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>The scroller inside a list's template, once the template has been applied.</summary>
        /// <param name="list">The list.</param>
        private static ScrollViewer? Scroller(ItemsControl? list)
        {
            if (list == null) return null;

            list.ApplyTemplate();

            return Found(list);

            static ScrollViewer? Found(DependencyObject from)
            {
                var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(from);

                for (var i = 0; i < count; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(from, i);

                    if (child is ScrollViewer scroll) return scroll;

                    var deeper = Found(child);

                    if (deeper != null) return deeper;
                }

                return null;
            }
        }

        /// <summary>A table.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="columns">Header and width per column. A width of 0 sizes to content.</param>
        public static ListView Table(IEnumerable<TableRow> rows, params (string Header, double Width)[] columns)
        {
            var grid = new GridView { AllowsColumnReorder = false };
            var flexible = new List<GridViewColumn>();

            // Every fixed column with the width it asked for, kept so the asking figure survives
            // being compressed - otherwise one narrow pane would ratchet the columns down for good.
            var asked = new List<KeyValuePair<GridViewColumn, double>>();
            var fixedWidth = 0.0;

            for (var i = 0; i < columns.Length; i++)
            {
                // A CellTemplate rather than DisplayMemberBinding, which produces a bare TextBlock
                // with no trimming — so a cell too narrow for its text was cut mid-word with no
                // ellipsis, and the one row anybody reads in a run history is the one that says
                // why something stopped.
                var column = new GridViewColumn
                {
                    Header = columns[i].Header,
                    CellTemplate = Cell(i, Numeric(columns[i].Header)),
                };

                if (columns[i].Width > 0)
                {
                    column.Width = columns[i].Width;
                    asked.Add(new KeyValuePair<GridViewColumn, double>(column, columns[i].Width));
                    fixedWidth += columns[i].Width;
                }
                else
                {
                    flexible.Add(column);
                }

                grid.Columns.Add(column);
            }

            var built = rows.ToList();

            var table = new ListView
            {
                View = grid,
                ItemsSource = built,
                Margin = new Thickness(0, Gap, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

            table.SetValue(RowsProperty, built);

            // How tall a table may be is a question about the pane, not a number. It was 420,
            // which overflowed the dock pane at its 340 minimum and left most of a tall monitor
            // empty - the same figure doing the wrong thing at both ends.
            //
            // Bound to the scroll view the screen sits in, which is the height the pane actually
            // gave it. No cycle: a ScrollViewer's own height is what its parent allocated and does
            // not depend on how tall its content turns out to be. Two thirds, so a table never
            // fills the screen it is one part of, and a floor so a collapsed pane still shows rows
            // rather than a header and a scrollbar.
            //
            // Unless it is the whole pane. On a bench the table is not one part of a stage, it is
            // the thing the pane is for, and two thirds of a tall window left the bottom third
            // white with the rows still scrolling. The pane says so by setting Fills, which every
            // descendant inherits - asked here at Loaded, because a table has no parent at the
            // moment it is built and so cannot yet be asked what it is inside. Asking then rather
            // than walking the tree once at build time is the point: a table drawn again after a
            // read is a new table, and the walk had already happened.
            table.Loaded += (s, e) =>
            {
                if (!GetFills(table)) return;

                BindingOperations.ClearBinding(table, FrameworkElement.MaxHeightProperty);
                table.MaxHeight = double.PositiveInfinity;
                table.VerticalAlignment = VerticalAlignment.Stretch;

                // And no gap over it. The margin separates a table from whatever is stacked above
                // it on a stage; on a bench the pane's header is directly above and the margin is
                // an empty band under a heading with nothing in it.
                table.Margin = new Thickness(0, 0, 0, 2);
            };

            // Top, not the default stretch. A stretched element carrying a MaxHeight is centred
            // in what is left over, so a table capped at 420 in a taller pane drew its header
            // half way down with an empty band above it. The Loaded handler above puts Stretch
            // back on a table that fills, which is the only case where stretching is meant.
            table.VerticalAlignment = VerticalAlignment.Top;

            table.SetBinding(FrameworkElement.MaxHeightProperty, new Binding("ActualHeight")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                {
                    AncestorType = typeof(ScrollViewer),
                },
                Converter = new Portion(0.66, 180),
                FallbackValue = 420.0,
            });

            // A column given a width of zero takes what is left, shared with any other, rather
            // than being left to size itself to its content.
            //
            // Auto-sizing is what a GridViewColumn does with no width, and what it does is measure
            // once and settle on something narrower than the text it then clips - "12 minutes ago"
            // arriving as "12 mi" with eighty pixels of empty pane to its right. Measuring against
            // the pane instead is both correct and what a table in a dock pane should do anyway,
            // since the pane is resized far more often than the data changes.
            //
            // And a declared width is what a column wants, not what it must have. Twenty-nine of
            // the product's fifty-three tables declare more fixed width than the dock pane's own
            // minimum - the widest asks 750 in a pane that can be dragged to 470 - so holding
            // those figures meant a horizontal scrollbar in the one place there is no room for
            // one. When the pane is narrower than the table wants, every column gives up width in
            // proportion, down to a floor that keeps a header readable; when it is wider they get
            // what they asked for and the flexible ones take the rest.
            // Set true by the first header-gripper drag: from then on the columns are the
            // user's, and the auto-fit that would snap them back on every window resize stays
            // out of the way.
            var userSized = false;

            void Fit()
            {
                if (userSized) return;

                // The room a vertical scrollbar and the two borders take. Guessed slightly high:
                // a column two pixels short of the edge looks intentional, and one two pixels
                // over brings the horizontal scrollbar back.
                var room = table.ActualWidth - 28;
                if (room <= 0) return;

                // What the flexible columns need before anything is called spare.
                var least = flexible.Count * 48;

                if (room < fixedWidth + least && fixedWidth > 0)
                {
                    // One ratio for all of them, so the columns keep their relative sizes and the
                    // table still reads as the same table. Floored, because past a point the
                    // honest answer is a narrow column and a tooltip rather than four characters.
                    var ratio = Math.Max(0.4, (room - least) / fixedWidth);

                    foreach (var entry in asked)
                        entry.Key.Width = Math.Max(40, entry.Value * ratio);

                    foreach (var column in flexible) column.Width = 48;
                    return;
                }

                foreach (var entry in asked) entry.Key.Width = entry.Value;

                if (flexible.Count == 0) return;

                var spare = room - fixedWidth;
                if (spare <= 0) return;

                foreach (var column in flexible) column.Width = Math.Max(70, spare / flexible.Count);
            }

            table.SizeChanged += (s, e) => Fit();
            table.Loaded += (s, e) => Fit();

            table.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler(
                    (s, e) => userSized = true));

            // Click a header to sort by that column; click it again to turn the sort around.
            // Every table in the product, one behaviour — a clash manager sorting eighteen
            // thousand results by status is the same gesture as a job list sorting by name.
            // Numeric when every filled cell reads as a number, so "18,844" sorts after "620"
            // instead of before it.
            var sortedBy = -1;
            var ascending = true;

            table.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
                new RoutedEventHandler((s, e) =>
                {
                    if (!(e.OriginalSource is GridViewColumnHeader header) || header.Column == null)
                        return;

                    var index = grid.Columns.IndexOf(header.Column);
                    if (index < 0) return;

                    // The rows the table was built from, not whatever its ItemsSource happens to
                    // be now. A table that groups its rows by folder has a CollectionView there
                    // rather than a list, so this test used to fail on exactly the table that
                    // most needs sorting - the Create tab's, where the headers did nothing at
                    // all.
                    var current = Held(table);

                    if (current.Count == 0) return;

                    ascending = sortedBy == index ? !ascending : true;
                    sortedBy = index;

                    // One pass over the column, deciding whether it reads as numbers and keeping
                    // each row's key as it goes. Asking twice parsed every cell of an
                    // eighteen-thousand-row column twice over before the sort began.
                    var numeric = true;
                    var keys = new double[current.Count];

                    for (var i = 0; i < current.Count; i++)
                    {
                        var cell = current[i][index];

                        if (cell.Length == 0) { keys[i] = double.MinValue; continue; }

                        if (double.TryParse(cell, NumberStyles.Any, CultureInfo.InvariantCulture,
                                            out var value))
                            keys[i] = value;
                        else { numeric = false; break; }
                    }

                    var order = Enumerable.Range(0, current.Count);

                    var sorted = (numeric
                        ? order.OrderBy(i => keys[i])
                        : order.OrderBy(i => current[i][index], StringComparer.OrdinalIgnoreCase))
                        .Select(i => current[i]).ToList();

                    if (!ascending) sorted.Reverse();

                    Refill(table, sorted);
                }));

            // A 3px bar down the left of the row, not the row's text.
            //
            // Painting every cell with the tone made a job name green in one row and red in the
            // next, and turned a Model column into a rainbow — colouring the one fact on the row
            // that is the same in every row. The bar says the same thing and says it once.
            table.ItemContainerGenerator.StatusChanged += (s, e) =>
            {
                if (table.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    return;

                // The realised containers, not every item. ContainerFromItem is a walk of the
                // whole list, so asking it once per item was quadratic in the rows on every
                // scroll batch — and a recycled container kept the colour of the row it used to
                // hold, which is why the brush is set either way.
                foreach (var container in Realised(table))
                {
                    if (!(container.DataContext is TableRow row)) continue;

                    container.BorderBrush = row.Tone == Tone.Plain
                        ? System.Windows.Media.Brushes.Transparent
                        : Skin.Ink(row.Tone);
                }
            };

            return table;
        }

        /// <summary>Where a table keeps the rows it was built from, whatever it is showing.</summary>
        private static readonly DependencyProperty RowsProperty =
            DependencyProperty.RegisterAttached("Rows", typeof(List<TableRow>), typeof(Ui),
                                                new PropertyMetadata(null));

        /// <summary>The rows a table was built from.</summary>
        /// <param name="table">The table.</param>
        private static List<TableRow> Held(ListView table) =>
            table.GetValue(RowsProperty) as List<TableRow> ?? new List<TableRow>();

        /// <summary>
        /// Show these rows, keeping whatever shape the table was given.
        ///
        /// A grouped table is showing a view over its rows rather than the rows, so putting a
        /// list back in its place would take the folder bands off the first time somebody sorted
        /// it. The grouping is rebuilt over the new order instead.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <param name="rows">The rows, in the order they are to show.</param>
        private static void Refill(ListView table, List<TableRow> rows)
        {
            table.SetValue(RowsProperty, rows);

            var groups = (table.ItemsSource as System.ComponentModel.ICollectionView)?
                .GroupDescriptions;

            if (groups == null || groups.Count == 0)
            {
                table.ItemsSource = rows;
                return;
            }

            var grouped = new CollectionViewSource { Source = rows };

            foreach (var one in groups) grouped.GroupDescriptions.Add(one);

            table.ItemsSource = grouped.View;
        }

        /// <summary>
        /// The row containers a virtualised list has actually made.
        ///
        /// Walked out of the visual tree, because that is the only place a virtualising panel says
        /// which rows exist right now.
        /// </summary>
        /// <param name="table">The list.</param>
        private static IEnumerable<ListViewItem> Realised(DependencyObject table)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(table);

            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(table, i);

                if (child is ListViewItem row) { yield return row; continue; }

                foreach (var deeper in Realised(child)) yield return deeper;
            }
        }

        /// <summary>
        /// One cell: trimmed rather than cut, and with the whole value on a tooltip.
        /// </summary>
        /// <param name="index">Which cell of the row.</param>
        /// <param name="numeric">Whether to line the digits up on the right.</param>
        private static DataTemplate Cell(int index, bool numeric)
        {
            var binding = new Binding("[" + index.ToString(CultureInfo.InvariantCulture) + "]");
            var text = new FrameworkElementFactory(typeof(TextBlock));

            text.SetBinding(TextBlock.TextProperty, binding);
            text.SetBinding(FrameworkElement.ToolTipProperty, binding);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            if (numeric)
            {
                // Digits in a column have to line up column to column, exactly as Figure already
                // takes the trouble to do. Left-aligned, "412" sat under the "8,2" of "8,204".
                text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
                text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

                text.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty,
                              FontNumeralAlignment.Tabular);
            }

            return new DataTemplate { VisualTree = text };
        }

        /// <summary>
        /// Whether a column holds numbers, from what it is called.
        ///
        /// By name rather than by a flag on every call site: nineteen panels build tables and the
        /// headings they use for counts are a short, stable list.
        /// </summary>
        /// <param name="header">The column heading.</param>
        private static bool Numeric(string? header)
        {
            if (string.IsNullOrEmpty(header)) return false;

            foreach (var word in NumberHeadings)
                if (header!.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        /// <summary>Column headings whose cells are counts or measurements.</summary>
        private static readonly string[] NumberHeadings =
        {
            "Elements", "Clashes", "Results", "Count", "Size", "Rows", "Models", "Added",
            "Removed", "Moved", "Changed", "Pass", "Fail", "Applies to", "Total", "Clear (",
            "Elevation", "Nodes",
        };

        /// <summary>Call back when a row is picked.</summary>
        /// <param name="table">The table.</param>
        /// <param name="onPick">Called with the row's item.</param>
        public static ListView OnPick(this ListView table, Action<object?> onPick)
        {
            table.SelectionChanged += (s, e) =>
            {
                if (table.SelectedItem is TableRow row) onPick(row.Source);
            };

            return table;
        }

        /// <summary>The item behind the selected row, or null.</summary>
        /// <param name="table">The table.</param>
        public static object? Picked(this ListView table) => (table.SelectedItem as TableRow)?.Source;

        /// <summary>
        /// Let more than one row be picked at once.
        ///
        /// Extended rather than Multiple: click, shift-click and control-click are what every list
        /// on the machine already does, and a list where a plain click adds to the selection
        /// instead of replacing it surprises people into selecting things they did not mean to.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <summary>
        /// Draw one column as a tick box rather than as text.
        ///
        /// A column of ticks and blanks says the same thing as a column of boxes and reads as a
        /// decoration rather than a control: on a board where every other list of things to
        /// include has a box, one that draws a tick is one somebody tries to click and cannot.
        /// Rows set <see cref="TableRow.Ticked"/> and <see cref="TableRow.OnTicked"/>.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <param name="column">Which column, from nought.</param>
        public static ListView Ticking(this ListView table, int column)
        {
            if (!(table.View is GridView grid) || column < 0 || column >= grid.Columns.Count)
                return table;

            var box = new FrameworkElementFactory(typeof(CheckBox));

            box.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Ticked")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });

            box.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            grid.Columns[column].CellTemplate = new DataTemplate { VisualTree = box };

            return table;
        }

        /// <summary>
        /// Draw one column as its row's <see cref="TableRow.Symbol"/> and then its text.
        ///
        /// For a column whose value is a kind rather than a measurement. A list of tests where
        /// every row carries the same mark cannot be sorted by eye, and the kind has to be read
        /// word by word; a mark per kind sorts the list at a glance and the word stays beside it
        /// for anybody who does not know the marks.
        ///
        /// Rows with no symbol draw the text alone, so a table may mix kinds that have a mark with
        /// kinds that do not.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <param name="column">Which column, from nought.</param>
        public static ListView Symbolled(this ListView table, int column)
        {
            if (!(table.View is GridView grid) || column < 0 || column >= grid.Columns.Count)
                return table;

            var cell = new FrameworkElementFactory(typeof(ContentPresenter));

            // The whole row, converted to a built visual, rather than a factory tree with the
            // path bound: a glyph is a path on its own 24-unit canvas inside a Viewbox, and Glyph
            // already assembles that. One assembly of it, used everywhere.
            cell.SetBinding(ContentPresenter.ContentProperty, new Binding(".")
            {
                Converter = new SymbolCell(column),
            });

            grid.Columns[column].CellTemplate = new DataTemplate { VisualTree = cell };

            return table;
        }

        /// <summary>
        /// Put the rows in bands, one per <see cref="TableRow.Filed"/> value, with a heading over
        /// each saying what it is and how many rows it holds.
        ///
        /// A folder printed in a cell is a column, not a folder: on forty tests filed into six
        /// folders the same six words are printed forty times and the filing has to be read back
        /// out of them. The caller sorts the rows into band order first — a band is a run of
        /// adjacent rows, not a query.
        /// </summary>
        /// <param name="table">The table.</param>
        public static ListView Filed(this ListView table)
        {
            var grouped = new CollectionViewSource { Source = table.ItemsSource };

            grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TableRow.Filed)));

            table.ItemsSource = grouped.View;

            // Grouping turns virtualisation off unless this says otherwise, and a federation with
            // six hundred tests is a list that has to stay virtualised.
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(table, true);

            // The band, as a toggle carrying the header, with the rows under it.
            //
            // A GroupStyle header alone is a strip that cannot be pressed: a project with a
            // hundred and sixty tests in a dozen folders had no way to put a folder away, so
            // finding the one being worked meant scrolling past every other. The band is a
            // ToggleButton with no chrome of its own - it draws exactly the header it drew
            // before, plus the chevron - and the rows fold when it is off.
            var band = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.ToggleButton));

            band.Name = "band";
            band.SetValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            band.SetValue(Control.CursorProperty, Cursors.Hand);
            band.SetValue(FrameworkElement.FocusVisualStyleProperty, null);
            band.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);

            band.SetBinding(ContentControl.ContentProperty,
                            new Binding(".") { Converter = new BandHead() });

            // Its own template, or the platform draws a push button round the band.
            var bare = new FrameworkElementFactory(typeof(ContentPresenter));

            band.SetValue(Control.TemplateProperty,
                          new ControlTemplate(typeof(System.Windows.Controls.Primitives.ToggleButton))
                          {
                              VisualTree = bare,
                          });

            var rows = new FrameworkElementFactory(typeof(ItemsPresenter));

            rows.Name = "rows";

            var whole = new FrameworkElementFactory(typeof(StackPanel));

            whole.AppendChild(band);
            whole.AppendChild(rows);

            var shape = new ControlTemplate(typeof(GroupItem)) { VisualTree = whole };

            shape.Triggers.Add(new Trigger
            {
                SourceName = "band",
                Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                Value = false,
                Setters =
                {
                    new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "rows"),
                },
            });

            table.GroupStyle.Add(new GroupStyle
            {
                ContainerStyle = new Style(typeof(GroupItem))
                {
                    Setters = { new Setter(Control.TemplateProperty, shape) },
                },
            });

            return table;
        }

        /// <summary>
        /// Draw one column as the status chip the board draws, rather than as its word.
        ///
        /// The same five statuses were three visual languages: filled chips on the Manage rows,
        /// coloured figures in its counter strip, and plain body text in both of Exchange's
        /// tables. A coordinator who learns the colours on the board could not use them on the
        /// tab where the work goes out to a trade.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <param name="column">Which column, from nought. Its cells hold a status word.</param>
        public static ListView Stated(this ListView table, int column)
        {
            if (!(table.View is GridView grid) || column < 0 || column >= grid.Columns.Count)
                return table;

            var cell = new FrameworkElementFactory(typeof(ContentPresenter));

            cell.SetBinding(ContentPresenter.ContentProperty, new Binding(".")
            {
                Converter = new StatusCell(column),
            });

            grid.Columns[column].CellTemplate = new DataTemplate { VisualTree = cell };

            return table;
        }

        public static ListView Multi(this ListView table)
        {
            table.SelectionMode = SelectionMode.Extended;
            return table;
        }

        /// <summary>The items behind every selected row, in the order the table holds them.</summary>
        /// <param name="table">The table.</param>
        public static IReadOnlyList<object> PickedMany(this ListView table)
        {
            var picked = new List<object>();

            foreach (var selected in table.SelectedItems)
                if (selected is TableRow row && row.Source != null) picked.Add(row.Source);

            return picked;
        }

        /// <summary>One section of a configuration window.</summary>
        public sealed class Page
        {
            /// <summary>Name it and say how to build it.</summary>
            /// <param name="name">How it reads in the section list.</param>
            /// <param name="build">Builds the section's fields, on first open.</param>
            public Page(string name, Func<UIElement> build)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
                Build = build ?? throw new ArgumentNullException(nameof(build));
            }

            internal string Name { get; }

            internal Func<UIElement> Build { get; }

            /// <summary>How it reads in the section list.</summary>
            public string Named => Name;

            /// <summary>
            /// Build the section on its own.
            ///
            /// For a harness that has no window to put it in: a Ui.Configure window needs a
            /// screen, so its pages were the one part of this product no picture could contain.
            /// </summary>
            public UIElement Drawn() => Build();
        }

        /// <summary>
        /// The options a screen is set up with once, in a window of their own.
        ///
        /// Keeping forty options beside two tables makes the work and the setup compete for the
        /// same space. The few filters used continuously stay on the surface; everything decided
        /// once per job — which properties are columns, what the status verb writes, marker size,
        /// where the markups live — lives here, one section at a time.
        ///
        /// Edits apply as they are made, so there is no Apply button to miss. Closing tells the
        /// caller to redraw, because a column added here changes the grid behind it.
        /// </summary>
        /// <param name="title">The window's title.</param>
        /// <param name="onClosed">Called when it closes, for the screen to redraw itself.</param>
        /// <param name="sections">The sections, in the order they read.</param>
        public static void Configure(string title, Action? onClosed, params Page[] sections)
        {
            if (sections == null || sections.Length == 0) return;

            var panel = new ContentControl();

            // One section, no list. Seven symbols on the action row that each open the same window
            // showing the same seven-item list read as one button drawn seven times: the thing the
            // symbol named was on the screen, but so was everything else, and nothing about the
            // window said which press had opened it. A symbol that names a section now opens that
            // section and only it, titled with its own name.
            if (sections.Length == 1)
            {
                Section(sections[0].Name, sections[0], onClosed);
                return;
            }

            var list = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
            var buttons = new List<System.Windows.Controls.Button>();

            void ShowSection(int at)
            {
                for (var i = 0; i < buttons.Count; i++)
                {
                    buttons[i].Wearing(i == at ? "RailRowOn" : "RailRow");
                }

                try
                {
                    panel.Content = sections[at].Build();
                }
                catch (Exception e)
                {
                    panel.Content = Problem("This section could not be opened: " + Describe(e));
                }
            }

            for (var i = 0; i < sections.Length; i++)
            {
                var at = i;

                var button = new System.Windows.Controls.Button
                {
                    Content = sections[i].Name,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 2),
                };

                button.Click += (s, e) => ShowSection(at);

                buttons.Add(button);
                list.Children.Add(button);
            }

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = list,
                },
            };

            rail.Tint(Border.BackgroundProperty, "Pane");
            rail.Tint(Border.BorderBrushProperty, "Bd");
            body.Children.Add(rail);

            var stage = new Border
            {
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = panel,
                    Padding = new Thickness(18, 14, 18, 16),
                },
            };

            stage.Tint(Border.BackgroundProperty, "Card");
            Grid.SetColumn(stage, 1);
            body.Children.Add(stage);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(body);

            var window = new Window
            {
                Title = "CamelWorks - " + title,
                Width = 720,
                Height = 520,
                MinWidth = 520,
                MinHeight = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var done = Primary("Done", () => window.Close());
            done.MinWidth = 92;

            var foot = new Border
            {
                Padding = new Thickness(14, 9, 14, 10),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new ContentControl { Content = done, HorizontalAlignment = HorizontalAlignment.Right },
            };

            foot.Tint(Border.BackgroundProperty, "Pane");
            foot.Tint(Border.BorderBrushProperty, "Bd");
            Grid.SetRow(foot, 1);
            root.Children.Add(foot);

            window.Content = root;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);
            ShowSection(0);

            if (onClosed != null) window.Closed += (s, e) => Safely(onClosed);

            window.ShowDialog();
        }

        /// <summary>
        /// One section, in a window of its own — what a symbol on the action row opens.
        /// </summary>
        /// <param name="title">The section's name, which is the window's title.</param>
        /// <param name="section">The section to show.</param>
        /// <param name="onClosed">Called when it closes, for the screen to redraw itself.</param>
        private static void Section(string title, Page section, Action? onClosed)
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement built;

            try
            {
                built = section.Build();
            }
            catch (Exception e)
            {
                built = Problem("This section could not be opened: " + Describe(e));
            }

            var stage = new Border
            {
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = built,
                    Padding = new Thickness(18, 16, 18, 16),
                },
            };

            stage.Tint(Border.BackgroundProperty, "Card");
            root.Children.Add(stage);

            var window = new Window
            {
                Title = "CamelWorks - " + title,
                Width = 560,
                // Sized to the section rather than to the largest of seven. A window that opens
                // three quarters empty reads as a screen that failed to load its content.
                SizeToContent = SizeToContent.Height,
                MaxHeight = 640,
                MinWidth = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var done = Primary("Done", () => window.Close());
            done.MinWidth = 92;

            var foot = new Border
            {
                Padding = new Thickness(14, 9, 14, 10),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new ContentControl
                {
                    Content = done,
                    HorizontalAlignment = HorizontalAlignment.Right,
                },
            };

            foot.Tint(Border.BackgroundProperty, "Pane");
            foot.Tint(Border.BorderBrushProperty, "Bd");
            Grid.SetRow(foot, 1);
            root.Children.Add(foot);

            window.Content = root;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            if (onClosed != null) window.Closed += (s, e) => Safely(onClosed);

            window.ShowDialog();
        }

        /// <summary>One entry on a table's right-click menu.</summary>
        public sealed class OnMenu
        {
            /// <summary>Name it and say what it does.</summary>
            /// <param name="caption">How it reads. <c>{n}</c> becomes the number of picked rows.</param>
            /// <param name="act">What to do with the picked rows.</param>
            public OnMenu(string caption, Action<IReadOnlyList<object>> act)
            {
                Caption = caption ?? throw new ArgumentNullException(nameof(caption));
                Act = act ?? throw new ArgumentNullException(nameof(act));
            }

            internal string Caption { get; }

            internal Action<IReadOnlyList<object>> Act { get; }
        }

        /// <summary>
        /// A right-click menu on a table's rows.
        ///
        /// The verbs under a grid are the ones somebody uses every minute; the right-click is where
        /// a person looks for the one they use when something occurs to them — which is exactly
        /// when an issue gets raised. Right-clicking a row that is not in the current selection
        /// picks it first, the way every list on the machine behaves: nobody expects a menu to act
        /// on rows somewhere else on screen.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <param name="entries">The menu, in order.</param>
        public static ListView Menu(this ListView table, params OnMenu[] entries)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (entries == null || entries.Length == 0) return table;

            var menu = new ContextMenu();

            table.PreviewMouseRightButtonDown += (s, e) =>
            {
                var row = Row(e.OriginalSource as DependencyObject);

                if (row != null && !row.IsSelected)
                {
                    table.SelectedItems.Clear();
                    row.IsSelected = true;
                }
            };

            menu.Opened += (s, e) =>
            {
                var picked = table.PickedMany();

                menu.Items.Clear();

                foreach (var entry in entries)
                {
                    var here = entry;

                    var item = new MenuItem
                    {
                        Header = Worded(here.Caption, picked.Count),
                        IsEnabled = picked.Count > 0,
                    };

                    item.Click += (a, b) => here.Act(table.PickedMany());

                    menu.Items.Add(item);
                }
            };

            table.ContextMenu = menu;

            return table;
        }

        /// <summary>
        /// A verb press, refused while the host is busy.
        ///
        /// Every one of these touches the document - selecting, isolating, writing - and a press
        /// that arrived while a long job was pumping would run it on top of that job.
        /// </summary>
        private static void Verb(OnPicked action, ListView table, Panel said)
        {
            if (IsBusy)
            {
                said.Children.Clear();
                said.Children.Add(Note("Something else is still running."));
                return;
            }

            action.Act(table.PickedMany(), said);
        }

        /// <summary>The row a click landed on, or null when it landed on the header or the gap.</summary>
        private static ListViewItem? Row(DependencyObject? from)
        {
            while (from != null && !(from is ListViewItem))
                from = VisualTreeHelper.GetParent(from);

            return from as ListViewItem;
        }

        /// <summary>
        /// The picked rows of a table, as tab separated lines.
        ///
        /// Picking hands back the object behind a row, not the row, so getting the text back means
        /// asking the table which rows are selected. Rebuilding the strings from the source a
        /// second way is how a copied line stops matching the line on screen.
        /// </summary>
        /// <param name="table">The table the rows are in.</param>
        /// <param name="picked">What <see cref="Picking"/> handed back.</param>
        public static IEnumerable<string> Lines(ListView table, IReadOnlyList<object> picked)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var wanted = new HashSet<object>(picked ?? Array.Empty<object>());

            foreach (var item in table.Items)
            {
                if (!(item is TableRow row) || row.Source == null) continue;
                if (!wanted.Contains(row.Source)) continue;

                var cells = new string[row.Count];
                for (var i = 0; i < row.Count; i++) cells[i] = row[i];

                yield return string.Join("\t", cells);
            }
        }

        /// <summary>
        /// Put lines on the clipboard, and say so.
        ///
        /// Tab separated, because the destination is a mail or a sheet and tabs paste as columns
        /// where commas paste as a sentence.
        ///
        /// Here rather than in each screen because the third copy of the same try/catch is the one
        /// that gets the message wrong - and Clipboard.SetText really does throw, on a machine
        /// where another process is holding the clipboard open, which is not rare.
        /// </summary>
        /// <param name="lines">What to copy, one per line.</param>
        /// <param name="say">Where to write what happened.</param>
        /// <param name="unit">What one line is, for the sentence.</param>
        public static void Copy(IEnumerable<string> lines, Panel say, string unit = "row")
        {
            if (say == null) throw new ArgumentNullException(nameof(say));

            say.Children.Clear();

            var text = (lines ?? Array.Empty<string>()).ToList();

            if (text.Count == 0)
            {
                say.Children.Add(Note("Nothing to copy."));
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(string.Join("\r\n", text));
                say.Children.Add(Done("Copied " + Count(text.Count, unit) + "."));
            }
            catch (Exception error)
            {
                say.Children.Add(Problem("Could not copy it: " + Describe(error)));
            }
        }

        /// <summary>One thing that can be done to the rows somebody has picked.</summary>
        public sealed class OnPicked
        {
            /// <summary>Name one.</summary>
            /// <param name="caption">
            /// The button's caption, with <c>{n}</c> where the count goes - "Assign {n}...". The
            /// count belongs in the caption and not only in a line above it, because the button is
            /// what somebody reads immediately before pressing it, and "Assign 40..." stops a
            /// press that "Assign..." does not.
            /// </param>
            /// <param name="act">
            /// What to do with the picked rows, and where to say what happened. The panel is the
            /// bar's own, handed over rather than chosen by the caller: three screens passed their
            /// results panel, and <see cref="Copy"/> clears what it writes to, so copying wiped the
            /// table it had just copied out of. A parameter nobody can get wrong beats a comment
            /// saying not to.
            /// </param>
            /// <param name="primary">Whether this is the one filled button.</param>
            /// <param name="danger">Whether this destroys something.</param>
            public OnPicked(string caption, Action<IReadOnlyList<object>, Panel> act,
                            bool primary = false, bool danger = false)
            {
                Caption = caption;
                Act = act;
                Primary = primary;
                Danger = danger;
            }

            /// <summary>The caption, with <c>{n}</c> for the count.</summary>
            public string Caption { get; }

            /// <summary>What to do with the picked rows, and where to report it.</summary>
            public Action<IReadOnlyList<object>, Panel> Act { get; }

            /// <summary>Whether this is the one filled button.</summary>
            public bool Primary { get; }

            /// <summary>Whether this destroys something.</summary>
            public bool Danger { get; }
        }

        /// <summary>
        /// A table, and the bar saying what can be done to the rows selected in it.
        ///
        /// Every table in the product should have one. A table that can be acted on only one row at
        /// a time forces one action per row, which was the original complaint: no multi-select, no
        /// select-all, no way to clear.
        ///
        /// The bar carries four things:
        ///
        ///   * the count, so a selection built by shift-clicking down a long list can be confirmed
        ///     before it is acted on;
        ///   * Select all and Clear, so neither has to be done by hand;
        ///   * the actions, disabled until something is selected, so the bar reads as unavailable
        ///     rather than broken;
        ///   * the count inside each caption, so the last thing read before pressing states the
        ///     scope.
        /// </summary>
        /// <param name="table">The table. It is switched to multi-select here.</param>
        /// <param name="actions">What can be done to the picked rows.</param>
        public static UIElement Picking(ListView table, params OnPicked[] actions) =>
            Picking(table, null, actions);

        /// <summary>
        /// The verb bar, with something one of the verbs needs answered standing next to it.
        ///
        /// A verb that writes a value - set the picked issues to a status, tag them with a party -
        /// has to get that value from somewhere, and the somewhere used to be a window opened by a
        /// symbol somewhere else on the screen. That put the question a whole press away from the
        /// answer and left two symbols opening the same window. The chooser belongs beside the
        /// button that spends it.
        /// </summary>
        /// <param name="table">The table being picked from.</param>
        /// <param name="aside">A control a verb reads, shown before the verbs, or null.</param>
        /// <param name="actions">The verbs.</param>
        public static UIElement Picking(ListView table, UIElement? aside, params OnPicked[] actions)
        {
            var parts = PickingParts(table, aside, actions);

            // The table is in what comes back, above its bar. A pane that pins the bar and lets
            // the rows scroll wants the bar alone, and takes the table back out with Loose - see
            // Bench.Listed. Handing this whole stack to a docked slot reparents the table into
            // it, which draws the rows at the foot of an empty pane.
            parts.Verbs.Children.Insert(0, parts.Tally);
            parts.Verbs.Children.Add(parts.Links);

            return Stack(table, parts.Verbs, parts.Said);
        }

        /// <summary>
        /// The verb bar's three parts, for a screen that puts them in three places.
        ///
        /// The Create tab does: the verbs go on the strip beside the button that makes a test,
        /// and the count and the All/None links go on the list pane's own header. As one bar they
        /// were a second row of chrome under the strip saying what the header had room for.
        /// </summary>
        public sealed class PickBar
        {
            /// <summary>The verbs, in a panel that wraps.</summary>
            public WrapPanel Verbs { get; internal set; } = new WrapPanel();

            /// <summary>How many rows are picked, of how many.</summary>
            public TextBlock Tally { get; internal set; } = new TextBlock();

            /// <summary>The All and None links, as one child so they cannot be wrapped apart.</summary>
            public UIElement Links { get; internal set; } = new StackPanel();

            /// <summary>Where a verb says what it did.</summary>
            public Panel Said { get; internal set; } = new StackPanel();
        }

        /// <summary>
        /// Build the verb bar's parts without placing them.
        /// </summary>
        /// <param name="table">The table being picked from.</param>
        /// <param name="aside">A control a verb reads, shown before the verbs, or null.</param>
        /// <param name="actions">The verbs.</param>
        public static PickBar PickingParts(ListView table, UIElement? aside, params OnPicked[] actions)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            table.Multi();

            var verbs = actions ?? Array.Empty<OnPicked>();

            // A wrap rather than a strip: some tables carry a dozen verbs, and a bar that cannot
            // wrap pushes the last of them off the window edge silently.
            var bar = new WrapPanel();

            // Where an action says what it did. Below the bar, so a sentence about eleven rows sits
            // under the button that said eleven.
            var said = Stack();

            // Not "Count". A local function hides an outer method of the same name outright, so a
            // local Count() would make the Count(int, string) this uses uncallable.
            var tally = Small(string.Empty);
            tally.VerticalAlignment = VerticalAlignment.Center;
            tally.Margin = new Thickness(0, 0, Gap, 0);

            var all = Link("All", () => table.SelectAll());
            var none = Link("None", () => table.UnselectAll());

            // Centred and spaced. The bar mixes a hint, padded buttons and two links, and without
            // this the links sat on the top edge of the buttons' line and ran together as one
            // word.
            all.VerticalAlignment = VerticalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;

            // A gap, or the two read as one word. They had none, so every table on every screen
            // ended its bar with "AllNone".
            all.Margin = new Thickness(0, 0, Gap, 0);

            // One child of the wrap, not two. As two they were wrapped independently, so a bar
            // with enough verbs to fill the line left "All" on it and pushed "None" onto the
            // next - a lone word under the table with nothing to say what it picks.
            var picking = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Gap, 0, 0, 0),
            };

            picking.Children.Add(all);
            picking.Children.Add(none);

            var buttons = new List<System.Windows.Controls.Button>();

            // The word inside a verb drawn as a symbol and a caption. It carries its own ink, so
            // it does not follow the button's foreground the way a worded verb does, and it is
            // kept here so an off verb dims whole rather than half.
            var captions = new Dictionary<System.Windows.Controls.Button, TextBlock>();

            // The verbs that carry their own ink in both states: a destructive verb keeps its
            // red, a primary keeps white on the accent fill.
            var dangers = new HashSet<System.Windows.Controls.Button>();

            if (aside != null)
            {
                bar.Children.Add(aside);
                bar.Children.Add(new Border { Width = Gap });
            }

            foreach (var action in verbs)
            {
                var here = action;
                var glyph = Symbol(here.Caption);

                // The symbol and the word, not the symbol alone.
                //
                // A verb drawn as a shape with its name on a tooltip is a verb nobody reads
                // before pressing it, and this bar carries a delete. It was symbols on the
                // reasoning that twelve one-word buttons is a wall of text; the answer to twelve
                // verbs is fewer verbs, not unreadable ones.
                var words = Worded(here.Caption, 0);

                var button = here.Primary
                    ? Primary(words, () => Verb(here, table, said))
                    : Quiet(words, () => Verb(here, table, said));

                if (glyph != null)
                {
                    var face = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    face.Children.Add(Glyph(glyph, 13, here.Danger ? "RedTx" : "Muted",
                                            new Thickness(0, 0, 6, 0)));

                    var caption = new TextBlock
                    {
                        Text = words,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    // The word in ordinary ink, not the quiet button's accent. Five accented
                    // phrases in a row beside the All and None links read as seven links, and
                    // two of them are.
                    caption.Tint(TextBlock.ForegroundProperty, here.Danger ? "RedTx" : "Text");

                    face.Children.Add(caption);

                    if (!here.Danger) captions[button] = caption;

                    button.Content = face;
                }

                // A destructive verb reads as destructive whether or not it carries a symbol.
                if (here.Danger && glyph == null) button.Wearing("DangerBtn");

                // A wider left gap before a danger verb, so the press before it is not adjacent
                // to the one that deletes.
                button.Margin = new Thickness(here.Danger ? Gap * 2 : Gap, 2, 0, 2);
                button.VerticalAlignment = VerticalAlignment.Center;
                button.Tag = here.Caption;

                if (here.Danger || here.Primary) dangers.Add(button);

                buttons.Add(button);
                bar.Children.Add(button);
            }

            void Say()
            {
                var picked = table.SelectedItems.Count;
                var total = table.Items.Count;

                // "selected", not "picked". The product named one concept four ways - picked,
                // included, in, selected - and the tick column on some of these tables means
                // something else entirely, so the two need different words.
                tally.Text = picked == 0
                    ? Count(total, "row")
                    : picked.ToString(CultureInfo.InvariantCulture)
                      + " of " + Count(total, "row") + " selected";

                foreach (var button in buttons)
                {
                    button.IsEnabled = picked > 0;

                    // An off verb reads as off in one tier, not two. The quiet button's own
                    // disabled trigger paints Faint, which is reasoned about against the
                    // large-text floor and these captions are eleven pixels; a verb drawn with a
                    // symbol did not follow it at all, because its caption holds its own ink. Both
                    // go to Muted, which is a tier below Text and still readable. "Include tests"
                    // with nothing selected was the case that showed it.
                    if (captions.TryGetValue(button, out var word))
                        word.Tint(TextBlock.ForegroundProperty, picked > 0 ? "Text" : "Muted");
                    else if (!dangers.Contains(button) && !(button.Tag is null))
                        button.Tint(Control.ForegroundProperty,
                                    picked > 0 ? "Accent" : "Muted");

                    if (!(button.Tag is string caption)) continue;

                    var worded = Worded(caption, picked);

                    // An icon keeps its shape and moves the count into the tooltip; a worded
                    // button carries it where it is read, immediately before the press.
                    if (Symbol(caption) == null) button.Content = worded;
                    else button.ToolTip = worded;
                }
            }

            table.SelectionChanged += (s, e) => Say();
            Say();

            // The same verbs on right-click, on every table that has a bar. It is where a hand
            // already is, and offering both costs nothing.
            table.Menu(verbs.Select(v => new OnMenu(v.Caption, picked => v.Act(picked, said))).ToArray());

            return new PickBar { Verbs = bar, Tally = tally, Links = picking, Said = said };
        }

        /// <summary>
        /// A verb's caption for a given selection.
        ///
        /// With rows selected the count goes where it is read, immediately before the press. With
        /// none it comes out altogether: "Include 0 tests" and "Leave out 0 clashes" read as
        /// statements about the list rather than as things to do, and four of them on one screen
        /// was the state Exchange opened in.
        ///
        /// A demonstrative left dangling by the removal goes with it, so "Delete these {n}"
        /// becomes "Delete" rather than "Delete these".
        /// </summary>
        /// <param name="caption">The verb's caption, with <c>{n}</c> where the count goes.</param>
        /// <param name="picked">How many rows are selected.</param>
        public static string Worded(string caption, int picked)
        {
            if (caption == null) return string.Empty;

            if (picked > 0)
                return caption.Replace("{n}", picked.ToString(CultureInfo.InvariantCulture));

            var said = caption.Replace("{n}", string.Empty);

            // The demonstrative and the double space the count left behind.
            said = said.Replace(" these ", " ").Replace(" those ", " ");
            said = said.TrimEnd();
            if (said.EndsWith(" these", StringComparison.Ordinal)) said = said.Substring(0, said.Length - 6);
            if (said.EndsWith(" those", StringComparison.Ordinal)) said = said.Substring(0, said.Length - 6);

            while (said.Contains("  ")) said = said.Replace("  ", " ");

            return said.Trim();
        }

        /// <summary>
        /// The symbol for a verb, by the word it starts with, or null for one nobody can draw.
        ///
        /// Keyed on the caption rather than declared at every call site, because the vocabulary is
        /// small and shared: forty tables across the product say Focus, Copy, Delete, Run, Rename
        /// and Assign, and the table that says Paint should get the same shape as the next one.
        /// A verb with no shape keeps its words rather than being given a shape that has to be
        /// learned.
        /// </summary>
        /// <param name="caption">The verb's caption.</param>
        public static string? Symbol(string? caption)
        {
            if (string.IsNullOrWhiteSpace(caption)) return null;

            var word = caption!.Trim();
            var space = word.IndexOf(' ');
            if (space > 0) word = word.Substring(0, space);

            switch (word.ToLowerInvariant())
            {
                case "focus":
                case "frame":
                case "zoom": return Glyphs.Focus;
                case "isolate": return Glyphs.Eye;
                case "show": return Glyphs.EyeOff;
                case "select":
                case "pick": return Glyphs.Boxed;
                case "copy":
                case "duplicate": return Glyphs.Copy;
                case "delete":
                case "remove":
                case "drop":
                case "forget": return Glyphs.Bin;
                case "run":
                case "try": return Glyphs.Play;
                case "rename":
                case "edit": return Glyphs.Pencil;
                case "set": return Glyphs.Tune;
                case "renumber": return Glyphs.Number;
                case "move": return Glyphs.MoveIn;
                case "compact": return Glyphs.Compact;
                case "mark":
                case "approve":
                case "tick": return Glyphs.Tick;
                case "assign": return Glyphs.Person;
                case "comment": return Glyphs.Comment;
                case "group": return Glyphs.Group;
                case "sphere": return Glyphs.Sphere;
                case "snapshot":
                case "capture": return Glyphs.Camera;
                case "raise": return Glyphs.Issue;
                case "add":
                case "new": return Glyphs.Add;
                case "save":
                case "keep": return Glyphs.Save;
                case "open":
                case "read": return Glyphs.Read;
                case "send":
                case "export": return Glyphs.Onward;
                case "write":
                case "report": return Glyphs.Report;
                case "paint": return Glyphs.Layers;
                case "find": return Glyphs.Find;
                default: return null;
            }
        }

        /// <summary>
        /// The symbol for a kind of check, from the key the check declares.
        ///
        /// One table, so every list of tests draws a clash test with the same mark: the Create
        /// list, the check picker, the Manage rail and the exchange desk had one glyph between
        /// them and the kind could only be read as text.
        ///
        /// A key with no shape here falls back to the checklist rather than to nothing, so a check
        /// added later draws a mark from the day it is registered and gets its own by adding one
        /// case.
        /// </summary>
        /// <param name="symbol">The key, as <see cref="CheckSymbols"/> spells it.</param>
        public static string CheckSymbol(string? symbol)
        {
            switch ((symbol ?? string.Empty).Trim().ToLowerInvariant())
            {
                case CheckSymbols.Clash: return Glyphs.Clash;
                case CheckSymbols.Clearance: return Glyphs.Clearance;
                case CheckSymbols.Opening: return Glyphs.Opening;
                default: return Glyphs.Checklist;
            }
        }

        // -----------------------------------------------------------------------------------
        // Running work
        // -----------------------------------------------------------------------------------

        /// <summary>How many rows of a diff are worth putting on screen.</summary>
        private const int DiffRows = 200;

        /// <summary>
        /// What a write would do to the model, as a sentence and a table.
        ///
        /// One rendering, because there were four: three write screens and, once Zones started
        /// proposing rather than stamping, a fourth. They had already drifted - the same table with
        /// different column widths and two of the three explanations differently worded - which is
        /// the ordinary fate of a block copied for the third time.
        /// </summary>
        public static UIElement Diff(WriteDiff diff)
        {
            if (diff == null) throw new ArgumentNullException(nameof(diff));

            var stack = Stack();
            stack.Children.Add(Line(diff.Explain(), 0.85));

            if (diff.Changes.Count == 0) return stack;

            var rows = diff.Changes.Take(DiffRows).Select(c => new TableRow(c,
                c.Label, c.Before ?? "-", c.After ?? "-", c.Kind.ToString())).ToList();

            stack.Children.Add(Table(rows,
                ("Element", 220), ("There now", 140), ("Would be", 140), ("", 0)));

            // Counted off Every rather than Changes: Changes is itself already capped, so a diff
            // over eight hundred elements used to offer to show "the first 20 of 25 rows".
            if (diff.Every.Count > DiffRows)
                stack.Children.Add(Small("Showing the first " + DiffRows.ToString(CultureInfo.InvariantCulture)
                                         + " of " + Count(diff.Every.Count, "row") + "."));

            return stack;
        }

        /// <summary>
        /// A proposal you can read, and then the irreversible act it proposes.
        ///
        /// Writing properties onto a model is the only thing this product does that cannot be
        /// undone from inside it, and it was reached three different ways: the Data tab made a
        /// preview compulsory, while Zones and Changes wrote on the first press. One act, three
        /// behaviours, and the two that wrote immediately were the ones whose buttons said
        /// "Stamp ... onto every element" - the widest-sounding of the three.
        ///
        /// The shape here is the Data tab's, because it is the right one: press once and you are
        /// told what would happen, press again and it happens. The first press is read-only, so
        /// there is nothing to be careful about; the second one names the number in its own
        /// caption, so what you are agreeing to is on the button rather than above it.
        /// </summary>
        /// <param name="propose">Caption for the read-only first press.</param>
        /// <param name="verb">Leading word of the second button - "Write", "Stamp".</param>
        /// <param name="noun">What is being counted, singular - "element".</param>
        /// <param name="plan">
        /// Works out what would happen, renders its detail into the panel it is handed, and returns
        /// how many <paramref name="noun"/>s would be written. Zero offers no second press.
        /// </param>
        /// <param name="apply">Does it. Only reachable once <paramref name="plan"/> has run.</param>
        /// <remarks>
        /// The detail panel is made here rather than passed in, so the four parts come out in the
        /// order they are read - propose, what it found, commit, what it did. A caller supplying
        /// its own panel would also be re-parenting it into this stack, which in WPF is not a
        /// layout quirk but an exception.
        /// </remarks>
        public static Panel Commit(string propose, string verb, string noun,
                                   Func<Job, Panel, int> plan, Action<Job> apply)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            var detail = Stack();
            var gate = Stack();
            var done = Stack();

            var runner = Runner(propose, detail, job =>
            {
                // Cleared before the work rather than after it: the plan can throw, and a stale
                // button offering to write last time's answer is the worst thing left on screen.
                gate.Children.Clear();
                done.Children.Clear();

                var count = plan(job, detail);
                if (count <= 0) return;

                gate.Children.Add(Actions(Button(verb + " " + Count(count, noun),
                    () => { gate.Children.Clear(); Run(done, apply); })));
            });

            return Stack(runner, detail, gate, done);
        }

        /// <summary>
        /// Run work with no button of its own, reporting into <paramref name="output"/>.
        ///
        /// A list of fifteen rules cannot carry fifteen runners: a runner is a button, a Stop and
        /// a progress block, and repeated down a list it is more chrome than list. A row carries a
        /// symbol instead and presses this, so the work still goes through the same guards — one
        /// host operation at a time, the wait cursor, and an exception shown in the pane rather
        /// than thrown at the host.
        /// </summary>
        /// <param name="output">Where the work writes. Cleared first, and given the progress bar.</param>
        /// <param name="work">The work.</param>
        public static void Run(Panel output, Action<Job> work)
        {
            if (IsBusy)
            {
                output.Children.Clear();
                output.Children.Add(Note("Something else is still running. Try again when it has "
                    + "finished."));
                return;
            }

            var status = new TextBlock().Wearing("Hint");
            var bar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 6) };

            output.Children.Clear();
            output.Children.Add(bar);
            output.Children.Add(status);

            Mouse.OverrideCursor = Cursors.Wait;
            Began();

            var job = new Job(output.Dispatcher, status, bar, output);
            output.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);

            try
            {
                work(job);
                status.Text = "Finished in " + Seconds(job.Elapsed) + ".";
                bar.IsIndeterminate = false;
                bar.Maximum = 1;
                bar.Value = 1;
            }
            catch (Exception ex)
            {
                output.Children.Clear();
                output.Children.Add(Problem(Describe(ex)));
            }
            finally
            {
                Mouse.OverrideCursor = null;
                Ended();
            }
        }

        /// <summary>
        /// A button that runs long work, with a progress bar, a running count and a Stop.
        ///
        /// Nothing in this product runs when a screen opens; everything runs from one of these.
        /// That is what keeps opening a tab from freezing on a federation of half a million
        /// elements, and it is what makes the zero-setup rule hold: the button works on a raw model
        /// with nothing configured.
        ///
        /// It paints itself before starting the work. The previous version set a status line and
        /// then called the work, so a job that never reported progress showed nothing and the pane
        /// simply stopped responding. It now pumps the dispatcher once before the work begins, so
        /// the disabled button, the moving bar and the Stop are on screen first.
        /// </summary>
        /// <param name="caption">What the button says.</param>
        /// <param name="output">Where results are written. Cleared on each run.</param>
        /// <param name="work">The work. Check <see cref="Job.IsCancelled"/> in any loop.</param>
        /// <param name="primary">
        /// Whether this is the card's main action. False for a second long-running button on a
        /// card: two primary buttons give the reader no indication which one is intended.
        /// </param>
        /// <param name="danger">True where the work cannot be undone, so the button is red.</param>
        /// <param name="confirm">Asked on the press; the work runs only when it answers true.</param>
        public static Panel Runner(string caption, Panel output, Action<Job> work,
                                   bool primary = true, bool danger = false,
                                   Func<bool>? confirm = null)
        {
            var parts = RunParts(caption, output, work, primary, danger, confirm);

            // The gap belongs to the runner as a whole, not to its button row. It used to sit on
            // the row, which meant a runner placed beside ordinary buttons stood eight pixels
            // lower than them - and it was reachable from nowhere to correct, because the row is
            // inside what this returns.
            var whole = Stack(parts.Buttons, parts.Progress);

            whole.Margin = new Thickness(0, Gap, 0, 0);

            return whole;
        }

        /// <summary>
        /// The same runner, returned in its two parts.
        ///
        /// Stacked, a runner is a button with a progress bar and a status line beneath it. Placing
        /// that column in a row of ordinary buttons — a footer — makes the row three lines tall,
        /// centres the other buttons against a bar that is not yet visible, and moves everything
        /// beside it when the job starts. A footer wants the buttons on one line and the progress
        /// where it can grow without displacing them.
        ///
        /// The two parts are one runner: the same job drives both.
        /// </summary>
        /// <param name="caption">What the button says.</param>
        /// <param name="output">Where the work writes its result.</param>
        /// <param name="work">The work.</param>
        /// <param name="primary">Whether the button is the screen's main press.</param>
        /// <param name="danger">True where the work cannot be undone, so the button is red.</param>
        /// <param name="confirm">Asked on the press; the work runs only when it answers true.</param>
        /// <returns>The button row, and the progress block that folds away when nothing runs.</returns>
        public static RunPair RunParts(string caption, Panel output, Action<Job> work,
                                   bool primary = true, bool danger = false,
                                   Func<bool>? confirm = null)
        {
            var status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }.Wearing("Hint");

            var bar = new ProgressBar
            {
                IsIndeterminate = true,
                Margin = new Thickness(0, 0, 0, 6),
            };

            var stop = new System.Windows.Controls.Button { Content = "Stop" };
            stop.Wearing("Btn");

            var go = new System.Windows.Controls.Button { Content = caption };
            go.Wearing(danger ? "DangerBtn" : primary ? "PrimaryBtn" : "Btn");

            Job? running = null;
            stop.Click += (s, e) =>
            {
                if (running == null) return;
                running.IsCancelled = true;
                status.Text = "Stopping at the end of this batch...";
            };

            // Folded away rather than left in place empty, so a screen at rest is not a column of
            // blank strips where its progress bars will one day be.
            var progress = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            progress.Children.Add(bar);
            progress.Children.Add(status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(go);
            buttons.Children.Add(new Border { Width = 8 });
            buttons.Children.Add(stop);

            stop.Visibility = Visibility.Collapsed;

            go.Click += (s, e) =>
            {
                // A click that arrived while something else was pumping. Starting a second host
                // operation on top of a live one interleaves two walks of the same document,
                // which the host does not survive.
                if (IsBusy)
                {
                    output.Children.Clear();
                    output.Children.Add(Note("Something else is still running. This will be ready "
                        + "the moment it finishes."));
                    return;
                }

                // Asked before anything is cleared or disabled, so a "no" leaves the screen
                // exactly as it was rather than blanking the last run's result.
                if (confirm != null && !confirm()) return;

                output.Children.Clear();

                go.IsEnabled = false;
                stop.Visibility = Visibility.Visible;
                progress.Visibility = Visibility.Visible;
                bar.IsIndeterminate = true;
                bar.Value = 0;
                status.Text = "Starting...";

                Mouse.OverrideCursor = Cursors.Wait;
                Began();

                var job = new Job(go.Dispatcher, status, bar, output);
                running = job;

                // Everything above this line has to be on the glass before anything below it runs.
                // Without this one call, a job whose first act is a thirty-second host query shows
                // the user an unchanged pane and a frozen Navisworks.
                go.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);

                try
                {
                    work(job);

                    // The bar goes when the work does. A finished progress bar sitting at a
                    // hundred per cent under a button is a strip of screen saying nothing, for
                    // the rest of the session - and on a footer it is the difference between one
                    // line and three.
                    //
                    // What it was still telling anybody - how long it took - goes into the output
                    // instead, beside the counts the work itself wrote there.
                    // The bar goes when the work does, and takes the line about how long it
                    // took with it. A footer is one line; the elapsed time was the third of
                    // three, and nobody reads it twice.
                    progress.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    // Shown in the pane rather than thrown at the host. A plug-in exception that
                    // escapes takes the message loop with it in some versions, and the user would
                    // see a crash instead of a sentence about what failed.
                    output.Children.Clear();
                    output.Children.Add(Problem(Describe(ex)));
                    progress.Visibility = Visibility.Collapsed;
                }
                finally
                {
                    running = null;
                    go.IsEnabled = true;
                    stop.Visibility = Visibility.Collapsed;
                    Mouse.OverrideCursor = null;
                    Ended();
                }
            };

            return new RunPair(buttons, progress);
        }

        /// <summary>A runner in its two pieces: the buttons, and the progress under them.</summary>
        public sealed class RunPair
        {
            internal RunPair(Panel buttons, Panel progress)
            {
                Buttons = buttons;
                Progress = progress;
            }

            /// <summary>The press, and the Stop that appears beside it while the job runs.</summary>
            public Panel Buttons { get; }

            /// <summary>The bar and the status line. Folded away until something runs.</summary>
            public Panel Progress { get; }
        }

        private static string Seconds(TimeSpan span) =>
            span.TotalSeconds < 1 ? "under a second"
            : span.TotalSeconds < 60 ? ((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s"
            : ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m "
              + span.Seconds.ToString(CultureInfo.InvariantCulture) + "s";

        private static void Began()
        {
            _running++;
            Running?.Invoke(null, _running);
        }

        private static void Ended()
        {
            if (_running > 0) _running--;
            Running?.Invoke(null, _running);
        }

        /// <summary>
        /// An exception in the words of somebody who did not write this code.
        ///
        /// The type name is dropped and the message kept, except where the type is the whole story —
        /// a stale element handle means the model changed underneath, which is worth saying in
        /// those words rather than as "StaleItemException".
        /// </summary>
        /// <param name="error">What went wrong.</param>
        public static string Describe(Exception error)
        {
            switch (error)
            {
                case Core.Abstractions.StaleItemException _:
                    return "The model changed while this was running, so some elements no longer exist. "
                           + "Run it again to work from the model as it is now.";

                case NotSupportedException _:
                    return error.Message + " (Navisworks does not offer this.)";

                case UnauthorizedAccessException _:
                    return "Windows refused access: " + error.Message;

                case System.IO.IOException _:
                    return "A file could not be read or written: " + error.Message;

                default:
                    return error.Message;
            }
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The ink to write on a user-chosen colour rather than on a theme colour.
        ///
        /// A chosen colour does not change with the theme, and the theme's ink does. The washes a
        /// symbol can be given are fixed — they belong to the matrix, not the seat — so in the dark
        /// palette the letter on top of one was near-white on a pale pastel, at 1:1 contrast, and
        /// every cell of a coloured symbol read as blank. Which ink to use is a property of the
        /// colour underneath and nothing else.
        /// </summary>
        /// <param name="on">The colour being written on.</param>
        public static Brush InkOn(Brush? on)
        {
            if (!(on is SolidColorBrush solid)) return Skin.Of("Text");

            var colour = solid.Color;

            // Rec. 709 luminance on the raw channels. Not gamma-correct, and it does not need to
            // be: the question is only "is this light or dark", and every wash on offer is far
            // from the middle.
            var light = ((0.2126 * colour.R) + (0.7152 * colour.G) + (0.0722 * colour.B)) / 255.0;

            return light > 0.55 ? Skin.Of("Ink.OnLight") : Skin.Of("Ink.OnDark");
        }

        /// <summary>The text colour for a tone.</summary>
        /// <param name="tone">The tone.</param>
        public static Brush Ink(Tone tone) => Skin.Ink(tone);

        /// <summary>The background wash for a tone.</summary>
        /// <param name="tone">The tone.</param>
        public static Brush Wash(Tone tone) => Skin.Wash(tone);

        /// <summary>A count with its unit, pluralised.</summary>
        /// <param name="count">How many.</param>
        /// <param name="unit">What of, singular.</param>
        public static string Count(int count, string unit) =>
            count.ToString("N0", CultureInfo.InvariantCulture) + " " + Plural(count, unit);

        /// <summary>
        /// A unit, pluralised.
        ///
        /// An "s" on the end covers almost everything a screen counts, but not the words ending in
        /// a sibilant: a board that says "96 clashs" reads as a typo in a product, and clash is the
        /// noun this one counts most.
        /// </summary>
        /// <param name="count">How many.</param>
        /// <param name="unit">What of, singular.</param>
        public static string Plural(int count, string unit)
        {
            if (count == 1 || string.IsNullOrEmpty(unit)) return unit;

            var said = unit.TrimEnd();
            var trailing = unit.Substring(said.Length);

            // The last word, not the whole phrase. Units here are often two or three words -
            // "selected row", "of them" - and suffixing the phrase put the "s" on the wrong word.
            var space = said.LastIndexOf(' ');
            var head = space < 0 ? string.Empty : said.Substring(0, space + 1);
            var tail = space < 0 ? said : said.Substring(space + 1);

            // Already plural. "of them" is the phrase five screens count with, and the generic
            // rule wrote "2 of thems" on every one of them.
            if (Already.Contains(tail)) return unit;

            // A consonant then y: party, priority, storey is not one of these because its y
            // follows a vowel. "0 partys" is the kind of typo that makes a count read as a bug.
            if (tail.Length > 1
                && tail.EndsWith("y", StringComparison.OrdinalIgnoreCase)
                && "aeiou".IndexOf(char.ToLowerInvariant(tail[tail.Length - 2])) < 0)
                return head + tail.Substring(0, tail.Length - 1) + "ies" + trailing;

            if (tail.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                || tail.EndsWith("x", StringComparison.OrdinalIgnoreCase)
                || tail.EndsWith("z", StringComparison.OrdinalIgnoreCase)
                || tail.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
                || tail.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
                return head + tail + "es" + trailing;

            return head + tail + "s" + trailing;
        }

        /// <summary>
        /// Words that are already plural, so <see cref="Plural"/> leaves them alone.
        ///
        /// Short on purpose: it holds the words this product actually counts with, not an English
        /// irregular-plural table. A word missing from it gets an "s", which is what happened
        /// before the list existed.
        /// </summary>
        private static readonly HashSet<string> Already =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "them", "these", "those", "others", "people", "children", "men", "women",
            };
    }

    /// <summary>
    /// The glyph shapes, as path data in a 24-unit box.
    ///
    /// Stroked outlines rather than filled shapes, so one set works on a light card and a dark one
    /// with nothing but a change of brush, and so a glyph at 14 pixels and the same glyph at 22 are
    /// the same drawing rather than two files.
    /// </summary>
    public static class Glyphs
    {
        /// <summary>A chevron pointing down: an open section.</summary>
        public const string Down = "M6,9 L12,15 L18,9";

        /// <summary>A chevron pointing right: a folded section.</summary>
        public const string Right = "M9,6 L15,12 L9,18";

        /// <summary>A chevron pointing left: fold a pane away, go back one.</summary>
        public const string Left = "M15,6 L9,12 L15,18";

        /// <summary>A chevron pointing up.</summary>
        public const string Up = "M6,15 L12,9 L18,15";

        /// <summary>A cross: remove this one.</summary>
        public const string Close = "M6,6 L18,18 M18,6 L6,18";

        /// <summary>A tick: this went right.</summary>
        public const string Tick = "M5,13 L10,17.5 L19,6.5";

        /// <summary>A plus: add another.</summary>
        public const string Add = "M12,5 L12,19 M5,12 L19,12";

        /// <summary>A bin: delete this.</summary>
        public const string Bin = "M5,7 L19,7 M10,7 L10,4 L14,4 L14,7 M6.5,7 L7.7,20 L16.3,20 L17.5,7";


        /// <summary>A magnifier: find something.</summary>
        public const string Find = "M11,4 A7,7 0 1 0 11.01,4 M16.2,16.2 L21,21";

        /// <summary>A warning triangle.</summary>
        public const string Warn = "M12,3.5 L22,20 L2,20 Z M12,9.5 L12,14 M12,16.8 L12,17.2";

        /// <summary>A crossed circle: this failed.</summary>
        public const string Error = "M12,3 A9,9 0 1 0 12.01,3 M8.8,8.8 L15.2,15.2 M15.2,8.8 L8.8,15.2";

        /// <summary>An i in a circle: worth knowing.</summary>
        public const string Info = "M12,3 A9,9 0 1 0 12.01,3 M12,11 L12,16.5 M12,7.5 L12,7.9";

        /// <summary>A keyboard: what the keys do.</summary>
        public const string Keyboard = "M2.5,6.5 L21.5,6.5 L21.5,17.5 L2.5,17.5 Z "
                                     + "M6,10 L6.2,10 M9,10 L9.2,10 M12,10 L12.2,10 "
                                     + "M15,10 L15.2,10 M18,10 L18.2,10 "
                                     + "M7.5,12.8 L7.7,12.8 M10.5,12.8 L10.7,12.8 "
                                     + "M13.5,12.8 L13.7,12.8 M16.5,12.8 L16.7,12.8 "
                                     + "M8,15.3 L16,15.3";

        /// <summary>An open box: there is nothing here.</summary>
        public const string Empty = "M4,7.5 L12,3.5 L20,7.5 L20,16.5 L12,20.5 L4,16.5 Z M4,7.5 L12,11.5 L20,7.5 M12,11.5 L12,20.5";

        /// <summary>A moon: the theme control.</summary>
        public const string Theme = "M20,13.5 A8.5,8.5 0 1 1 10.5,4 A6.6,6.6 0 0 0 20,13.5 Z";

        /// <summary>A stack of sheets: models, or a federation.</summary>
        public const string Model = "M12,3 L21,7.5 L12,12 L3,7.5 Z M3,12 L12,16.5 L21,12 M3,16.5 L12,21 L21,16.5";

        /// <summary>Overlapping panes: appearance layers.</summary>
        public const string Layers = "M3.5,10 L12,10 L12,20.5 L3.5,20.5 Z M8,10 L8,4.5 L20.5,4.5 L20.5,15.5 L12,15.5";

        /// <summary>Two shapes meeting: a clash.</summary>
        public const string Clash = "M4,12 L11,12 M13,12 L20,12 M12,4 L12,11 M12,13 L12,20 M9,9 L15,15 M15,9 L9,15";

        /// <summary>
        /// A soffit, a floor, and the height between them: a clearance.
        ///
        /// A section rather than a ruler. A ruler is any measured dimension, and this check
        /// measures one dimension in particular — what is over a surface, and how far.
        /// </summary>
        public const string Clearance = "M4,6.5 L20,6.5 M4,19.5 L20,19.5 M12,8.5 L12,17.5 M9.6,10.9 L12,8.5 L14.4,10.9 M9.6,15.1 L12,17.5 L14.4,15.1";

        /// <summary>A floor with a gap in it and a drop through the gap: a fall hazard.</summary>
        public const string Opening = "M3,9.5 L9,9.5 M15,9.5 L21,9.5 M9,9.5 L9,12.5 M15,9.5 L15,12.5 M12,12 L12,20 M9.4,17.4 L12,20 L14.6,17.4";

        /// <summary>Two panels side by side: a comparison.</summary>
        public const string Compare = "M11,4 L11,20 M4,7 L8,7 L8,17 L4,17 Z M16,7 L20,7 L20,17 L16,17 Z";

        /// <summary>A grid: a table of values.</summary>
        public const string Table = "M3,5 L21,5 L21,19 L3,19 Z M3,10 L21,10 M3,14.5 L21,14.5 M9,10 L9,19 M15,10 L15,19";

        /// <summary>A folder.</summary>
        public const string Folder = "M3,6.5 L9.5,6.5 L11.5,9 L21,9 L21,19 L3,19 Z";

        /// <summary>A disk: saved somewhere it will still be tomorrow.</summary>
        public const string Save = "M4,4 L16,4 L20,8 L20,20 L4,20 Z M8,4 L8,10 L15,10 L15,4 M8,20 L8,14 L16,14 L16,20";

        /// <summary>A triangle: run it.</summary>
        public const string Play = "M8,5 L19,12 L8,19 Z";

        /// <summary>A camera: a saved viewpoint.</summary>
        public const string Camera = "M3,8.5 L7,8.5 L9,5.5 L15,5.5 L17,8.5 L21,8.5 L21,19 L3,19 Z M12,14 A3.2,3.2 0 1 0 12.01,14";

        /// <summary>A funnel: a rule or a set.</summary>
        public const string Filter = "M3,5 L21,5 L14,13 L14,20 L10,18 L10,13 Z";

        /// <summary>A page: a report.</summary>
        public const string Report = "M6,3 L15,3 L19,7 L19,21 L6,21 Z M15,3 L15,7 L19,7 M9,12 L16,12 M9,16 L16,16";

        /// <summary>A clock: when it last happened.</summary>
        public const string Clock = "M12,3 A9,9 0 1 0 12.01,3 M12,7 L12,12.5 L16,14.5";

        /// <summary>A dial: settings and thresholds.</summary>
        public const string Tune = "M4,7 L20,7 M4,17 L20,17 M9,4.5 L9,9.5 M15,14.5 L15,19.5";

        /// <summary>
        /// Bars long to short: put them in an order.
        ///
        /// One symbol for sorting, wherever sorting is. The rail's sort and the board's used to be
        /// a dial and a stack of layers, which are two different promises for the same act.
        /// </summary>
        public const string Sort = "M4,6 L20,6 M4,12 L15,12 M4,18 L10,18";

        /// <summary>An arrow onward: this takes you somewhere.</summary>
        public const string Onward = "M5,12 L18,12 M13,7 L18,12 L13,17";

        /// <summary>An arrow back: the previous one.</summary>
        public const string Backward = "M19,12 L6,12 M11,7 L6,12 L11,17";

        /// <summary>A box drawn round something: isolate it.</summary>
        public const string Boxed = "M4,4 L20,4 L20,20 L4,20 Z M9,9 L15,9 L15,15 L9,15 Z";

        /// <summary>A ruler: a measured dimension.</summary>
        public const string Ruler = "M3,9 L21,9 L21,15 L3,15 Z M7,9 L7,12 M11,9 L11,13 M15,9 L15,12 M19,9 L19,13";

        // The verbs a grid carries. Drawn rather than lettered because a row of one-word buttons
        // under a table is a wall of text where the eye wants a row of shapes.

        /// <summary>A pencil: rename this.</summary>
        public const string Pencil = "M4,20 L8,20 L20,8 L16,4 L4,16 Z M14,6 L18,10";

        /// <summary>Two crossing pairs: number them again.</summary>
        public const string Number = "M9,4 L7,20 M17,4 L15,20 M4,9 L20,9 M3,15 L19,15";

        /// <summary>A ring with a bar through it: ruled out, on purpose.</summary>
        public const string Barred = "M12,4 A8,8 0 1 0 12.01,4 M6.3,6.3 L17.7,17.7";

        /// <summary>Corners and a filled middle: all of it, every cell.</summary>
        public const string SelectAll = "M4,7.5 L4,4 L7.5,4 M16.5,4 L20,4 L20,7.5 M20,16.5 L20,20 L16.5,20 M7.5,20 L4,20 L4,16.5 M9,9 L15,9 L15,15 L9,15 Z";

        /// <summary>A list with a tick against it: every one of them accounted for.</summary>
        public const string Checklist = "M4,7 L13,7 M4,12 L11,12 M4,17 L10,17 M13.5,16.5 L16,19 L20.5,13.5";

        /// <summary>An arrow up against a bar: on to the next one.</summary>
        public const string Next = "M6.5,5.5 L13.5,12 L6.5,18.5 M17.5,5.5 L17.5,18.5";

        /// <summary>A sheet with a tick on it: checked, and against something.</summary>
        public const string Verify = "M5,5 L19,5 L19,20.5 L5,20.5 Z M9.5,3.5 L14.5,3.5 M8.5,12.5 L11,15 L15.5,9.5";

        /// <summary>Two arrows passing: what is different between them.</summary>
        public const string Changes = "M3,9 L16,9 M13,6 L16,9 L13,12 M21,15 L8,15 M11,12 L8,15 L11,18";

        /// <summary>An arrow leaving a tray: out of here, into a file.</summary>
        public const string Export = "M12,3 L12,14 M8.5,6.5 L12,3 L15.5,6.5 M5,13 L5,20 L19,20 L19,13";

        /// <summary>A painter's board with its colours on it: how the thing looks.</summary>
        public const string Palette = "M12,3.5 C6.8,3.5 3,7.4 3,12 C3,16.7 6.9,20.5 12,20.5 C13.4,20.5 14.1,19.7 14.1,18.8 C14.1,18 13.4,17.6 13.4,16.8 C13.4,16 14.1,15.4 14.9,15.4 L16.6,15.4 C19.1,15.4 21,13.4 21,10.9 C21,6.8 16.9,3.5 12,3.5 M7.6,10.6 L7.61,10.6 M10.6,7.6 L10.61,7.6 M14.4,7.6 L14.41,7.6 M17,10.6 L17.01,10.6";

        /// <summary>Pour it into what is empty: a fill, not another plus.</summary>
        public const string Sweep = "M4,4 L20,4 L20,8 L4,8 Z M12,8 L12,11 M8.5,11 L15.5,11 L15.5,20.5 L8.5,20.5 Z M8.5,15.5 L15.5,15.5";

        /// <summary>Rub it out: a clear, not another cross.</summary>
        public const string Eraser = "M6.5,19 L3.5,15.5 L14,5 L19.5,11 L11,19 Z M9.5,19 L20.5,19";

        /// <summary>A folder with something going into it: move it there.</summary>
        public const string MoveIn = "M3,6.5 L9.5,6.5 L11.5,9 L21,9 L21,19 L3,19 Z M12,16 L12,10.5 M9.8,12.7 L12,10.5 L14.2,12.7";

        /// <summary>Arrows meeting: squeeze out what is finished.</summary>
        public const string Compact = "M4,12 L20,12 M8,8 L12,4 L16,8 M8,16 L12,20 L16,16";

        /// <summary>Two sheets: copy it.</summary>
        public const string Copy = "M9,9 L20,9 L20,20 L9,20 Z M5,15 L5,4 L16,4";

        /// <summary>A ringed dot: put the camera on it.</summary>
        public const string Focus = "M12,8 A4,4 0 1 0 12.01,8 M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12";

        /// <summary>An eye: show only this.</summary>
        public const string Eye = "M2,12 C5,7 8.5,5.5 12,5.5 C15.5,5.5 19,7 22,12 C19,17 15.5,18.5 12,18.5 C8.5,18.5 5,17 2,12 Z M12,9.6 A2.4,2.4 0 1 0 12.01,9.6";

        /// <summary>An eye struck through: put everything back.</summary>
        public const string EyeOff = "M3,3 L21,21 M10.6,6.2 C11,6.1 11.5,6 12,6 C18.4,6 22,12 22,12 C21.2,13.3 20.2,14.5 19,15.6 M6.3,7.9 C3.7,9.5 2,12 2,12 C2,12 5.6,18 12,18 C13.1,18 14.2,17.8 15.3,17.5";

        /// <summary>A person: whose problem it is.</summary>
        public const string Person = "M12,11 A3.4,3.4 0 1 0 12.01,11 M5.4,20 A6.6,6.6 0 0 1 18.6,20";

        /// <summary>A speech bubble: somebody said something.</summary>
        public const string Comment = "M4,5 L20,5 L20,16 L9,16 L4,20 Z";

        /// <summary>Three boxes: gather these into one.</summary>
        public const string Group = "M3,4 L10,4 L10,11 L3,11 Z M14,4 L21,4 L21,11 L14,11 Z M8.5,14 L15.5,14 L15.5,20 L8.5,20 Z";

        /// <summary>A ball: a marker dropped in the model.</summary>
        public const string Sphere = "M12,5 A7,7 0 1 0 12.01,5 M5.4,9.5 C9.4,11.9 14.6,11.9 18.6,9.5";

        /// <summary>A page with a plus: raise an issue about it.</summary>
        public const string Issue = "M5,4 L16,4 L19,7 L19,20 L5,20 Z M9,12 L15,12 M12,9 L12,15";

        /// <summary>Attach to something that already exists — two links of a chain.</summary>
        public const string Link = "M10,14 A4,4 0 0 1 10,8.4 L13,5.4 A4,4 0 0 1 18.6,11 L17.2,12.4 M14,10 A4,4 0 0 1 14,15.6 L11,18.6 A4,4 0 0 1 5.4,13 L6.8,11.6";

        /// <summary>Two columns: what the table shows.</summary>
        public const string Columns = "M3,4 L21,4 L21,20 L3,20 Z M9,4 L9,20 M15,4 L15,20";

        /// <summary>A circle of arrows: the round trip out and back.</summary>
        public const string Round = "M4,12 A8,8 0 0 1 17.7,6.3 L20,8 M20,4 L20,8 L16,8 M20,12 A8,8 0 0 1 6.3,17.7 L4,16 M4,20 L4,16 L8,16";

        /// <summary>Three dots: the rest of them.</summary>
        public const string More = "M5,12 L5.01,12 M12,12 L12.01,12 M19,12 L19.01,12";

        /// <summary>An open book: read what is there.</summary>
        public const string Read = "M12,6 C10,4.5 7.5,4 4,4 L4,18 C7.5,18 10,18.5 12,20 M12,6 C14,4.5 16.5,4 20,4 L20,18 C16.5,18 14,18.5 12,20 M12,6 L12,20";
    }
}
