using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The parts the clash board needed that the widget vocabulary did not yet carry.
    ///
    /// They live here rather than in <see cref="Ui"/> because each is a candidate for the
    /// vocabulary proper — a filter that can be seen and removed, a value that is its own control, a
    /// menu behind one symbol — and none has a second caller yet. A widget promoted to <c>Ui</c> on
    /// its first use has its shape decided by one screen, which leads either to a parameter list
    /// bent to fit a second caller or to a helper too widely used to change. They move up when a
    /// second screen wants them, with two call sites to settle the signature.
    ///
    /// Everything below is built from <see cref="Skin"/> keys and <see cref="Ui"/> primitives, so
    /// the theme reaches them through <c>DynamicResource</c> rather than a brush resolved once at
    /// build time.
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

        /// <summary>One thing a chip can be set to, with what it is and what it is under.</summary>
        public readonly struct ChipOption
        {
            /// <summary>Describe one option.</summary>
            /// <param name="value">What setting it writes.</param>
            /// <param name="label">What it says.</param>
            /// <param name="under">A quieter second line, or null.</param>
            /// <param name="glyph">The symbol that says what kind of thing it is, or null.</param>
            /// <param name="key">The skin key the symbol is tinted with, or null.</param>
            public ChipOption(string value, string label, string? under = null,
                              string? glyph = null, string? key = null)
            {
                _value = value ?? string.Empty;
                _label = label ?? string.Empty;
                Under = under;
                Glyph = glyph;
                Key = key;
            }

            private readonly string? _value;
            private readonly string? _label;

            /// <summary>
            /// What setting it writes.
            ///
            /// Read through the field so <c>default(ChipOption)</c> answers with an empty string
            /// rather than null: a struct promising a string its own default would not give is a
            /// null reference waiting for the first array somebody sizes before filling.
            /// </summary>
            public string Value => _value ?? string.Empty;

            /// <summary>What it says.</summary>
            public string Label => _label ?? string.Empty;

            /// <summary>A quieter second line, or null.</summary>
            public string? Under { get; }

            /// <summary>The symbol that says what kind of thing it is, or null.</summary>
            public string? Glyph { get; }

            /// <summary>The skin key the symbol is tinted with, or null.</summary>
            public string? Key { get; }
        }

        /// <summary>
        /// A chip whose options are things of different kinds, each stating its kind.
        ///
        /// A list holding a person, a role, a company and a team spells four different kinds of
        /// thing the same way, and a coordinator reading a board needs to see at a glance whether a
        /// row belongs to one person or a whole team; they mean different amounts of work. So the
        /// kind travels with the name, in the list and on the chip.
        ///
        /// The last line always offers a way to add one. An assign list with nobody on it was
        /// previously an empty dropdown, which is a dead end at the point where the team is being
        /// built.
        /// </summary>
        /// <param name="value">What it is set to now.</param>
        /// <param name="options">What it can be set to.</param>
        /// <param name="onChange">Called with the new value.</param>
        /// <param name="glyph">The symbol for what it is set to now, or null.</param>
        /// <param name="placeholder">What it says when it is set to nothing.</param>
        /// <param name="last">One more line under the options, or null.</param>
        public static UIElement KindChip(string? value, IReadOnlyList<ChipOption> options,
                                         Action<string> onChange, string? glyph = null,
                                         string placeholder = "Set", ChipOption? last = null)
        {
            var empty = string.IsNullOrEmpty(value) || value == "—";

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (!empty && glyph != null)
                row.Children.Add(Ui.Glyph(glyph, 11, "Muted", new Thickness(0, 0, 4, 0)));

            var label = new TextBlock
            {
                Text = empty ? placeholder : value,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
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
                chip.Tint(Border.BackgroundProperty, "Nav");
                chip.Tint(Border.BorderBrushProperty, "Bd");
                label.Tint(TextBlock.ForegroundProperty, "Muted");
            }

            var list = Ui.Stack();

            foreach (var option in options ?? Array.Empty<ChipOption>())
            {
                var here = option;

                list.Children.Add(Kinded(here,
                                         string.Equals(here.Value, value, StringComparison.Ordinal),
                                         () => onChange?.Invoke(here.Value)));
            }

            if (last.HasValue)
            {
                if (list.Children.Count > 0) list.Children.Add(Ui.Rule());

                var one = last.Value;

                list.Children.Add(Kinded(one, false, () => onChange?.Invoke(one.Value)));
            }

            Opens(chip, Floating(list, 210), PlacementMode.Bottom);

            return chip;
        }

        /// <summary>One option of a kind chip: its symbol, its name, and what it is under.</summary>
        /// <param name="option">The option.</param>
        /// <param name="on">Whether it is the one in force.</param>
        /// <param name="onAct">What picking it does.</param>
        private static UIElement Kinded(ChipOption option, bool on, Action onAct)
        {
            var words = new StackPanel { Orientation = Orientation.Horizontal };

            if (option.Glyph != null)
                words.Children.Add(Ui.Glyph(option.Glyph, 12, option.Key ?? "Muted",
                                            new Thickness(0, 0, 7, 0)));

            var stacked = new StackPanel();

            var label = new TextBlock
            {
                Text = option.Label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };

            label.Tint(TextBlock.ForegroundProperty, "Text");
            stacked.Children.Add(label);

            if (!string.IsNullOrEmpty(option.Under))
                stacked.Children.Add(new TextBlock
                {
                    Text = option.Under,
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 0, 0),
                }.Wearing("Hint"));

            words.Children.Add(stacked);

            var line = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = words,
            };

            if (on) line.Tint(Border.BackgroundProperty, "NavActive");

            line.MouseEnter += (s, e) => { if (!on) line.Tint(Border.BackgroundProperty, "Hover"); };
            line.MouseLeave += (s, e) => { if (!on) line.Background = Brushes.Transparent; };

            line.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Ui.Safely(onAct);
            };

            return line;
        }

        /// <summary>
        /// A chip that opens something instead of dropping a list down.
        ///
        /// Same shape as <see cref="KindChip"/> so a row reads the same, but the choice it leads
        /// to is a window - which is what a choice of several has to be.
        /// </summary>
        /// <param name="value">What it is set to now, or null for nothing.</param>
        /// <param name="glyph">The symbol for what it is set to, or null.</param>
        /// <param name="placeholder">What it says when it is set to nothing.</param>
        /// <param name="onPress">
        /// What pressing it opens, given the chip itself so a menu can hang off it rather than
        /// opening in the middle of the screen.
        /// </param>
        public static UIElement Presser(string? value, string? glyph, string placeholder,
                                        Action<FrameworkElement> onPress)
        {
            var empty = string.IsNullOrEmpty(value);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (!empty && glyph != null)
                row.Children.Add(Ui.Glyph(glyph, 11, "Muted", new Thickness(0, 0, 4, 0)));

            var label = new TextBlock
            {
                Text = empty ? placeholder : value,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis,
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
                chip.Tint(Border.BackgroundProperty, "Nav");
                chip.Tint(Border.BorderBrushProperty, "Bd");
                label.Tint(TextBlock.ForegroundProperty, "Muted");
            }

            chip.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Ui.Safely(() => onPress(chip));
            };

            return chip;
        }

        /// <summary>
        /// Tick as many of a short list as apply, in a menu under the control that opened it.
        ///
        /// A menu rather than a window, because this hangs off a chip on a row. It was previously a
        /// modal in the middle of the screen, covering the row it was about, for a choice that sits
        /// beside another chip that drops a list down. Two controls on one row should not behave as
        /// two different kinds of control.
        ///
        /// It still takes several values. A clash is commonly two parties' problem, so the ticks
        /// stay, the menu stays open while several are ticked, and the set is reported when it
        /// closes. That is what a plain drop-down cannot do.
        /// </summary>
        /// <param name="on">What the menu hangs off.</param>
        /// <param name="prompt">What the menu says at the top.</param>
        /// <param name="options">What can be ticked, each saying what kind of thing it is.</param>
        /// <param name="chosen">What is ticked when it opens.</param>
        /// <param name="more">The wording for the way to add another, or null for none.</param>
        /// <param name="onMore">What that opens.</param>
        /// <param name="onDone">Given everything ticked, once the menu closes on a change.</param>
        public static void TickMenu(FrameworkElement on, string prompt,
                                    IReadOnlyList<ChipOption> options,
                                    IReadOnlyCollection<string>? chosen,
                                    string? more, Action? onMore,
                                    Action<IReadOnlyCollection<string>> onDone)
        {
            if (on == null) return;

            var ticked = new HashSet<string>(chosen ?? Array.Empty<string>(), StringComparer.Ordinal);
            var changed = false;

            var card = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };

            card.Children.Add(new TextBlock
            {
                Text = prompt ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 4, 8, 8),
            }.Wearing("Body"));

            var list = new StackPanel();

            foreach (var option in options ?? Array.Empty<ChipOption>())
            {
                if (option.Value.Length == 0) continue;

                var here = option;

                var line = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                if (here.Glyph != null)
                    line.Children.Add(Ui.Glyph(here.Glyph, 12, here.Key ?? "Muted",
                                               new Thickness(0, 0, 7, 0)));

                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                words.Children.Add(new TextBlock { Text = here.Label }.Wearing("Body"));

                if (!string.IsNullOrEmpty(here.Under))
                    words.Children.Add(new TextBlock { Text = here.Under, FontSize = 10 }
                        .Wearing("Hint"));

                line.Children.Add(words);

                var tick = new CheckBox
                {
                    Content = line,
                    IsChecked = ticked.Contains(here.Value),
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(8, 3, 8, 3),
                    VerticalContentAlignment = VerticalAlignment.Center,
                };

                tick.Checked += (s, e) => { ticked.Add(here.Value); changed = true; };
                tick.Unchecked += (s, e) => { ticked.Remove(here.Value); changed = true; };

                list.Children.Add(tick);
            }

            if (list.Children.Count == 0)
            {
                var none = Ui.Small("No team members yet.");

                none.Margin = new Thickness(8, 4, 8, 6);
                list.Children.Add(none);
            }

            card.Children.Add(new ScrollViewer
            {
                Content = list,
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var popup = new Popup
            {
                PlacementTarget = on,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                HorizontalOffset = -8,
                VerticalOffset = -8,
            };

            var foot = Menu(new List<Entry?>
            {
                Entry.Rule(),
                Entry.Act("Unassigned \u2014 clear it", Glyphs.Close, () =>
                {
                    ticked.Clear();
                    changed = true;
                    popup.IsOpen = false;
                }),
                more == null || onMore == null
                    ? null
                    : Entry.Act(more, Glyphs.Add, () =>
                    {
                        // The menu goes first, so what it opens is not fighting a popup that is
                        // still closing - and nothing is reported, because adding somebody is
                        // not the same act as assigning to them.
                        changed = false;
                        popup.IsOpen = false;
                        Ui.Safely(onMore);
                    }),
                Entry.Act("Done", Glyphs.Tick, () => popup.IsOpen = false),
            }.Where(e => e != null).Select(e => e!).ToList(), false);

            card.Children.Add(foot);

            popup.Child = Floating(card, 250);

            popup.Closed += (s, e) =>
            {
                if (!changed) return;

                changed = false;
                Ui.Safely(() => onDone?.Invoke(ticked));
            };

            popup.IsOpen = true;
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

            line.MouseLeftButtonUp += (s, e) =>
            {
                // Shut first, then act. The option has already been chosen by the time this runs,
                // so closing cannot swallow it - and whatever the option opens is not fighting a
                // menu still standing over it.
                Shut(line);
                Ui.Safely(onAct);
            };

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
        /// <param name="on">True when what the menu holds is in force, so the symbol says so.</param>
        public static UIElement MoreMenu(string glyph, string tip, IReadOnlyList<Entry> items,
                                         bool up = false, bool on = false)
        {
            // The press is the popup's, hung on below. Ui.Icon wants a handler, and one that does
            // nothing is honest here: opening the menu is not work, so it does not belong inside
            // the busy guard every other row symbol runs under.
            var button = Ui.Icon(glyph, tip, () => { });

            // A control that is doing something says so. A menu holding a filter or a sort with
            // nothing on screen admitting it is a board quietly hiding half its rows, which is the
            // fault the coloured funnel on the rail already exists to prevent - and the board's
            // own menu did not do it.
            if (on) button.Content = Ui.Glyph(glyph, 15, "Accent");

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
        /// Ask for one line of text, and do something with it.
        ///
        /// A window rather than a popup. This was a <c>Popup</c>, and inside the Navisworks host a
        /// popup is its own top-level window that never receives the keyboard: the box focused, the
        /// caret blinked, and nothing typed arrived. Opened from a double-click it closed
        /// immediately, because the second release of the double-click lands outside the popup and
        /// a popup that does not stay open treats that as a click-away.
        ///
        /// This was reported three ways: the rename window flashing and vanishing, the comment box
        /// refusing input, and "move into a folder" refusing a folder. One cause. The fix is to use
        /// the same kind of window as every other dialogue here — owned by the screen that opened
        /// it, modal, and in the keyboard's scope.
        /// </summary>
        /// <param name="near">The screen this belongs to, which becomes its owner.</param>
        /// <param name="prompt">What is being asked.</param>
        /// <param name="starting">What the box starts with.</param>
        /// <param name="caption">What the commit button says.</param>
        /// <param name="onDone">Given the trimmed text, once somebody commits.</param>
        public static void Ask(FrameworkElement near, string prompt, string? starting,
                               string caption, Action<string> onDone)
        {
            _ = near;

            var box = Ui.Text(starting ?? string.Empty, null, 320);

            var label = new TextBlock
            {
                Text = prompt ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };

            var window = new Window
            {
                Title = "CamelWorks",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            string? said = null;

            void Done()
            {
                said = box.Text.Trim();
                window.Close();
            }

            var press = Ui.Primary(caption, Done);
            press.MinWidth = 92;

            var cancel = Ui.Quiet("Cancel", () => window.Close());
            cancel.Margin = new Thickness(0, 0, 8, 0);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(press);

            // Return commits, Escape leaves. Somebody typing a group name has both hands on the
            // keyboard, and reaching for the mouse to press a button beside the box they are
            // typing in is the kind of thing that gets called clunky and is right.
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Done();
                else if (e.Key == Key.Escape) window.Close();
            };

            var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

            body.Children.Add(label.Wearing("Body"));
            body.Children.Add(box);
            body.Children.Add(buttons);

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            // On Loaded, not before: focusing a box whose visual tree has not been built yet
            // quietly does nothing, which is a prompt you have to click into before you can type.
            window.Loaded += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };

            window.ShowDialog();

            // After the window has gone, so whatever this opens next is not fighting a dialogue
            // that is still closing.
            if (said != null) onDone?.Invoke(said);
        }

        /// <summary>
        /// Choose a colour for something, or take the one it has away.
        ///
        /// The same swatches the legend and the style page offer, in a window, for the places
        /// that reach a colour through a menu rather than through a panel that is already open.
        /// </summary>
        /// <param name="near">The element this belongs to; the window is owned by the host.</param>
        /// <param name="prompt">What is being coloured.</param>
        /// <param name="held">
        /// Where the swatch strip is putting the colour as it is pressed. The caller builds the
        /// strip against this, so what the window hands back is whatever was last chosen in it.
        /// </param>
        /// <param name="swatches">The swatch strip, built by the caller against <paramref name="held"/>.</param>
        /// <param name="onDone">Called with the colour chosen, or empty when it was cleared.</param>
        public static void Pick(FrameworkElement near, string prompt, Box held,
                                UIElement swatches, Action<string> onDone)
        {
            _ = near;

            var label = new TextBlock
            {
                Text = prompt ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };

            var window = new Window
            {
                Title = "CamelWorks",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            string? said = null;


            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };

            // No colour is an explicit choice rather than an absence. A folder coloured by mistake
            // needs a way back to the theme's own ground, and that must not mean finding the swatch
            // that stands for "none".
            var clear = Ui.Quiet("No colour", () =>
            {
                said = string.Empty;
                window.Close();
            });

            clear.Margin = new Thickness(0, 0, 8, 0);

            var press = Ui.Primary("Use it", () =>
            {
                said = (held?.Colour ?? string.Empty).Trim();
                window.Close();
            });

            press.MinWidth = 92;

            buttons.Children.Add(clear);
            buttons.Children.Add(press);

            var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16), MaxWidth = 360 };

            body.Children.Add(label.Wearing("Body"));
            body.Children.Add(swatches);
            body.Children.Add(buttons);

            var ground = new Border { Child = body };

            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) window.Close();
            };

            window.ShowDialog();

            if (said != null) onDone?.Invoke(said);
        }

        /// <summary>A colour on its way from a swatch strip to the window that closes over it.</summary>
        public sealed class Box
        {
            /// <summary>The colour chosen so far.</summary>
            public string Colour { get; set; } = string.Empty;
        }

        /// <summary>
        /// Say what is about to happen and let it happen, or not.
        ///
        /// For an act that reaches the document in bulk and cannot be undone by pressing the
        /// same thing again. The prompt says what will be done and to how much; the button says
        /// the verb rather than "OK", so the last thing read before the press is the act itself.
        /// </summary>
        /// <param name="near">The element this belongs to; the window is owned by the host.</param>
        /// <param name="prompt">What is about to happen, in full.</param>
        /// <param name="doIt">The verb on the button that does it.</param>
        /// <param name="onYes">Called when the verb is pressed, and not otherwise.</param>
        public static void Sure(FrameworkElement near, string prompt, string doIt, Action onYes)
        {
            _ = near;

            var window = new Window
            {
                Title = "CamelWorks",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var said = false;

            var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16), MaxWidth = 420 };

            body.Children.Add(new TextBlock
            {
                Text = prompt ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            }.Wearing("Body"));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var cancel = Ui.Quiet("Cancel", () => window.Close());
            cancel.MinWidth = 92;
            buttons.Children.Add(cancel);

            var go = Ui.Primary(doIt, () =>
            {
                said = true;
                window.Close();
            });

            go.MinWidth = 92;
            go.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(go);

            body.Children.Add(buttons);

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();

            if (said) onYes?.Invoke();
        }


        /// <summary>
        /// Ask which of a known set of things, with the option of a new one.
        ///
        /// Not <see cref="Ask"/>: asking the user to type a name already on their screen requires
        /// them to remember and spell it, and one typo creates a second thing that looks like the
        /// first — two folders called Level 03 and Level 3, with nothing to say which tests went
        /// where. A name that exists is picked; a name that does not is typed once.
        /// </summary>
        /// <param name="near">The screen this belongs to, which becomes its owner.</param>
        /// <param name="prompt">What is being asked.</param>
        /// <param name="known">What already exists, in the order to offer it.</param>
        /// <param name="current">What the subject is on now, ticked in the list. May be null.</param>
        /// <param name="fresh">What the "something new" line says, or null to leave it out.</param>
        /// <param name="none">What the "none of them" line says, or null to leave it out.</param>
        /// <param name="onDone">Given the chosen name, or the empty string for none.</param>
        /// <param name="starting">What the new-one box starts with, when there is a sensible guess.</param>
        public static void Choose(FrameworkElement near, string prompt,
                                  IReadOnlyList<string> known, string? current,
                                  string? fresh, string? none, Action<string> onDone,
                                  string? starting = null)
        {
            _ = near;

            var window = new Window
            {
                Title = "CamelWorks",
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            string? said = null;

            void Done(string what)
            {
                said = what;
                window.Close();
            }

            var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16), MinWidth = 300 };

            var label = new TextBlock
            {
                Text = prompt ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };

            body.Children.Add(label.Wearing("Body"));

            var list = new StackPanel();

            foreach (var one in known ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(one)) continue;

                var here = one;
                var on = string.Equals(here, current, StringComparison.OrdinalIgnoreCase);

                var press = Ui.Quiet(on ? here + "  ·  where it is now" : here, () => Done(here));

                press.HorizontalAlignment = HorizontalAlignment.Stretch;
                press.HorizontalContentAlignment = HorizontalAlignment.Left;
                press.Margin = new Thickness(0, 0, 0, 4);
                press.IsEnabled = !on;

                list.Children.Add(press);
            }

            // Scrolled rather than grown: a job with forty folders should not open a window taller
            // than the screen it is opening on.
            body.Children.Add(new ScrollViewer
            {
                Content = list,
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            if (fresh != null)
            {
                var box = Ui.Text(starting ?? string.Empty, null, 300);
                box.Margin = new Thickness(0, 10, 0, 0);

                void Make()
                {
                    var typed = box.Text.Trim();
                    if (typed.Length > 0) Done(typed);
                }

                box.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter) Make();
                    else if (e.Key == Key.Escape) window.Close();
                };

                body.Children.Add(Ui.Rule());
                body.Children.Add(Ui.Eyebrow(fresh));
                body.Children.Add(box);

                var make = Ui.Primary("Move", Make);
                make.MinWidth = 92;
                make.HorizontalAlignment = HorizontalAlignment.Right;
                make.Margin = new Thickness(0, 8, 0, 0);

                body.Children.Add(make);
            }

            if (none != null)
            {
                var off = Ui.Quiet(none, () => Done(string.Empty));

                off.HorizontalAlignment = HorizontalAlignment.Left;
                off.Margin = new Thickness(0, 10, 0, 0);

                body.Children.Add(off);
            }

            var cancel = Ui.Quiet("Cancel", () => window.Close());

            cancel.HorizontalAlignment = HorizontalAlignment.Right;
            cancel.Margin = new Thickness(0, 12, 0, 0);

            body.Children.Add(cancel);

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();

            if (said != null) onDone?.Invoke(said);
        }

        /// <summary>
        /// Pick values out of however many there are, by typing at them.
        ///
        /// Not a menu. <see cref="Choose"/> lists what it is given, which suits a job's folders or a
        /// test's groups — a dozen of each. A property column over a real federation carries
        /// hundreds of distinct values, and a drop-down of those hangs off the filter symbol and
        /// covers the board behind it. The list here is filtered as the user types and capped at a
        /// readable length, and the cap states how many values it is not showing.
        ///
        /// Counts are shown because the choice turns on them: narrowing to a value that holds four
        /// of six hundred results is a different action from narrowing to one that holds four
        /// hundred, and this is the only place that is visible.
        ///
        /// Several values can be picked at once. A column narrowed to one value answers "show me
        /// the ducts"; the usual question is "mechanical and electrical, not structural". So the
        /// values tick, the window stays open while several are ticked, and it reports the set when
        /// it closes.
        /// </summary>
        /// <param name="near">The element this belongs to; the window is owned by the host.</param>
        /// <param name="prompt">What the window says at the top.</param>
        /// <param name="values">Every value and how many rows carry it, commonest first.</param>
        /// <param name="chosen">The values in force, which come back ticked.</param>
        /// <param name="any">The wording for taking the narrowing off, or null for no such button.</param>
        /// <param name="onDone">Called with the ticked values; an empty set means no narrowing.</param>
        public static void Search(FrameworkElement near, string prompt,
                                  IReadOnlyList<KeyValuePair<string, int>> values,
                                  IReadOnlyCollection<string>? chosen, string? any,
                                  Action<IReadOnlyCollection<string>> onDone)
        {
            _ = near;

            // Enough rows that a job's whole set library is on one list. The old sixty was a
            // scanning limit borrowed from a menu, and it made "select all" a lie: a filter
            // matching two hundred showed sixty of them, so the only reachable answer was the
            // sixty. Selecting acts on every match now, shown or not, and the note says so.
            const int Most = 400;

            var window = new Window
            {
                Title = "CamelWorks",
                Width = 620,
                Height = 560,
                MinWidth = 460,
                MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var ticked = new HashSet<string>(chosen ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);
            var changed = false;

            var all = values ?? new List<KeyValuePair<string, int>>();
            var showing = new List<KeyValuePair<string, int>>();
            var matching = new List<KeyValuePair<string, int>>();

            // Where the last row was ticked, so shift and a click take everything between. This
            // is the gesture every list of anything has had since 1984, and twenty-five saved
            // sets ticked one at a time is what its absence costs.
            var anchor = -1;

            var body = new DockPanel { Margin = new Thickness(18, 16, 18, 16), LastChildFill = true };

            var label = new TextBlock
            {
                Text = prompt ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };

            DockPanel.SetDock(label.Wearing("Body"), Dock.Top);
            body.Children.Add(label);

            var box = Ui.Text(string.Empty, "Search", null, 0);
            box.Margin = new Thickness(0, 0, 0, 10);
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            DockPanel.SetDock(box, Dock.Top);
            body.Children.Add(box);

            var list = new StackPanel();
            var note = Ui.Small(string.Empty);
            var chose = Ui.Small(string.Empty);

            // ---- the header, which is also the select-all ---------------------------------
            var master = new CheckBox
            {
                IsThreeState = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };

            var head = new DockPanel { LastChildFill = true, Margin = new Thickness(2, 0, 2, 4) };

            var heading = new TextBlock
            {
                Text = "Count",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                MinWidth = 54,
                TextAlignment = TextAlignment.Right,
            };

            DockPanel.SetDock(master, Dock.Left);
            head.Children.Add(master);

            DockPanel.SetDock(heading.Wearing("Caps"), Dock.Right);
            head.Children.Add(heading);

            head.Children.Add(new TextBlock
            {
                Text = "Name",
                VerticalAlignment = VerticalAlignment.Center,
            }.Wearing("Caps"));

            var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 4) };

            rule.Tint(Border.BackgroundProperty, "Bd");

            var header = new StackPanel();

            header.Children.Add(head);
            header.Children.Add(rule);

            DockPanel.SetDock(header, Dock.Top);
            body.Children.Add(header);

            void Say()
            {
                chose.Text = ticked.Count == 0
                    ? "Nothing selected."
                    : Ui.Count(ticked.Count, "selected");

                // The header box says what the matching rows are, which is what pressing it acts
                // on - not what the whole library is, which nobody can see from here.
                var on = matching.Count(v => ticked.Contains(v.Key ?? string.Empty));

                master.IsChecked = matching.Count == 0 || on == 0 ? false
                    : on == matching.Count ? true
                    : (bool?)null;
            }

            void Paint()
            {
                foreach (var child in list.Children)
                    if (child is Border row && row.Tag is string key && row.Child is DockPanel line
                        && line.Children.Count > 0 && line.Children[0] is CheckBox tick)
                    {
                        var on = ticked.Contains(key);

                        tick.IsChecked = on;
                        row.Background = on ? Skin.Of("NavActive") : Brushes.Transparent;
                    }

                Say();
            }

            void Fill()
            {
                var typed = box.Text.Trim();

                list.Children.Clear();
                anchor = -1;

                matching = all.Where(v => typed.Length == 0
                                          || (v.Key ?? string.Empty).IndexOf(
                                                 typed, StringComparison.OrdinalIgnoreCase) >= 0)
                              .ToList();

                showing = matching.Take(Most).ToList();

                for (var at = 0; at < showing.Count; at++)
                {
                    var here = showing[at].Key ?? string.Empty;
                    var mine = at;

                    var tick = new CheckBox
                    {
                        IsChecked = ticked.Contains(here),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                        Focusable = false,
                        IsHitTestVisible = false,
                    };

                    var tally = new TextBlock
                    {
                        Text = Skin.Num(showing[at].Value),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0),
                        MinWidth = 54,
                        TextAlignment = TextAlignment.Right,
                    };

                    var line = new DockPanel { LastChildFill = true };

                    DockPanel.SetDock(tick, Dock.Left);
                    line.Children.Add(tick);

                    DockPanel.SetDock(tally.Wearing("Hint"), Dock.Right);
                    line.Children.Add(tally);

                    line.Children.Add(new TextBlock
                    {
                        Text = here,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    }.Wearing("Body"));

                    // The whole row, not a twelve-pixel box on the end of it. A list somebody is
                    // going to work down twenty-five times is a list where the target is the row.
                    var row = new Border
                    {
                        Child = line,
                        Tag = here,
                        Padding = new Thickness(2, 5, 2, 5),
                        CornerRadius = new CornerRadius(4),
                        Background = ticked.Contains(here) ? Skin.Of("NavActive") : Brushes.Transparent,
                        Cursor = Cursors.Hand,
                    };

                    row.MouseEnter += (s, e) =>
                    {
                        if (!ticked.Contains(here)) row.Tint(Border.BackgroundProperty, "Hover");
                    };

                    row.MouseLeave += (s, e) =>
                    {
                        row.Background = ticked.Contains(here)
                            ? Skin.Of("NavActive") : Brushes.Transparent;
                    };

                    row.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                    {
                        e.Handled = true;
                        changed = true;

                        // Shift takes the run from the last row touched to this one, and gives
                        // every row in it whatever this one is about to become.
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                            && anchor >= 0 && anchor < showing.Count)
                        {
                            var wanted = !ticked.Contains(here);
                            var from = Math.Min(anchor, mine);
                            var to = Math.Max(anchor, mine);

                            for (var i = from; i <= to; i++)
                            {
                                var one = showing[i].Key ?? string.Empty;

                                if (wanted) ticked.Add(one); else ticked.Remove(one);
                            }
                        }
                        else
                        {
                            if (!ticked.Remove(here)) ticked.Add(here);

                            anchor = mine;
                        }

                        Paint();
                    });

                    list.Children.Add(row);
                }

                note.Text = matching.Count == 0
                    ? "Nothing matches."
                    : matching.Count > Most
                        ? Ui.Count(matching.Count, "match") + ", the first "
                          + Skin.Num(Most) + " shown. Selecting all takes every match."
                        : Ui.Count(matching.Count, "match") + ".";

                Say();
            }

            // ---- selecting in bulk, which is what the list is for -------------------------
            master.Click += (s, e) => Ui.Safely(() =>
            {
                var on = matching.Count(v => ticked.Contains(v.Key ?? string.Empty));
                var wanted = on < matching.Count;

                changed = true;

                foreach (var one in matching)
                {
                    var key = one.Key ?? string.Empty;

                    if (wanted) ticked.Add(key); else ticked.Remove(key);
                }

                Paint();
            });

            var verbs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8),
            };

            void Verb(string said, Action did)
            {
                var one = Ui.Quiet(said, () => Ui.Safely(did));

                one.Margin = new Thickness(0, 0, 14, 0);
                verbs.Children.Add(one);
            }

            Verb("Select all", () =>
            {
                changed = true;

                foreach (var one in matching) ticked.Add(one.Key ?? string.Empty);

                Paint();
            });

            Verb("Select none", () =>
            {
                changed = true;

                foreach (var one in matching) ticked.Remove(one.Key ?? string.Empty);

                Paint();
            });

            Verb("Invert", () =>
            {
                changed = true;

                foreach (var one in matching)
                {
                    var key = one.Key ?? string.Empty;

                    if (!ticked.Remove(key)) ticked.Add(key);
                }

                Paint();
            });

            if (any != null)
                Verb(any, () =>
                {
                    ticked.Clear();
                    changed = true;
                    Fill();
                });

            DockPanel.SetDock(verbs, Dock.Top);
            body.Children.Add(verbs);

            var done = Ui.Primary("Done", () => window.Close());

            done.MinWidth = 110;
            done.HorizontalAlignment = HorizontalAlignment.Right;
            done.Margin = new Thickness(0, 10, 0, 0);

            DockPanel.SetDock(done, Dock.Bottom);
            body.Children.Add(done);

            DockPanel.SetDock(chose, Dock.Bottom);
            body.Children.Add(chose);

            note.Margin = new Thickness(0, 8, 0, 0);
            DockPanel.SetDock(note, Dock.Bottom);
            body.Children.Add(note);

            box.TextChanged += (s, e) => Ui.Safely(Fill);

            box.KeyDown += (s, e) => Ui.Safely(() =>
            {
                if (e.Key != Key.Enter) return;

                // Enter after typing takes everything the filter matched, which is the fast path
                // this list exists for: type "WSC", press Enter, and the twelve are selected.
                changed = true;

                foreach (var one in matching) ticked.Add(one.Key ?? string.Empty);

                Paint();
            });

            Fill();

            body.Children.Add(new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var ground = new Border { Child = body };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            // A modal window of our own, so the box takes the keys. A popup inside the host never
            // gets keyboard focus, which is the whole reason this is a window.
            window.Loaded += (s, e) => box.Focus();

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();

            if (changed) onDone?.Invoke(ticked);
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
        /// A band across the table saying how the view is arranged, or where the loose results
        /// start.
        ///
        /// Deliberately not shaped like a group row — no tick, no chevron, no status colour —
        /// because it is not an object in the model. A band that looked like a group row would
        /// imply that arranging the view creates groups, which this screen exists to keep distinct.
        /// </summary>
        /// <param name="glyph">The symbol at the left. One of <see cref="Glyphs"/>.</param>
        /// <param name="label">The eyebrow: what the band is banding by.</param>
        /// <param name="value">The value this band holds.</param>
        /// <param name="count">How many rows are under it.</param>
        /// <param name="onPickAll">Ticks everything under the band, or null for no such link.</param>
        /// <param name="depth">How many bands are above this one, which is what indents it.</param>
        /// <param name="shut">True when this band is folded away, or null when it cannot fold.</param>
        /// <param name="onFold">Folds it, or unfolds it.</param>
        /// <param name="onFoldAll">Folds or opens every band at once, or null.</param>
        public static UIElement ViewBand(string glyph, string label, string value, string count,
                                         Action? onPickAll, int depth = 0, bool? shut = null,
                                         Action? onFold = null, Action? onFoldAll = null)
        {
            var row = new DockPanel { LastChildFill = true };

            // The two links live on a strip that appears under the pointer.
            //
            // They were on every band all the time, which on a chain three deep is two links per
            // band down the whole table saying the same two things, competing with the values
            // they sit beside. The rows already work this way - their verbs arrive on hover and
            // leave with the pointer - and a band is a row.
            var links = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden,
            };

            if (onFoldAll != null)
            {
                var every = Ui.Link(shut == true ? "Open them all" : "Fold them all",
                                    onFoldAll);

                every.VerticalAlignment = VerticalAlignment.Center;
                every.Margin = new Thickness(0, 0, 12, 0);

                links.Children.Add(every);
            }

            if (onPickAll != null)
            {
                var all = Ui.Link("Pick all of them", onPickAll);
                all.VerticalAlignment = VerticalAlignment.Center;
                links.Children.Add(all);
            }

            if (links.Children.Count > 0)
            {
                DockPanel.SetDock(links, Dock.Right);
                row.Children.Add(links);
            }

            var words = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // The chevron first, so the eye reads the fold state before anything else - and only
            // where there is something to fold.
            if (shut.HasValue)
                words.Children.Add(Ui.Glyph(shut.Value ? Glyphs.Right : Glyphs.Down, 12, "Muted",
                                            new Thickness(0, 0, 6, 0)));

            words.Children.Add(Ui.Glyph(glyph, 12, "Faint", new Thickness(0, 0, 8, 0)));

            var eyebrow = Ui.Eyebrow(label);
            eyebrow.VerticalAlignment = VerticalAlignment.Center;
            words.Children.Add(eyebrow);

            var what = new TextBlock
            {
                Text = value ?? string.Empty,
                FontWeight = depth == 0 ? FontWeights.SemiBold : FontWeights.Medium,
                FontSize = depth == 0 ? 13 : 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
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

            // Indented by its level rather than nested inside anything: the table is one flat,
            // virtualised list of lines, and a band that contained its rows would be a panel the
            // list could not recycle.
            //
            // Depth is shown by weight as well as indent. Indent alone read as three identical grey
            // bars at slightly different margins, so on a chain three deep the nesting was not
            // legible. Each level now recedes: the outermost is a full-weight bar with a rule above
            // it, and every level under it is lighter, smaller and unruled, with a rail down its
            // left edge marking where its parent's ground ends.
            //
            // One element per band. This previously wrapped every nested band in a second border
            // carrying the rail, inside a Grid — three elements and a stack of padding per band,
            // which on a chain three deep left a wide empty gutter down the side of the table. The
            // indent, the weight and the band's own left edge carry the depth at one element.
            //
            // The step per level is small: twenty pixels a level pushed a third-level band a third
            // of the way across the table before its text began.
            var band = new Border
            {
                Padding = new Thickness(12 + (depth * 14), depth == 0 ? 6 : 3, 12,
                                        depth == 0 ? 6 : 3),
                BorderThickness = new Thickness(depth == 0 ? 0 : 2, depth == 0 ? 1 : 0, 0, 1),
                Child = row,
                Cursor = onFold == null ? null : Cursors.Hand,
                Background = Brushes.Transparent,
            };

            band.Tint(Border.BackgroundProperty, depth == 0 ? "Bg" : depth == 1 ? "Card2" : "Pane");
            band.Tint(Border.BorderBrushProperty, "Bd");

            if (onFold != null)
                band.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    Ui.Safely(onFold);
                };

            if (links.Children.Count > 0)
            {
                band.MouseEnter += (s, e) => links.Visibility = Visibility.Visible;
                band.MouseLeave += (s, e) => links.Visibility = Visibility.Hidden;
            }

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
        /// Hang a menu off anything, opened by whatever the caller decides opens it.
        ///
        /// <see cref="MoreMenu"/> and <see cref="MenuButton"/> both make a button and put a menu
        /// on it. A grid's row headers and its cells are not buttons - they are the grid - and
        /// giving each of four hundred of them a button chrome would be four hundred boxes
        /// competing with the symbols they hold. So this attaches the same menu to an element
        /// that already exists, built when it is opened rather than up front, because a cell's
        /// menu depends on what is in the cell right now.
        /// </summary>
        /// <param name="on">What the menu hangs off.</param>
        /// <param name="items">The menu, as it stands.</param>
        public static void PopMenu(FrameworkElement on, IReadOnlyList<Entry> items)
        {
            if (on == null || items == null || items.Count == 0) return;

            var popup = new Popup
            {
                PlacementTarget = on,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                HorizontalOffset = -8,
                VerticalOffset = -8,
                Child = Floating(Menu(items, false), 230),
            };

            // Parented into the same window as everything else on screen, so the skin reaches it
            // and it closes when the pane it belongs to goes away.
            popup.IsOpen = true;
        }

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

            // The theme sheet is merged onto the card itself. A popup's child is not in the
            // window's logical tree — the popup is parented to nothing and only pointed at a
            // placement target — so every SetResourceReference inside it walks up, finds no
            // dictionary and resolves to nothing. The card then paints transparent and the menu
            // reads as bare text over whatever is behind it, which is how the assign list and the
            // filter menu were reported. Merging the sheet here gives everything inside the card a
            // dictionary to resolve against.
            card.Resources.MergedDictionaries.Add(Skin.Sheet);

            card.Tint(Border.BackgroundProperty, "Card");
            card.Tint(Border.BorderBrushProperty, "Bd");

            return card;
        }

        /// <summary>
        /// Close the menu an element sits in, if it sits in one.
        ///
        /// Walks out to the <see cref="Popup"/> rather than being handed it, so an option does not
        /// have to be built knowing which menu it will end up in. That requirement is what led the
        /// card to attempt this on the option's behalf, twice, and get it wrong both times.
        /// </summary>
        /// <param name="from">The option that was chosen.</param>
        private static void Shut(DependencyObject? from)
        {
            for (var node = from; node != null; )
            {
                if (node is Popup popup)
                {
                    popup.IsOpen = false;
                    return;
                }

                node = LogicalTreeHelper.GetParent(node)
                       ?? (node is Visual ? VisualTreeHelper.GetParent(node) : null);
            }
        }

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

            // Nothing here closes the menu. The options do, in Line, once they have acted.
            //
            // Two ways of doing it from the card have now been wrong. PreviewMouseLeftButtonUp
            // tunnels: the card saw the press before the option did, and closing a Popup tears its
            // child out of the visual tree, so the option's own handler was never raised and every
            // menu in the product opened and did nothing. Bubbling fixed that and introduced the
            // opposite fault - *any* release inside the card shuts the menu, including a click
            // into a text box in one.
            //
            // An option knows when it has been chosen. Nothing else in a menu should decide the
            // menu is finished.

            if (target is ButtonBase press)
            {
                press.Click += (s, e) => popup.IsOpen = !popup.IsOpen;
            }
            else
            {
                target.MouseLeftButtonUp += (s, e) =>
                {
                    popup.IsOpen = !popup.IsOpen;

                    // Marked, so opening a chip's menu is not also a click on the row underneath
                    // it. A board row selects on click now, and a press meant for the status
                    // dropdown must not move the selection out from under the person pressing it.
                    e.Handled = true;
                };
            }
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
