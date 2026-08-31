using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The parts the clash board needed that the widget vocabulary did not carry yet.
    ///
    /// <b>Why they are here and not in <see cref="Ui"/>.</b> Every one of these is a candidate for
    /// the vocabulary proper — a filter you can see and take off, a value that is its own control,
    /// a menu behind one symbol — and none of them has a second caller yet. A widget promoted to
    /// <c>Ui</c> on its first use is a widget whose shape was decided by one screen, and the two
    /// mistakes that follow are a parameter list bent to a second caller and a helper nobody can
    /// change because forty screens use it. They live one file away until a second screen wants
    /// them, and then they move up with two call sites arguing about the signature rather than one.
    ///
    /// Everything below is built out of <see cref="Skin"/> keys and <see cref="Ui"/> primitives, so
    /// the theme reaches them the same way it reaches everything else: through
    /// <c>DynamicResource</c>, never a brush resolved once at build time.
    /// </summary>
    public static class Parts
    {
        /// <summary>The height of a chip, and of the inline control a chip becomes.</summary>
        private const double ChipHeight = 26;

        /// <summary>
        /// One line of a menu: a verb, a label, a divider, or a verb that cannot be pressed.
        ///
        /// A class with named factories rather than a constructor with six optional arguments,
        /// because a menu written as a list of positional tuples is a list nobody can read at the
        /// call site — and these menus are read far more often than they are written.
        /// </summary>
        public sealed class Entry
        {
            private Entry()
            {
            }

            /// <summary>What the line says.</summary>
            public string Text { get; private set; } = string.Empty;

            /// <summary>The glyph in front of it, or null for none.</summary>
            public string? Glyph { get; private set; }

            /// <summary>The quieter second line under it, or null for none.</summary>
            public string? Why { get; private set; }

            /// <summary>Whether it removes or overwrites something.</summary>
            public bool IsDanger { get; private set; }

            /// <summary>Whether this is a divider rather than a line.</summary>
            public bool IsRule { get; private set; }

            /// <summary>Whether this is a heading rather than something to press.</summary>
            public bool IsLabel { get; private set; }

            /// <summary>Whether it is shown but cannot be pressed, with <see cref="Why"/> saying so.</summary>
            public bool IsOff { get; private set; }

            /// <summary>What pressing it does, or null when there is nothing behind it yet.</summary>
            public Action? OnAct { get; private set; }

            /// <summary>Whether this line can be pressed at all.</summary>
            public bool Presses => !IsRule && !IsLabel && !IsOff;

            /// <summary>A verb.</summary>
            /// <param name="text">What it says.</param>
            /// <param name="glyph">One of <see cref="Glyphs"/>, or null.</param>
            /// <param name="onAct">What it does.</param>
            public static Entry Act(string text, string? glyph, Action? onAct) =>
                new Entry { Text = text, Glyph = glyph, OnAct = onAct };

            /// <summary>A verb with a line under it saying what it is for.</summary>
            /// <param name="text">What it says.</param>
            /// <param name="glyph">One of <see cref="Glyphs"/>, or null.</param>
            /// <param name="why">The quieter line under it.</param>
            /// <param name="onAct">What it does.</param>
            public static Entry Saying(string text, string? glyph, string why, Action? onAct) =>
                new Entry { Text = text, Glyph = glyph, Why = why, OnAct = onAct };

            /// <summary>
            /// A verb that is shown and cannot be pressed, with the reason on it.
            ///
            /// Shown rather than left out: a way out that vanishes teaches nobody it exists, and
            /// "this is a published NWD" is the answer somebody is looking for when they go
            /// hunting for the sync that is not there.
            /// </summary>
            /// <param name="text">What it says.</param>
            /// <param name="glyph">One of <see cref="Glyphs"/>, or null.</param>
            /// <param name="why">Why it cannot be pressed.</param>
            public static Entry Barred(string text, string? glyph, string why) =>
                new Entry { Text = text, Glyph = glyph, Why = why, IsOff = true };

            /// <summary>A verb that removes or overwrites something.</summary>
            /// <param name="text">What it says.</param>
            /// <param name="glyph">One of <see cref="Glyphs"/>, or null.</param>
            /// <param name="onAct">What it does.</param>
            public static Entry Danger(string text, string? glyph, Action? onAct) =>
                new Entry { Text = text, Glyph = glyph, IsDanger = true, OnAct = onAct };

            /// <summary>A heading over the lines under it, which is not itself pressable.</summary>
            /// <param name="text">What it says.</param>
            public static Entry Heading(string text) =>
                new Entry { Text = text, IsLabel = true };

            /// <summary>A divider between two groups of lines.</summary>
            public static Entry Rule() => new Entry { IsRule = true };
        }

        // -----------------------------------------------------------------------------------
        // Chips
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A filter, as a visible object you can take off.
        ///
        /// The whole answer to "I cannot tell what this board is filtered to": every narrowing in
        /// force is a chip on screen, and removing it is one press on the chip rather than a hunt
        /// through the window that set it.
        /// </summary>
        /// <param name="label">What kind of filter it is — "Status", "Discipline".</param>
        /// <param name="value">What it is set to.</param>
        /// <param name="onRemove">Takes the filter off, or null for a chip that only reads.</param>
        /// <param name="accent">True for the board's own chips, which sit on the accent wash.</param>
        public static UIElement FilterChip(string label, string value, Action? onRemove,
                                           bool accent = false)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var kind = new TextBlock
            {
                Text = label ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };

            kind.Tint(TextBlock.ForegroundProperty, "Muted");
            row.Children.Add(kind);

            var what = new TextBlock
            {
                Text = value ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 0, 0, 0),
            };

            what.Tint(TextBlock.ForegroundProperty, "Text");
            row.Children.Add(what);

            var chip = new Border
            {
                Height = ChipHeight,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, onRemove == null ? 9 : 6, 0),
                Margin = new Thickness(0, 0, 6, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Child = row,
            };

            chip.Tint(Border.BackgroundProperty, accent ? "AccentWash" : "Nav");
            chip.Tint(Border.BorderBrushProperty, accent ? "Accent" : "Bd");

            if (onRemove == null) return chip;

            var cross = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Remove " + label + " " + value,
                Child = Ui.Glyph(Glyphs.Close, 11, "Muted"),
            };

            Lights(cross, "Press");
            cross.MouseLeftButtonUp += (s, e) => Ui.Safely(onRemove);
            row.Children.Add(cross);

            return chip;
        }

        /// <summary>
        /// A pill that turns a narrowing on and off — one discipline, one level.
        ///
        /// Distinct from <see cref="FilterChip"/> on purpose: this one offers a filter that is not
        /// in force yet, and a chip reports one that is. They are the two halves of the same idea
        /// and the eye has to be able to tell them apart, so the offer has no cross on it and the
        /// report always does.
        /// </summary>
        /// <param name="text">The value it stands for.</param>
        /// <param name="on">Whether it is currently in force.</param>
        /// <param name="onClick">Turns it on or off.</param>
        public static UIElement Pick(string text, bool on, Action onClick)
        {
            var label = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            label.Tint(TextBlock.ForegroundProperty, "Text");

            var pill = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = label,
            };

            pill.Tint(Border.BackgroundProperty, on ? "AccentWash" : "Card");
            pill.Tint(Border.BorderBrushProperty, on ? "Accent" : "Bd");

            if (onClick != null) pill.MouseLeftButtonUp += (s, e) => Ui.Safely(onClick);

            return pill;
        }

        /// <summary>
        /// A button with its glyph in front of its words.
        ///
        /// The bar for what is picked carries verbs that also exist as symbols on a row, and a
        /// row of plain word-buttons beside those symbols makes the reader learn the same verb
        /// twice. The glyph is the thing they already know it by.
        /// </summary>
        /// <param name="caption">What it says.</param>
        /// <param name="glyph">One of <see cref="Glyphs"/>.</param>
        /// <param name="primary">True for the one action the bar is currently for.</param>
        /// <param name="onClick">What it does.</param>
        public static System.Windows.Controls.Button Verb(string caption, string glyph,
                                                          bool primary, Action onClick)
        {
            var button = primary ? Ui.Primary(caption, onClick) : Ui.Button(caption, onClick);

            var row = new StackPanel { Orientation = Orientation.Horizontal };

            var mark = Ui.Glyph(glyph, 13, "Muted", new Thickness(0, 0, 7, 0));

            // PrimaryBtn paints its words literally White in both themes, because the plate under
            // them is the brand gradient in both. A palette key would follow the theme and leave
            // this glyph dark on a blue button every time the pane went dark.
            if (primary && mark.Child is System.Windows.Shapes.Path drawn)
                drawn.Stroke = Brushes.White;

            row.Children.Add(mark);

            row.Children.Add(new TextBlock
            {
                Text = caption,
                VerticalAlignment = VerticalAlignment.Center,
            });

            button.Content = row;

            return button;
        }

        /// <summary>
        /// A quiet blue pill: something about this row is worth knowing and nothing is wrong.
        ///
        /// <see cref="Ui.Pill"/> covers the four tones a value can read as — fine, worth a look,
        /// wrong, no opinion. This is the fifth thing a pill says, which is none of those: it is
        /// background, and the palette already has a tier for it.
        /// </summary>
        /// <param name="text">What it says.</param>
        public static UIElement Note(string text)
        {
            var label = new TextBlock
            {
                Text = text ?? string.Empty,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            label.Tint(TextBlock.ForegroundProperty, "BlueTx");

            var pill = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label,
            };

            pill.Tint(Border.BackgroundProperty, "BlueBg");
            pill.Tint(Border.BorderBrushProperty, "BlueBd");

            return pill;
        }

        /// <summary>
        /// A value that is also its own control.
        ///
        /// The status and the assignee on a row are edited where they are read, rather than by a
        /// verb that opens a window somewhere else and writes back to whatever was picked. That
        /// window is the reason the old bench hid the two facts a coordinator changes most.
        /// </summary>
        /// <param name="value">What it reads now, or null and empty for "not set".</param>
        /// <param name="options">What it can be set to.</param>
        /// <param name="onChange">Called with the new value.</param>
        /// <param name="tone">How the current value reads.</param>
        /// <param name="placeholder">What to show when nothing is set.</param>
        public static UIElement InlineChip(string? value, IReadOnlyList<string> options,
                                           Action<string> onChange, Tone tone = Tone.Plain,
                                           string placeholder = "Set")
        {
            var empty = string.IsNullOrEmpty(value) || value == "—";

            var label = new TextBlock
            {
                Text = empty ? placeholder : value,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            row.Children.Add(label);

            var chevron = Ui.Glyph(Glyphs.Down, 10, empty ? "Faint" : "Muted",
                                   new Thickness(5, 0, 0, 0));

            chevron.Opacity = empty ? 0.35 : 0.7;
            row.Children.Add(chevron);

            var chip = new Border
            {
                Height = 24,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 0, 7, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = row,
            };

            if (empty)
            {
                chip.Background = Brushes.Transparent;
                chip.BorderBrush = Brushes.Transparent;
                label.Tint(TextBlock.ForegroundProperty, "Faint");

                // The empty one has to be findable without being loud: nothing at rest, an edge
                // and a plate under the pointer. A permanently outlined "Assign" on every row of
                // a four-hundred-row board is four hundred boxes competing with the names.
                chip.MouseEnter += (s, e) =>
                {
                    chip.Tint(Border.BackgroundProperty, "Hover");
                    chip.Tint(Border.BorderBrushProperty, "BdStrong");
                };

                chip.MouseLeave += (s, e) =>
                {
                    chip.Background = Brushes.Transparent;
                    chip.BorderBrush = Brushes.Transparent;
                };
            }
            else
            {
                chip.Background = Skin.Wash(tone);
                chip.BorderBrush = Skin.Edge(tone);
                label.Foreground = Skin.Ink(tone);

                if (tone == Tone.Plain)
                {
                    chip.Tint(Border.BackgroundProperty, "Nav");
                    chip.Tint(Border.BorderBrushProperty, "Bd");
                    label.Tint(TextBlock.ForegroundProperty, "Muted");
                }
            }

            var list = Ui.Stack();

            foreach (var option in options ?? Array.Empty<string>())
            {
                var here = option;

                list.Children.Add(Line(here, string.Equals(here, value, StringComparison.Ordinal),
                                       () => onChange?.Invoke(here)));
            }

            Opens(chip, Floating(list, 150), PlacementMode.Bottom);

            return chip;
        }

        /// <summary>One option inside a dropdown, marked when it is the one in force.</summary>
        /// <param name="text">What it says.</param>
        /// <param name="on">Whether it is the current value.</param>
        /// <param name="onAct">What picking it does.</param>
        private static UIElement Line(string text, bool on, Action onAct)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };

            label.Tint(TextBlock.ForegroundProperty, "Text");

            var line = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = label,
            };

            if (on) line.Tint(Border.BackgroundProperty, "NavActive");
            else Lights(line, "Hover");

            line.MouseLeftButtonUp += (s, e) => Ui.Safely(onAct);

            return line;
        }

        // -----------------------------------------------------------------------------------
        // Menus
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A menu behind one symbol, for the fourth verb onwards.
        ///
        /// Three verbs on the surface and the rest one press away beats eleven verbs competing for
        /// the same glance — which is what the row this replaced actually had.
        /// </summary>
        /// <param name="glyph">One of <see cref="Glyphs"/>.</param>
        /// <param name="tip">What the symbol does, as a tooltip.</param>
        /// <param name="items">The lines, in reading order.</param>
        /// <param name="up">True to open upwards, for a symbol near the bottom of the window.</param>
        public static UIElement MoreMenu(string glyph, string tip, IReadOnlyList<Entry> items,
                                         bool up = false)
        {
            // The press is the popup's, hung on below. Ui.Icon wants a handler, and one that does
            // nothing is honest here: opening the menu is not work, so it does not belong inside
            // the busy guard every other row symbol runs under.
            var button = Ui.Icon(glyph, tip, () => { });

            Opens(button, Floating(Menu(items, false), 210),
                  up ? PlacementMode.Top : PlacementMode.Bottom);

            return button;
        }

        /// <summary>
        /// One worded button carrying the several ways work leaves the product.
        ///
        /// Worded rather than a symbol because none of these is a verb somebody does every minute,
        /// and three phrases in a row beside the primary is how the primary stops being the
        /// primary.
        /// </summary>
        /// <param name="label">What the button says.</param>
        /// <param name="glyph">One of <see cref="Glyphs"/>, in front of the words.</param>
        /// <param name="items">The lines, in reading order.</param>
        public static UIElement MenuButton(string label, string glyph, IReadOnlyList<Entry> items)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            row.Children.Add(Ui.Glyph(glyph, 13, "Muted", new Thickness(0, 0, 7, 0)));

            var caption = new TextBlock
            {
                Text = label ?? string.Empty,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };

            caption.Tint(TextBlock.ForegroundProperty, "Text");
            row.Children.Add(caption);
            row.Children.Add(Ui.Glyph(Glyphs.Up, 11, "Muted", new Thickness(7, 0, 0, 0)));

            var button = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 7, 11, 7),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = row,
            };

            button.Tint(Border.BackgroundProperty, "Card2");
            button.Tint(Border.BorderBrushProperty, "FieldBd");

            button.MouseEnter += (s, e) => button.Tint(Border.BorderBrushProperty, "Accent");
            button.MouseLeave += (s, e) => button.Tint(Border.BorderBrushProperty, "FieldBd");

            Opens(button, Floating(Menu(items, true), 260), PlacementMode.Top);

            return button;
        }

        /// <summary>
        /// The same lines, as the menu the right button opens.
        ///
        /// Built from the identical <see cref="Entry"/> list the symbol menus use, because a
        /// right-click that offers a different set from the "…" beside it is two menus to keep in
        /// step and one of them will drift. Native <see cref="ContextMenu"/> chrome rather than the
        /// skin's popup: a context menu is the one place people expect their own platform.
        /// </summary>
        /// <param name="items">The lines, in reading order.</param>
        /// <param name="before">Rebuilds the list as the menu opens, so it reads current state.</param>
        public static ContextMenu Context(Func<IReadOnlyList<Entry>> items, Action? before = null)
        {
            var menu = new ContextMenu();

            menu.Opened += (s, e) =>
            {
                before?.Invoke();
                menu.Items.Clear();

                foreach (var item in items() ?? Array.Empty<Entry>())
                {
                    if (item.IsRule)
                    {
                        menu.Items.Add(new Separator());
                        continue;
                    }

                    if (item.IsLabel)
                    {
                        menu.Items.Add(new MenuItem { Header = item.Text, IsEnabled = false });
                        continue;
                    }

                    var here = item;

                    var line = new MenuItem
                    {
                        Header = here.Text,
                        IsEnabled = here.Presses && here.OnAct != null,
                        ToolTip = here.Why,
                    };

                    if (here.Glyph != null)
                        line.Icon = Ui.Glyph(here.Glyph, 14,
                                             here.IsDanger ? "RedTx" : "Muted");

                    line.Click += (a, b) => Ui.Safely(here.OnAct);
                    menu.Items.Add(line);
                }
            };

            return menu;
        }

        /// <summary>The lines of a menu, as one stack.</summary>
        /// <param name="items">The lines.</param>
        /// <param name="worded">True when a line may carry a second, quieter line under it.</param>
        private static UIElement Menu(IReadOnlyList<Entry> items, bool worded)
        {
            var stack = Ui.Stack();

            foreach (var item in items ?? Array.Empty<Entry>())
            {
                if (item.IsRule)
                {
                    var rule = new Border { Height = 1, Margin = new Thickness(0, 3, 0, 3) };
                    rule.Tint(Border.BackgroundProperty, "Bd");
                    stack.Children.Add(rule);
                    continue;
                }

                if (item.IsLabel)
                {
                    var heading = Ui.Eyebrow(item.Text);
                    heading.Margin = new Thickness(8, 5, 8, 3);
                    stack.Children.Add(heading);
                    continue;
                }

                stack.Children.Add(Verb(item, worded));
            }

            return stack;
        }

        /// <summary>One pressable line of a menu.</summary>
        /// <param name="item">What the line says and does.</param>
        /// <param name="worded">True when the line may carry a second, quieter line under it.</param>
        private static UIElement Verb(Entry item, bool worded)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal };

            head.Children.Add(item.Glyph == null
                ? (UIElement)new Border { Width = 14 }
                : Ui.Glyph(item.Glyph, 14, item.IsDanger ? "RedTx" : "Muted"));

            var label = new TextBlock
            {
                Text = item.Text,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            label.Tint(TextBlock.ForegroundProperty, item.IsDanger ? "RedTx" : "Text");
            head.Children.Add(label);

            var body = Ui.Stack(head);

            if (worded && item.Why != null)
            {
                var why = new TextBlock
                {
                    Text = item.Why,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(22, 1, 0, 0),
                };

                why.Tint(TextBlock.ForegroundProperty, "Faint");
                body.Children.Add(why);
            }

            var line = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, worded ? 7 : 6, 8, worded ? 7 : 6),
                Background = Brushes.Transparent,
                Child = body,
            };

            if (item.IsOff)
            {
                // Faded, not plated. A disabled line that fills its background reads as a line
                // being held down, which is the fault the button styles were fixed for.
                line.Opacity = 0.55;
                line.ToolTip = item.Why;
                return line;
            }

            line.Cursor = Cursors.Hand;
            if (item.Why != null && !worded) line.ToolTip = item.Why;

            Lights(line, "Hover");
            line.MouseLeftButtonUp += (s, e) => Ui.Safely(item.OnAct);

            return line;
        }

        /// <summary>
        /// Ask for one line of text, beside the work rather than in a window.
        ///
        /// <b>Not a dialog, on purpose.</b> A modal with no owner disables every top-level window
        /// on the thread — the host's own frame among them — and Navisworks never comes back. This
        /// is a popup on the same window, so there is no owner to get wrong, and it closes when
        /// the pointer goes anywhere else.
        /// </summary>
        /// <param name="near">What it opens beside. Something long-lived on the screen.</param>
        /// <param name="prompt">What is being asked for.</param>
        /// <param name="starting">What the box starts with.</param>
        /// <param name="caption">What the button says.</param>
        /// <param name="onDone">What to do with the answer.</param>
        public static void Ask(FrameworkElement near, string prompt, string? starting,
                               string caption, Action<string> onDone)
        {
            if (near == null) return;

            var box = Ui.Text(starting ?? string.Empty, null, 240);
            box.Margin = new Thickness(0, 6, 0, 0);

            var label = new TextBlock
            {
                Text = prompt ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            };

            var popup = new Popup
            {
                PlacementTarget = near,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                HorizontalOffset = -8,
                VerticalOffset = -8,
            };

            void Done()
            {
                popup.IsOpen = false;
                onDone?.Invoke(box.Text.Trim());
            }

            var press = Ui.Primary(caption, Done);
            press.Margin = new Thickness(0, 10, 0, 0);
            press.HorizontalAlignment = HorizontalAlignment.Right;

            // Return commits it. Somebody typing a group name has both hands on the keyboard,
            // and reaching for the mouse to press a button beside the box they are typing in is
            // the kind of thing that gets called clunky and is right.
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Done();
                else if (e.Key == Key.Escape) popup.IsOpen = false;
            };

            popup.Child = Floating(Ui.Stack(label.Wearing("Body"), box, press), 260);

            // On Opened, not after setting IsOpen: the popup builds its own visual tree on a
            // later pass, and focusing a box that is not in one yet quietly does nothing — which
            // is a prompt you have to click into before you can type.
            popup.Opened += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };

            popup.IsOpen = true;
        }

        // -----------------------------------------------------------------------------------
        // Bands and bars
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A test's four figures, in one place.
        ///
        /// Four right-aligned tabular numbers rather than four columns of a table, so a rail of
        /// eighteen tests reads down rather than across — which is the whole reason the tests moved
        /// out of a table and into a rail.
        /// </summary>
        /// <param name="counts">New, active, reviewed, approved and resolved, in that order.</param>
        /// <param name="dim">True when the test has nothing open, so the row recedes.</param>
        public static UIElement CountBand(IReadOnlyList<int> counts, bool dim)
        {
            // New, active, reviewed, approved, resolved - the five Navisworks has. The last two
            // are both green because both mean closed; they are separate figures because they are
            // separate statuses, and a band that folds two of them shows a count no segment of
            // the board can be filtered to.
            var keys = new[] { "RedTx", "Text", "AmberTx", "Ok", "Ok" };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = dim ? 0.5 : 1,
            };

            for (var i = 0; i < keys.Length; i++)
            {
                var value = counts != null && i < counts.Count ? counts[i] : 0;

                var figure = new TextBlock
                {
                    Text = Skin.Num(value),
                    FontSize = 12,
                    FontWeight = value == 0 ? FontWeights.Normal : FontWeights.SemiBold,
                    MinWidth = 26,
                    Margin = new Thickness(10, 0, 0, 0),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                // Digits that line up column to column. Four proportional figures down a rail of
                // eighteen rows is four ragged columns, and the rail exists to be read down.
                figure.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty,
                                FontNumeralAlignment.Tabular);

                figure.Tint(TextBlock.ForegroundProperty, value == 0 ? "Faint" : keys[i]);
                row.Children.Add(figure);
            }

            return row;
        }

        /// <summary>
        /// A band across the table saying how the <b>view</b> is arranged, or where the loose
        /// results start.
        ///
        /// Deliberately not shaped like a group row — no tick, no chevron, no status colour —
        /// because it is not a thing in the model. A band that looked like a group row would be
        /// the product teaching somebody that arranging the view creates groups, which is the one
        /// misunderstanding this whole screen is built to prevent.
        /// </summary>
        /// <param name="glyph">One of <see cref="Glyphs"/>.</param>
        /// <param name="label">The eyebrow — what kind of band this is.</param>
        /// <param name="value">The bucket's value.</param>
        /// <param name="count">How many rows are under it.</param>
        /// <param name="onPickAll">Picks every row under it, or null for none.</param>
        public static UIElement ViewBand(string glyph, string label, string value, string count,
                                         Action? onPickAll)
        {
            var row = new DockPanel { LastChildFill = true };

            if (onPickAll != null)
            {
                var all = Ui.Link("Pick all of them", onPickAll);
                all.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(all, Dock.Right);
                row.Children.Add(all);
            }

            var words = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            words.Children.Add(Ui.Glyph(glyph, 12, "Faint", new Thickness(0, 0, 8, 0)));

            var eyebrow = Ui.Eyebrow(label);
            eyebrow.VerticalAlignment = VerticalAlignment.Center;
            words.Children.Add(eyebrow);

            var what = new TextBlock
            {
                Text = value ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            what.Wearing("Body");
            words.Children.Add(what);

            var tally = new TextBlock
            {
                Text = count ?? string.Empty,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            tally.Wearing("Hint");
            words.Children.Add(tally);

            row.Children.Add(words);

            var band = new Border
            {
                Padding = new Thickness(12, 6, 12, 6),
                BorderThickness = new Thickness(0, 1, 0, 1),
                Child = row,
            };

            band.Tint(Border.BackgroundProperty, "Bg");
            band.Tint(Border.BorderBrushProperty, "Bd");

            return band;
        }

        /// <summary>
        /// The verbs for what is picked, on a bar that only exists while something is picked.
        ///
        /// Nothing on the surface at rest, and the verbs the moment there is something to act on.
        /// The alternative — and what this replaced — is a permanent row of greyed symbols, which
        /// somebody's own words called a wall of buttons that look pressed rather than off.
        /// </summary>
        /// <param name="picked">How many rows are picked.</param>
        /// <param name="what">What one of them is called.</param>
        /// <param name="onClear">Unpicks everything.</param>
        /// <param name="verbs">The verbs, left to right.</param>
        public static UIElement SelectionBar(int picked, string what, Action onClear,
                                             params UIElement?[] verbs)
        {
            var row = new DockPanel { LastChildFill = false };

            var clear = Ui.Link("Clear", onClear);
            clear.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(clear, Dock.Right);
            row.Children.Add(clear);

            var count = new TextBlock
            {
                Text = Ui.Count(picked, what) + " picked",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };

            count.Wearing("Body");
            count.Tint(TextBlock.ForegroundProperty, "Accent");
            row.Children.Add(count);

            var divider = new Border
            {
                Width = 1,
                Height = 20,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            divider.Tint(Border.BackgroundProperty, "BdStrong");
            row.Children.Add(divider);

            foreach (var verb in verbs)
            {
                if (verb == null) continue;

                if (verb is FrameworkElement element)
                {
                    element.Margin = new Thickness(0, 0, 8, 0);
                    element.VerticalAlignment = VerticalAlignment.Center;
                }

                row.Children.Add(verb);
            }

            var bar = new Border
            {
                Padding = new Thickness(16, 8, 16, 8),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = row,
            };

            bar.Tint(Border.BackgroundProperty, "AccentWash");
            bar.Tint(Border.BorderBrushProperty, "Accent");

            return bar;
        }

        // -----------------------------------------------------------------------------------
        // The plumbing under all of it
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The one popup card shape: the product's only shadow, on a bordered card.
        /// </summary>
        /// <param name="content">What is inside it.</param>
        /// <param name="width">How narrow it may be.</param>
        private static UIElement Floating(UIElement content, double width)
        {
            var card = new Border
            {
                MinWidth = width,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),

                // Room for the shadow to fall into. A popup clips to its own bounds, so without
                // this the shadow is drawn and immediately cut off on every side.
                Margin = new Thickness(8),
                Child = content,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 2,
                    Direction = 270,
                    Opacity = 0.18,
                    Color = Colors.Black,
                },
            };

            card.Tint(Border.BackgroundProperty, "Card");
            card.Tint(Border.BorderBrushProperty, "Bd");

            return card;
        }

        /// <summary>
        /// Hang a popup off something, opened by pressing it and closed by pressing anywhere else.
        ///
        /// <b>A popup and not a window.</b> Every modal this product shows has to be owned by the
        /// window it came from, or it disables the host's own frame and Navisworks never comes
        /// back. A dropdown is not a modal and must not become one: a <see cref="Popup"/> is a
        /// layer over the same window, so there is no owner to get wrong.
        /// </summary>
        /// <param name="target">What opens it.</param>
        /// <param name="card">What is inside it.</param>
        /// <param name="where">Which side of the target it opens on.</param>
        private static void Opens(FrameworkElement target, UIElement card, PlacementMode where)
        {
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = where,
                StaysOpen = false,
                AllowsTransparency = true,
                HorizontalOffset = -8,
                VerticalOffset = where == PlacementMode.Top ? 8 : -8,
                Child = card,
            };

            // Inside the popup, so a press on an option closes the menu it was picked from. The
            // handler on the card runs after the option's own, which is what lets an option act
            // and the menu shut on one press rather than two.
            card.PreviewMouseLeftButtonUp += (s, e) => popup.IsOpen = false;

            if (target is ButtonBase press) press.Click += (s, e) => popup.IsOpen = !popup.IsOpen;
            else target.MouseLeftButtonUp += (s, e) => popup.IsOpen = !popup.IsOpen;
        }

        /// <summary>Fill a plate under the pointer, and take it away again.</summary>
        /// <param name="element">What to light up.</param>
        /// <param name="key">Which palette key the plate is.</param>
        private static void Lights(Border element, string key)
        {
            element.MouseEnter += (s, e) => element.Tint(Border.BackgroundProperty, key);
            element.MouseLeave += (s, e) => element.Background = Brushes.Transparent;
        }
    }
}
