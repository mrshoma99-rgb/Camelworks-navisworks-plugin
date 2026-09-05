using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Selects which clash tests become a matrix, and how their names are structured.
    ///
    /// Previously the read used every test in the document and named each row after the saved
    /// set it points at. Set names are usually not the names a coordination matrix should carry,
    /// and the discipline codes, numbers, folders and test kinds already present in the test
    /// names were discarded.
    ///
    /// This window provides:
    ///
    /// - a test table with a search box, checkboxes and bulk selection;
    /// - name patterns written in <see cref="MatrixNaming.Tokens"/>, applied in reverse;
    /// - a live indication of which pattern matched each test.
    ///
    /// Patterns are evaluated in order and the first match wins, so specific patterns belong
    /// above general ones.
    /// </summary>
    public static class MatrixReadWindow
    {
        /// <summary>One test as the table lists it.</summary>
        private sealed class Row
        {
            internal Row(ClashTestRead read) { Read = read; }

            internal ClashTestRead Read { get; }

            internal string Name => Read.Test.Name;

            internal bool Chosen { get; set; } = true;

            /// <summary>Index of the matching pattern, from 1; 0 when none matched.</summary>
            internal int By { get; set; }

            internal string Between =>
                Read.Shape == null || !Read.Shape.KnowsA || !Read.Shape.KnowsB
                    ? "not stated"
                    : Side(Read.Shape.SetA, Read.Shape.ScopesA) + "  vs  "
                      + Side(Read.Shape.SetB, Read.Shape.ScopesB);

            private static string Side(string? set, IReadOnlyList<string> scopes) =>
                !string.IsNullOrWhiteSpace(set)
                    ? set!.Trim()
                    : scopes != null && scopes.Count > 0
                        ? (scopes.Count == 1 ? scopes[0] : Ui.Count(scopes.Count, "model"))
                        : "nothing";
        }

        /// <summary>Show the window and return the matrix that was read.</summary>
        /// <param name="tests">Every test the document holds, already read.</param>
        /// <param name="pattern">The pattern the matrix writes names with, as a starting point.</param>
        /// <returns>The matrix that was read, or null when the window was closed without one.</returns>
        public static MatrixReading? Ask(IReadOnlyList<ClashTestRead> tests, string? pattern)
        {
            if (tests == null || tests.Count == 0) return null;

            MatrixReading? done = null;

            var window = new Window
            {
                Title = "CamelWorks - Read tests into a matrix",
                Width = 980,
                Height = 660,
                MinWidth = 780,
                MinHeight = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var ground = new Border
            {
                Child = Draw(tests, pattern,
                             reading => { done = reading; window.Close(); },
                             () => window.Close()),
            };

            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            Skin.Apply(window);
            HostWindow.Own(window);

            window.ShowDialog();

            return done;
        }

        /// <summary>
        /// Builds the window content as a panel.
        ///
        /// Separate from <see cref="Ask"/> so the render harness can capture the layout; a WPF
        /// <c>Window</c> must be shown to render, and the harness has no display.
        /// </summary>
        /// <param name="tests">Every test the document holds, already read.</param>
        /// <param name="pattern">The pattern the matrix writes names with, as a starting point.</param>
        /// <param name="onDone">Called with the matrix that was read.</param>
        /// <param name="onCancel">Called when the person leaves without one.</param>
        public static UIElement Draw(IReadOnlyList<ClashTestRead> tests, string? pattern,
                                       Action<MatrixReading>? onDone, Action? onCancel)
        {
            var rows = (tests ?? Array.Empty<ClashTestRead>())
                .Where(one => one != null)
                .Select(one => new Row(one))
                .ToList();

            // Seeded with the pattern the matrix uses to generate names, since that is the
            // convention the project has already declared.
            var written = new List<TextBox>();
            var patterns = new List<string>
            {
                string.IsNullOrWhiteSpace(pattern) ? MatrixNaming.Default : pattern!,
            };

            var list = Ui.Stack();
            var table = Ui.Stack();
            var strip = Ui.Stack();
            var trouble = Ui.Stack();

            var find = Ui.Text(string.Empty, "part of a name", null, 220);
            var take = Ui.Check("Use saved sets for unmatched tests", true, null);
            var names = Ui.Check("Prefer parsed names over set names", true, null);

            TextBox? focused = null;

            // Re-evaluated on every redraw so a partially typed pattern shows its current match
            // set as it is edited.
            List<MatrixNamePattern> Read()
            {
                var read = patterns.Select(MatrixNamePattern.Parse).ToList();

                foreach (var row in rows)
                {
                    row.By = 0;

                    for (var at = 0; at < read.Count; at++)
                    {
                        if (!read[at].IsUsable) continue;
                        if (read[at].Match(row.Name) == null) continue;

                        row.By = at + 1;
                        break;
                    }
                }

                return read;
            }

            void Redraw()
            {
                var read = Read();

                DrawPatterns(list, patterns, read, rows, written, box => focused = box,
                             () => { patterns.Add(string.Empty); Redraw(); }, Redraw);
                DrawTable(table, rows, find.Text, Redraw);
                DrawStrip(strip, rows, read);
            }

            find.TextChanged += (s, e) => Ui.Safely(Redraw);

            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

            foreach (var token in MatrixNaming.Tokens)
            {
                var one = token;

                chips.Children.Add(Chip(one, () =>
                {
                    var box = focused ?? written.FirstOrDefault();

                    if (box == null) return;

                    var at = Math.Max(0, Math.Min(box.Text.Length, box.SelectionStart));

                    box.Text = box.Text.Substring(0, at) + one.Token + box.Text.Substring(at);
                    box.SelectionStart = at + one.Token.Length;

                    box.Focus();
                }));
            }

            var side = new DockPanel { LastChildFill = true, Width = 420 };

            var under = Ui.Stack(
                Ui.Eyebrow("Insert a token"),
                chips,
                take,
                names);

            under.Margin = new Thickness(0, 8, 0, 0);

            DockPanel.SetDock(under, Dock.Bottom);
            side.Children.Add(under);

            var says = Ui.Small("Patterns are matched in order; the first match is used. Put "
                                + "specific patterns above general ones.");

            says.TextWrapping = TextWrapping.Wrap;
            says.Margin = new Thickness(0, 2, 0, 8);

            var top = Ui.Stack(Ui.Eyebrow("Test name patterns"), says);

            DockPanel.SetDock(top, Dock.Top);
            side.Children.Add(top);

            side.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(list),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var middle = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 16, 0) };

            var searching = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };

            var acts = Ui.Across(
                Ui.Quiet("Select shown", () => { Sweep(rows, find.Text, true); Redraw(); }),
                Ui.Quiet("Clear shown", () => { Sweep(rows, find.Text, false); Redraw(); }),
                Ui.Quiet("Invert", () =>
                {
                    foreach (var row in Shown(rows, find.Text)) row.Chosen = !row.Chosen;
                    Redraw();
                }));

            DockPanel.SetDock(acts, Dock.Right);
            searching.Children.Add(acts);
            searching.Children.Add(Ui.Field("Find", find, null));

            DockPanel.SetDock(searching, Dock.Top);
            middle.Children.Add(searching);

            DockPanel.SetDock(strip, Dock.Bottom);
            middle.Children.Add(Ui.Loose(strip));

            middle.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(table),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var body = new DockPanel { LastChildFill = true };

            DockPanel.SetDock(side, Dock.Right);
            body.Children.Add(side);
            body.Children.Add(middle);

            var go = Ui.Primary("Read tests", () =>
            {
                trouble.Children.Clear();

                var chosen = rows.Where(r => r.Chosen).Select(r => r.Read).ToList();

                if (chosen.Count == 0)
                {
                    trouble.Children.Add(Ui.Problem("Select at least one test."));
                    return;
                }

                var reading = MatrixFromNames.Read(chosen,
                    patterns.Select(MatrixNamePattern.Parse).ToList(),
                    new MatrixReadOptions
                    {
                        TakeUnmatched = take.IsChecked == true,
                        NamesWin = names.IsChecked == true,
                    });

                if (!reading.IsUsable)
                {
                    trouble.Children.Add(Ui.Problem(reading.Explain()));
                    return;
                }

                onDone?.Invoke(reading);
            });

            go.MinWidth = 110;

            var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

            var foot = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Child = Ui.Actions(go, Ui.Quiet("Cancel", () => onCancel?.Invoke())),
            };

            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            DockPanel.SetDock(trouble, Dock.Bottom);
            root.Children.Add(Ui.Loose(trouble));

            root.Children.Add(body);

            Redraw();

            return root;
        }

        /// <summary>The tests matching the current search text.</summary>
        private static IEnumerable<Row> Shown(IEnumerable<Row> rows, string? find)
        {
            var said = (find ?? string.Empty).Trim();

            return said.Length == 0
                ? rows
                : rows.Where(r => r.Name.IndexOf(said, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void Sweep(IEnumerable<Row> rows, string? find, bool chosen)
        {
            foreach (var row in Shown(rows, find)) row.Chosen = chosen;
        }

        /// <summary>
        /// Draws the pattern cards, each showing its match count and the values extracted from
        /// the first name it matched.
        /// </summary>
        private static void DrawPatterns(Panel into, List<string> patterns,
                                         IReadOnlyList<MatrixNamePattern> read,
                                         IReadOnlyList<Row> rows, List<TextBox> written,
                                         Action<TextBox> onFocus, Action onAdd, Action redraw)
        {
            into.Children.Clear();
            written.Clear();

            for (var at = 0; at < patterns.Count; at++)
            {
                var here = at;
                var one = read[at];

                var box = Ui.Text(patterns[at], "{row} vs {column}",
                                  typed => { patterns[here] = typed; redraw(); }, 240);

                box.HorizontalAlignment = HorizontalAlignment.Stretch;

                // Wrapped: a full pattern is wider than the field and scrolled out of view on a
                // single line.
                box.TextWrapping = TextWrapping.Wrap;
                box.AcceptsReturn = false;
                box.MinHeight = 46;

                box.GotKeyboardFocus += (s, e) => onFocus(box);

                written.Add(box);

                var card = new Border
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(10, 9, 10, 10),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                };

                card.Tint(Border.BackgroundProperty, "Card2");
                card.Tint(Border.BorderBrushProperty, "Bd");

                var stack = Ui.Stack();

                var head = new DockPanel { LastChildFill = true,
                                           Margin = new Thickness(0, 0, 0, 6) };

                var moves = Ui.Across(
                    Ui.Quiet("↑", () =>
                    {
                        if (here == 0) return;

                        var held = patterns[here];

                        patterns.RemoveAt(here);
                        patterns.Insert(here - 1, held);
                        redraw();
                    }),
                    Ui.Quiet("↓", () =>
                    {
                        if (here >= patterns.Count - 1) return;

                        var held = patterns[here];

                        patterns.RemoveAt(here);
                        patterns.Insert(here + 1, held);
                        redraw();
                    }),
                    Ui.Quiet("✕", () =>
                    {
                        if (patterns.Count == 1) return;

                        patterns.RemoveAt(here);
                        redraw();
                    }));

                DockPanel.SetDock(moves, Dock.Right);
                head.Children.Add(moves);

                head.Children.Add(new TextBlock
                {
                    Text = "Pattern " + (here + 1).ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body"));

                stack.Children.Add(head);
                stack.Children.Add(box);

                if (!one.IsUsable)
                {
                    var why = Ui.Small(one.Trouble ?? "This pattern cannot be parsed.");

                    why.TextWrapping = TextWrapping.Wrap;
                    why.Margin = new Thickness(0, 5, 0, 0);
                    why.Tint(TextBlock.ForegroundProperty, "RedTx");

                    stack.Children.Add(why);
                }
                else
                {
                    var mine = rows.Where(r => r.By == here + 1).ToList();

                    var count = Ui.Small(mine.Count == 0
                        ? "No matches"
                        : "Matches " + Ui.Count(mine.Count, "test"));

                    count.Margin = new Thickness(0, 5, 0, 0);
                    stack.Children.Add(count);

                    // Extracted values from a real name: a pattern can match and still assign
                    // the wrong parts.
                    var first = mine.FirstOrDefault();
                    var caught = first == null ? null : one.Match(first.Name);

                    if (caught != null && caught.Held.Count > 0)
                    {
                        var says = Ui.Small(string.Join("  ·  ",
                            caught.Held.Where(h => h.Value.Length > 0)
                                       .Select(h => h.Key + " = " + h.Value)));

                        says.TextWrapping = TextWrapping.Wrap;
                        says.Margin = new Thickness(0, 3, 0, 0);
                        says.Tint(TextBlock.ForegroundProperty, "Muted");

                        stack.Children.Add(says);
                    }
                }

                card.Child = stack;
                into.Children.Add(card);
            }

            var adding = Ui.Quiet("Add a pattern", onAdd);

            adding.HorizontalAlignment = HorizontalAlignment.Left;

            into.Children.Add(adding);
        }

        /// <summary>
        /// Draws the test table. The whole row toggles the checkbox, not just the box itself.
        /// </summary>
        private static void DrawTable(Panel into, IReadOnlyList<Row> rows, string? find,
                                      Action redraw)
        {
            into.Children.Clear();

            var head = Line(null, "Test", "Between", "Pattern", heading: true);

            into.Children.Add(head);

            var shown = Shown(rows, find).ToList();

            if (shown.Count == 0)
            {
                var none = Ui.Small("No test name contains that text.");

                none.Margin = new Thickness(4, 8, 4, 4);
                into.Children.Add(none);
                return;
            }

            foreach (var row in shown)
            {
                var one = row;

                var line = Line(one.Chosen, one.Name, one.Between,
                                one.By == 0 ? "—"
                                    : (one.By).ToString(CultureInfo.InvariantCulture),
                                heading: false);

                line.Cursor = Cursors.Hand;
                line.Background = System.Windows.Media.Brushes.Transparent;

                line.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
                {
                    e.Handled = true;

                    // Shift-click applies the toggle across the range from the last click.
                    if (Keyboard.Modifiers == ModifierKeys.Shift && _anchor != null
                        && rows.Contains(_anchor))
                    {
                        var from = rows.ToList().IndexOf(_anchor);
                        var to = rows.ToList().IndexOf(one);

                        if (from >= 0 && to >= 0)
                        {
                            var wanted = !one.Chosen;

                            for (var at = Math.Min(from, to); at <= Math.Max(from, to); at++)
                                rows[at].Chosen = wanted;

                            redraw();
                            return;
                        }
                    }

                    one.Chosen = !one.Chosen;
                    _anchor = one;

                    redraw();
                });

                into.Children.Add(line);
            }
        }

        /// <summary>Anchor row for shift-click range selection.</summary>
        private static Row? _anchor;

        private static Border Line(bool? ticked, string name, string between, string by,
                                   bool heading)
        {
            var grid = new Grid();

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

            void Cell(int column, string text, bool bold, string key)
            {
                var said = new TextBlock
                {
                    Text = text,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                }.Wearing(key);

                Grid.SetColumn(said, column);
                grid.Children.Add(said);
            }

            if (ticked != null)
            {
                var mark = new TextBlock
                {
                    Text = ticked == true ? "✓" : string.Empty,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body");

                mark.Tint(TextBlock.ForegroundProperty, ticked == true ? "Accent" : "Faint");

                var box = new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = mark,
                };

                box.Tint(Border.BorderBrushProperty, ticked == true ? "Accent" : "FieldBd");

                Grid.SetColumn(box, 0);
                grid.Children.Add(box);
            }

            Cell(1, name, heading, heading ? "Caps" : "Body");
            Cell(2, between, false, heading ? "Caps" : "Hint");
            Cell(3, by, false, heading ? "Caps" : "Hint");

            var line = new Border
            {
                Padding = new Thickness(4, 5, 4, 5),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };

            line.Tint(Border.BorderBrushProperty, "Bd");

            return line;
        }

        /// <summary>Draws the count line under the table.</summary>
        private static void DrawStrip(Panel into, IReadOnlyList<Row> rows,
                                      IReadOnlyList<MatrixNamePattern> read)
        {
            into.Children.Clear();

            var chosen = rows.Count(r => r.Chosen);
            var claimed = rows.Count(r => r.Chosen && r.By > 0);

            var said = Ui.Small(chosen.ToString(CultureInfo.InvariantCulture) + " of "
                                + rows.Count.ToString(CultureInfo.InvariantCulture)
                                + " tests selected  ·  "
                                + claimed.ToString(CultureInfo.InvariantCulture)
                                + " matched by a pattern");

            said.Margin = new Thickness(4, 8, 4, 0);

            into.Children.Add(said);
        }

        /// <summary>Draws one token as a clickable chip.</summary>
        private static UIElement Chip(NameToken token, Action onPress)
        {
            var chip = new Border
            {
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(7, 3, 7, 3),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = token.Why,
                Child = new TextBlock { Text = token.Title, FontSize = 11 }.Wearing("Body"),
            };

            chip.Tint(Border.BackgroundProperty, "Card");
            chip.Tint(Border.BorderBrushProperty, "FieldBd");

            chip.MouseEnter += (s, e) => chip.Tint(Border.BorderBrushProperty, "Accent");
            chip.MouseLeave += (s, e) => chip.Tint(Border.BorderBrushProperty, "FieldBd");

            chip.MouseLeftButtonUp += (s, e) => Ui.Safely(onPress);

            return chip;
        }
    }
}
