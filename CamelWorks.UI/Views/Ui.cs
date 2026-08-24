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
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace CamelWorks.UI.Views
{
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

        /// <summary>Cell text by column. Out-of-range is empty rather than an exception.</summary>
        /// <param name="index">Column index.</param>
        public string this[int index] =>
            index >= 0 && index < _cells.Length ? _cells[index] ?? string.Empty : string.Empty;

        /// <summary>How many cells the row has.</summary>
        public int Count => _cells.Length;

        /// <inheritdoc />
        public override string ToString() => string.Join(" | ", _cells);
    }

    /// <summary>
    /// A long-running piece of work, and the only way this product has of staying responsive
    /// during one.
    ///
    /// <b>This is not a background thread, and it cannot be.</b> The Navisworks API is
    /// main-thread-only: reading a property from another thread is undefined behaviour, not a
    /// slower version of the same thing. So the work runs on the UI thread and pumps the dispatcher
    /// between chunks, which is what lets the progress bar move and the Stop button be clickable at
    /// all. Cancellation therefore happens between chunks, never inside one — stated plainly here
    /// because a Stop button that takes twenty seconds to notice looks broken unless you know why.
    ///
    /// Give it a <see cref="Total"/> whenever the count is known before the loop starts. The
    /// difference between "12,300 of 29,000, about 20s left" and a bar that could mean anything is
    /// the difference between waiting and force-quitting Navisworks.
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
        private readonly System.Diagnostics.Stopwatch _sincePump = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _since = System.Diagnostics.Stopwatch.StartNew();

        private long _total;
        private long _done;
        private string _what = string.Empty;

        internal Job(Dispatcher? dispatcher, TextBlock? status, ProgressBar? bar)
        {
            _dispatcher = dispatcher;
            _status = status;
            _bar = bar;
        }

        /// <summary>
        /// A job with nothing to report to and no way to be cancelled.
        ///
        /// For work driven by something other than a button — a graph node calling a service that
        /// takes a job. The alternative is a second copy of every service that does not report
        /// progress, which is how two code paths start disagreeing.
        /// </summary>
        internal static Job Silent() => new Job(null, null, null);

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

        /// <summary>An identifier, a path or a hash — anything that must not be read as prose.</summary>
        /// <param name="text">The text.</param>
        public static TextBlock Code(string text) =>
            new TextBlock { Text = text }.Wearing("Mono");

        /// <summary>A small upper-case label above a group of things.</summary>
        /// <param name="text">The label.</param>
        public static TextBlock Eyebrow(string text) =>
            new TextBlock { Text = (text ?? string.Empty).ToUpperInvariant() }.Wearing("Caps");

        /// <summary>A number and its label, for a scoreboard.</summary>
        /// <param name="value">The number, already formatted.</param>
        /// <param name="label">What it counts.</param>
        /// <param name="tone">How it reads.</param>
        public static UIElement Figure(string value, string label, Tone tone = Tone.Plain)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 20, 6) };

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
            foreach (var child in children) if (child != null) panel.Children.Add(child);
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
                panel.Children.Add(child);
            }

            return panel;
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
            var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Gap, 0, 0) };

            if (first is FrameworkElement lead)
            {
                lead.Margin = new Thickness(0, 0, 8, 5);
                panel.Children.Add(lead);
            }
            else if (first != null)
            {
                panel.Children.Add(first);
            }

            foreach (var child in rest)
            {
                if (child == null) continue;
                if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 6, 5);
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
            Padding = new Thickness(12, 12, 12, 16),
            Background = Brushes.Transparent,
            Content = content,
        };

        /// <summary>A titled block of related things.</summary>
        /// <param name="title">The block's title.</param>
        /// <param name="subtitle">One line saying what it is for, or null.</param>
        /// <param name="body">The contents.</param>
        public static Border Card(string title, string? subtitle, params UIElement?[] body)
        {
            var panel = new StackPanel();

            if (!string.IsNullOrEmpty(title)) panel.Children.Add(Heading(title));
            if (subtitle != null) panel.Children.Add(Sub(subtitle));

            foreach (var child in body) if (child != null) panel.Children.Add(child);

            var card = new Border { Child = panel };
            card.Wearing("CardStyle");
            return card;
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
        public static Border Section(string title, string? summary, bool open, params UIElement?[] body)
        {
            var chevron = Glyph(open ? Glyphs.Down : Glyphs.Right, 14, "Muted");

            var caption = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 10, 0),
            }.Wearing("Body");

            var state = new TextBlock
            {
                Text = summary ?? string.Empty,

                // NoWrap as well as trimming: the Hint style wraps, and wrapping beats trimming,
                // so a long summary broke onto a second right-aligned line instead of ending in an
                // ellipsis. A folded header is one line by definition.
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(12, 0, 0, 0),
                Visibility = open || string.IsNullOrEmpty(summary) ? Visibility.Collapsed : Visibility.Visible,
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
                state.Visibility = shown || string.IsNullOrEmpty(summary) ? Visibility.Collapsed : Visibility.Visible;

                chevron.Child = Arrow(shown ? Glyphs.Down : Glyphs.Right, "Muted");
            };

            var card = new Border { Child = Stack(header, contents) };
            card.Wearing("CardStyle");
            return card;
        }

        /// <summary>A hairline between two groups of things.</summary>
        public static UIElement Rule() => new Border
        {
            Height = 1,
            Margin = new Thickness(0, Gap, 0, Gap),
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
        public static System.Windows.Controls.Button Icon(string glyph, string tip, Action onClick)
        {
            var button = new System.Windows.Controls.Button
            {
                // Through Glyph, not Arrow: a raw path draws at its own 24-unit size, which inside
                // a 26-pixel button is a cross that fills the row it is meant to sit beside.
                Content = Glyph(glyph, 15, "Muted"),
                ToolTip = tip,
            };

            button.Wearing("RowBtn");
            if (onClick != null) button.Click += (s, e) => onClick();
            return button;
        }

        private static System.Windows.Controls.Button Press(string caption, Action onClick, string? style)
        {
            var button = new System.Windows.Controls.Button { Content = caption };

            if (style != null) button.Wearing(style);
            else button.MinWidth = 88;

            if (onClick != null) button.Click += (s, e) => onClick();
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
        public static TextBox Text(string? value, Action<string>? onChanged, double width = 180)
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
        public static TextBox Text(string? value, string placeholder, Action<string>? onChanged, double width = 180)
        {
            var box = Text(value, onChanged, width);

            // The skin's TextBox template shows Tag as the placeholder, so every text field in the
            // product gets one the same way and none of them can drift out of position.
            box.Tag = placeholder;
            return box;
        }

        /// <summary>A dropdown of fixed options.</summary>
        /// <param name="options">What can be picked.</param>
        /// <param name="selected">What is picked now, or null.</param>
        /// <param name="onChanged">Called with the new pick.</param>
        /// <param name="width">The narrowest it may be.</param>
        public static ComboBox Choice(IEnumerable<string> options, string? selected,
                                      Action<string>? onChanged, double width = 170)
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
            if (options.Count > 4) return Choice(options, selected, onChanged);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var buttons = new List<ToggleButton>();

            foreach (var option in options)
            {
                var pick = option;

                var button = new ToggleButton
                {
                    Content = option,
                    IsChecked = string.Equals(option, selected, StringComparison.Ordinal)
                                || (selected == null && ReferenceEquals(option, options[0])),
                };

                button.Wearing("Segment");

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

                buttons.Add(button);
                row.Children.Add(button);
            }

            if (!buttons.Any(b => b.IsChecked == true)) buttons[0].IsChecked = true;

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
        public static Panel Field(string label, UIElement control, string? hint)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            }.Wearing("Lbl"));

            // Deliberately not touching the control's alignment. Text and Choice already ask to
            // stretch; Segmented asks not to, because a three-way switch pulled across a whole
            // pane stops reading as a switch. Overriding here took that decision away from both.
            panel.Children.Add(control);

            if (!string.IsNullOrEmpty(hint)) panel.Children.Add(Small(hint!));

            return panel;
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
        public static UIElement Empty(string what, string why)
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

            row.Children.Add(Glyph(Glyphs.Empty, 22, "Faint", new Thickness(0, 1, 12, 0)));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 4, 0, 4),
                Child = row,
            };

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

        private static UIElement Banner(string message, string glyph, string wash, string edge, string ink)
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
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

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
        /// Drawn from path data rather than loaded from a file because these appear inside panes
        /// the user can theme: a PNG is one colour for ever, and a grey glyph on a dark card is
        /// the detail that makes a plug-in look unfinished.
        /// </summary>
        /// <param name="data">Path data, in a 24-unit box. One of <see cref="Glyphs"/>.</param>
        /// <param name="size">How big to draw it.</param>
        /// <param name="key">Which palette key to draw it in.</param>
        /// <param name="margin">Space around it.</param>
        public static Viewbox Glyph(string data, double size, string key, Thickness margin = default)
        {
            return new Viewbox
            {
                Width = size,
                Height = size,
                Margin = margin,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Child = Arrow(data, key),
            };
        }

        private static System.Windows.Shapes.Path Arrow(string data, string key)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = 2,
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

        /// <summary>A table.</summary>
        /// <param name="rows">The rows.</param>
        /// <param name="columns">Header and width per column. A width of 0 sizes to content.</param>
        public static ListView Table(IEnumerable<TableRow> rows, params (string Header, double Width)[] columns)
        {
            var grid = new GridView { AllowsColumnReorder = false };

            for (var i = 0; i < columns.Length; i++)
            {
                var column = new GridViewColumn
                {
                    Header = columns[i].Header,
                    DisplayMemberBinding = new Binding("[" + i.ToString(CultureInfo.InvariantCulture) + "]"),
                };

                if (columns[i].Width > 0) column.Width = columns[i].Width;
                grid.Columns.Add(column);
            }

            var table = new ListView
            {
                View = grid,
                ItemsSource = rows.ToList(),
                MaxHeight = 420,
                Margin = new Thickness(0, Gap, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

            // The row's own tone paints its text. Done as a container-generated hook rather than an
            // ItemContainerStyle trigger because the tone lives on the row object, and a trigger
            // would need a converter per tone to say the same thing.
            table.ItemContainerGenerator.StatusChanged += (s, e) =>
            {
                if (table.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    return;

                foreach (var item in table.Items)
                {
                    if (!(item is TableRow row) || row.Tone == Tone.Plain) continue;
                    if (table.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem container)
                        container.Foreground = Skin.Ink(row.Tone);
                }
            };

            return table;
        }

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

        // -----------------------------------------------------------------------------------
        // Running work
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A button that runs something long, with a progress bar, a running count and a Stop.
        ///
        /// Nothing in this product runs when a screen opens; everything runs from one of these.
        /// That is what keeps clicking a tab from freezing a federation of half a million elements,
        /// and it is also what makes the zero-setup rule honest — the button works on a raw model
        /// with nothing configured.
        ///
        /// <b>The first thing it does is paint itself.</b> The old version set a status line and
        /// then called the work, which meant that a job which never reported progress showed
        /// nothing at all: the pane simply stopped responding, and Navisworks looked hung. It now
        /// pumps the dispatcher once before the work begins, so the disabled button, the moving bar
        /// and the Stop are on screen before anything slow starts.
        /// </summary>
        /// <param name="caption">What the button says.</param>
        /// <param name="output">Where results are written. Cleared on each run.</param>
        /// <param name="work">The work. Check <see cref="Job.IsCancelled"/> in any loop.</param>
        public static Panel Runner(string caption, Panel output, Action<Job> work)
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
            go.Wearing("PrimaryBtn");

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

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, Gap, 0, 0) };
            buttons.Children.Add(go);
            buttons.Children.Add(new Border { Width = 8 });
            buttons.Children.Add(stop);

            stop.Visibility = Visibility.Collapsed;

            go.Click += (s, e) =>
            {
                output.Children.Clear();

                go.IsEnabled = false;
                stop.Visibility = Visibility.Visible;
                progress.Visibility = Visibility.Visible;
                bar.IsIndeterminate = true;
                bar.Value = 0;
                status.Text = "Starting...";

                Mouse.OverrideCursor = Cursors.Wait;
                Began();

                var job = new Job(go.Dispatcher, status, bar);
                running = job;

                // Everything above this line has to be on the glass before anything below it runs.
                // Without this one call, a job whose first act is a thirty-second host query shows
                // the user an unchanged pane and a frozen Navisworks.
                go.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);

                try
                {
                    work(job);

                    status.Text = job.IsCancelled
                        ? "Stopped after " + Seconds(job.Elapsed) + "."
                        : "Finished in " + Seconds(job.Elapsed) + ".";

                    bar.IsIndeterminate = false;
                    bar.Maximum = 1;
                    bar.Value = 1;
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

            return Stack(buttons, progress);
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
            count.ToString("N0", CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? string.Empty : "s");
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

        /// <summary>An open box: there is nothing here.</summary>
        public const string Empty = "M4,7.5 L12,3.5 L20,7.5 L20,16.5 L12,20.5 L4,16.5 Z M4,7.5 L12,11.5 L20,7.5 M12,11.5 L12,20.5";

        /// <summary>A moon: the theme control.</summary>
        public const string Theme = "M20,13.5 A8.5,8.5 0 1 1 10.5,4 A6.6,6.6 0 0 0 20,13.5 Z";
    }
}
