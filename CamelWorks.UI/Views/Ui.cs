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
using CamelWorks.Core.Data;
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
            Content = content,
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
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The box asked for a comfortable width when it was made. In a 470-pixel pane with a
            // button beside it there is no such width, and a MinWidth the column cannot honour
            // pushes the button off the edge.
            box.MinWidth = 90;
            row.Children.Add(box);

            var browse = Button("Browse", () => Pick(box, kind, filter));
            browse.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);

            return Field(label, row, hint);
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

                    if (save.ShowDialog() == true) box.Text = save.FileName;
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

                if (open.ShowDialog() != true) return;

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

            Free(control);

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
        /// Several decisions taken in order, because each one depends on the last.
        ///
        /// <b>Only where the dependency is real.</b> You cannot map a spreadsheet's columns before
        /// you have read the spreadsheet, and you cannot preview a write before you have mapped
        /// them. Where the decisions are independent a wizard is a form with extra clicks, and a
        /// form is the better answer.
        ///
        /// <b>The irreversible thing lives on the last step and nowhere else.</b> That is most of
        /// the value: a screen showing every control at once puts "Write it into the model" beside
        /// the box you are still typing a file path into.
        /// </summary>
        /// <param name="steps">The steps, in order.</param>
        /// <param name="finish">
        /// The caption of the main action, on the last step - or null when the last step's own
        /// surface already carries it. A wizard whose last step holds a two-stage commit does not
        /// want a third button above it that says the same thing and does nothing.
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

            // The spine: every step down the left, where a surface's setup column would be, with
            // its state visible — the shape of the whole job before the first decision is taken.
            // The current step's surface takes the stage, and the way forward sits at the spine's
            // foot, in the same place a tool's one action always is.
            var spine = new StackPanel();
            var body = new ContentControl();
            var why = Small(string.Empty);
            why.TextWrapping = TextWrapping.Wrap;
            var back = Quiet("Back", () => { });
            var next = Primary("Next", () => { });
            next.HorizontalAlignment = HorizontalAlignment.Stretch;

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
                        Margin = new Thickness(0, 4, 0, 4),
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

            return Bench.Surface(spine, Stack(next, back, why), body, setupWidth: 224);
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

            MessageBox.Show(message, "CamelWorks", MessageBoxButton.OK, MessageBoxImage.Information);
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
            card.Click += (s, e) => onOpen?.Invoke();

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
        public static UIElement Empty(string what, string why, string? caption, Action? onClick)
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

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 4, 0, 4),
                Child = row,
            };

            if (caption != null && onClick != null)
                text.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0),
                    Children = { Button(caption, onClick) },
                });

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

        /// <summary>Background worth having, with the one thing you can do about it.</summary>
        /// <param name="message">The note.</param>
        /// <param name="caption">What the button says.</param>
        /// <param name="onClick">What it does.</param>
        public static UIElement Info(string message, string caption, Action onClick) =>
            Banner(message, Glyphs.Info, "BlueBg", "BlueBd", "BlueTx", caption, onClick);

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

            var table = new ListView
            {
                View = grid,
                ItemsSource = rows.ToList(),
                Margin = new Thickness(0, Gap, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };

            // How tall a table may be is a question about the pane, not a number. It was 420,
            // which overflowed the dock pane at its 340 minimum and left most of a tall monitor
            // empty - the same figure doing the wrong thing at both ends.
            //
            // Bound to the scroll view the screen sits in, which is the height the pane actually
            // gave it. No cycle: a ScrollViewer's own height is what its parent allocated and does
            // not depend on how tall its content turns out to be. Two thirds, so a table never
            // fills the screen it is one part of, and a floor so a collapsed pane still shows rows
            // rather than a header and a scrollbar.
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
            void Fit()
            {
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

            // A 3px bar down the left of the row, not the row's text.
            //
            // Painting every cell with the tone made a job name green in one row and red in the
            // next, and turned a Model column into a rainbow — colouring the one fact on the row
            // that is the same in every row. The bar says the same thing and says it once.
            table.ItemContainerGenerator.StatusChanged += (s, e) =>
            {
                if (table.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    return;

                foreach (var item in table.Items)
                {
                    if (!(item is TableRow row)) continue;
                    if (!(table.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem container)) continue;

                    if (row.Tone != Tone.Plain) container.BorderBrush = Skin.Ink(row.Tone);
                }
            };

            return table;
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
        /// A table, and the bar saying what can be done to the rows picked in it.
        ///
        /// Every table in the product should have one. A table somebody can only act on a row at a
        /// time is a table they act on with the mouse forty times - which was the complaint that
        /// started this: no multi-select, no select-all, no way to clear.
        ///
        /// The bar carries four things, each for a reason:
        ///
        ///   * the <b>count</b>, because a selection made by shift-clicking down a long list is one
        ///     somebody wants confirmed before acting on it;
        ///   * <b>Select all</b> and <b>Clear</b>, because doing either by hand is the tax;
        ///   * the <b>actions</b>, disabled until something is picked, so the bar reads as
        ///     unavailable rather than broken;
        ///   * the <b>count inside each caption</b>, so the last thing read before pressing says
        ///     how much it is about to happen to.
        /// </summary>
        /// <param name="table">The table. It is switched to multi-select here.</param>
        /// <param name="actions">What can be done to the picked rows.</param>
        public static UIElement Picking(ListView table, params OnPicked[] actions)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            table.Multi();

            var bar = new StackPanel { Orientation = Orientation.Horizontal };

            // Where an action says what it did. Below the bar, so a sentence about eleven rows sits
            // under the button that said eleven.
            var said = Stack();

            // Not "Count". A local function hides an outer method of the same name outright, so a
            // local Count() would make the Count(int, string) this uses uncallable.
            var tally = Line(string.Empty, 0.75);
            tally.VerticalAlignment = VerticalAlignment.Center;

            var all = Link("Select all", () => table.SelectAll());
            var none = Link("Clear", () => table.UnselectAll());

            var buttons = new List<System.Windows.Controls.Button>();

            foreach (var action in actions ?? Array.Empty<OnPicked>())
            {
                var here = action;

                var button = here.Primary
                    ? Primary(here.Caption.Replace("{n}", "0"), () => here.Act(table.PickedMany(), said))
                    : Quiet(here.Caption.Replace("{n}", "0"), () => here.Act(table.PickedMany(), said));

                if (here.Danger) button.Wearing("DangerBtn");

                button.Margin = new Thickness(Gap, 0, 0, 0);
                button.Tag = here.Caption;

                buttons.Add(button);
                bar.Children.Add(button);
            }

            void Say()
            {
                var picked = table.SelectedItems.Count;
                var total = table.Items.Count;

                tally.Text = picked == 0
                    ? Count(total, "row")
                    : picked.ToString(CultureInfo.InvariantCulture)
                      + " of " + Count(total, "row") + " picked";

                foreach (var button in buttons)
                {
                    button.IsEnabled = picked > 0;

                    if (button.Tag is string caption)
                        button.Content = caption.Replace(
                            "{n}", picked.ToString(CultureInfo.InvariantCulture));
                }
            }

            table.SelectionChanged += (s, e) => Say();
            Say();

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(tally);
            left.Children.Add(new Border { Width = Gap });
            left.Children.Add(all);
            left.Children.Add(new Border { Width = Gap });
            left.Children.Add(none);

            var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, Gap, 0, 0) };
            DockPanel.SetDock(left, Dock.Left);
            DockPanel.SetDock(bar, Dock.Right);
            row.Children.Add(left);
            row.Children.Add(bar);

            return Stack(table, row, said);
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

        /// <summary>Run an action under a job, reporting into <paramref name="output"/>.</summary>
        private static void Run(Panel output, Action<Job> work)
        {
            var status = new TextBlock().Wearing("Hint");
            var bar = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 6) };

            output.Children.Clear();
            output.Children.Add(bar);
            output.Children.Add(status);

            Mouse.OverrideCursor = Cursors.Wait;
            Began();

            var job = new Job(output.Dispatcher, status, bar);
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
        /// <param name="primary">
        /// Whether this is the action the card is for. False for the second long-running button on
        /// a card: two primaries is the same as none, because the reader still has to work out
        /// which of them the screen wanted them to press.
        /// </param>
        public static Panel Runner(string caption, Panel output, Action<Job> work, bool primary = true)
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
            go.Wearing(primary ? "PrimaryBtn" : "Btn");

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

        /// <summary>A stack of sheets: models, or a federation.</summary>
        public const string Model = "M12,3 L21,7.5 L12,12 L3,7.5 Z M3,12 L12,16.5 L21,12 M3,16.5 L12,21 L21,16.5";

        /// <summary>Overlapping panes: appearance layers.</summary>
        public const string Layers = "M4,9 L11,9 L11,20 L4,20 Z M13,4 L20,4 L20,15 L13,15 Z";

        /// <summary>Two shapes meeting: a clash.</summary>
        public const string Clash = "M4,12 L11,12 M13,12 L20,12 M12,4 L12,11 M12,13 L12,20 M9,9 L15,15 M15,9 L9,15";

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

        /// <summary>An arrow onward: this takes you somewhere.</summary>
        public const string Onward = "M5,12 L18,12 M13,7 L18,12 L13,17";

        /// <summary>An arrow back: the previous one.</summary>
        public const string Backward = "M19,12 L6,12 M11,7 L6,12 L11,17";

        /// <summary>A box drawn round something: isolate it.</summary>
        public const string Boxed = "M4,4 L20,4 L20,20 L4,20 Z M9,9 L15,9 L15,15 L9,15 Z";

        /// <summary>A ruler: a measured dimension.</summary>
        public const string Ruler = "M3,9 L21,9 L21,15 L3,15 Z M7,9 L7,12 M11,9 L11,13 M15,9 L15,12 M19,9 L19,13";
    }
}
