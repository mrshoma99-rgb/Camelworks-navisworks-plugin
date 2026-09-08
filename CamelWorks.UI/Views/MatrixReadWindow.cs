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
    /// - name patterns written in <see cref="MatrixNaming.Tokens"/>;
    /// - a per-test assignment saying which pattern reads it.
    ///
    /// The assignment is the user's, not the matcher's. Each pattern is still run over every
    /// name and the result is shown as a proposal, because assigning a hundred and sixty tests
    /// by hand is the work this window exists to remove. But the proposal is only a starting
    /// value: any test can be moved to any pattern, or off the patterns entirely, and that
    /// choice wins. Previously the first pattern whose regex matched claimed the test, so where
    /// two patterns both fitted, the order of the list decided — silently, and with nothing on
    /// screen to say so or to change it.
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

            /// <summary>Index of the pattern whose regex fits this name, from 1; 0 when none does.</summary>
            internal int By { get; set; }

            /// <summary>
            /// The pattern the user put this test on, from 1, or 0 for none of them. Null while
            /// the test is still on the proposal.
            /// </summary>
            internal int? Put { get; set; }

            /// <summary>The pattern that will actually read this test.</summary>
            internal int Using => Put ?? By;

            /// <summary>The host's identity for the test, which the assignment is keyed on.</summary>
            internal string Id => Read.Test.Id;

            internal bool Stated =>
                Read.Shape != null && Read.Shape.KnowsA && Read.Shape.KnowsB;

            /// <summary>The first side, or the whole answer when there is not one.</summary>
            internal string SideA =>
                Stated ? Side(Read.Shape!.SetA, Read.Shape.ScopesA) : "not stated";

            /// <summary>The second side, or empty when there is not one.</summary>
            internal string SideB =>
                Stated ? Side(Read.Shape!.SetB, Read.Shape.ScopesB) : string.Empty;

            internal string Between => Stated ? SideA + "  vs  " + SideB : "not stated";

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

                // Taller than the pane it replaced: a row carries two lines now that both sides
                // of a test are printed, so the same table needs more of the screen to show the
                // same number of tests.
                Height = 720,
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
            var bulk = Ui.Stack();
            var strip = Ui.Stack();
            var trouble = Ui.Stack();

            var find = Ui.Find("part of a name", null, Ui.Wide.Phrase);
            var take = Ui.Check("Use saved sets for unmatched tests", true, null);
            var names = Ui.Check("Prefer parsed names over set names", true, null);

            TextBox? focused = null;

            // Re-evaluated on every redraw so a partially typed pattern shows its current match
            // set as it is edited.
            List<MatrixNamePattern> Read()
            {
                var read = patterns.Select(one => MatrixNamePattern.Parse(one)).ToList();

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

                DrawPatterns(list, patterns, read, rows, written,
                             held => { focused = held; Redraw(); }, focused,
                             () => { patterns.Add(string.Empty); Redraw(); }, Redraw, Palette);
                DrawBulk(bulk, rows, find.Text, patterns.Count, Put);
                DrawTable(table, rows, find.Text, patterns.Count, Redraw, Put);
                DrawStrip(strip, rows);
            }

            // One place that writes an assignment, so the table cell, the bulk bar and any later
            // caller cannot drift on what null and zero mean.
            void Put(Row row, int? onto)
            {
                row.Put = onto;
                Redraw();
            }

            find.TextChanged += (s, e) => Ui.Safely(Redraw);

            var side = new DockPanel { LastChildFill = true, Width = 400,
                                       Margin = new Thickness(4, 0, 0, 0) };

            var says = Ui.Small("Each pattern is run over every test name; what it reads is shown "
                                + "against the test, and can be changed there.");

            says.TextWrapping = TextWrapping.Wrap;
            says.Margin = new Thickness(0, 2, 0, 8);

            // Sentence case, because Ui.Eyebrow upper-cases what it is handed and that fix
            // belongs in the shared file.
            var top = Ui.Stack(
                new TextBlock { Text = "Test name patterns" }.Wearing("Caps"), says);

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

            // Docked rather than filling, so the box stays the width of what goes in it.
            var box = Ui.Field("Find", find, null);

            box.Width = 260;
            box.HorizontalAlignment = HorizontalAlignment.Left;

            DockPanel.SetDock(box, Dock.Left);
            searching.Children.Add(box);
            searching.Children.Add(new Border());

            DockPanel.SetDock(searching, Dock.Top);
            middle.Children.Add(searching);

            // The bar that assigns the ticked tests to one pattern in a single act, which is the
            // only thing that makes assignment usable on a job with a hundred and sixty tests.
            DockPanel.SetDock(bulk, Dock.Top);
            middle.Children.Add(Ui.Loose(bulk));

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

                var how = new MatrixReadOptions
                {
                    TakeUnmatched = take.IsChecked == true,
                    NamesWin = names.IsChecked == true,
                };

                foreach (var row in rows.Where(r => r.Chosen && r.Put != null
                                                    && !string.IsNullOrEmpty(r.Id)))
                    how.Assigned[row.Id] = row.Put!.Value;

                var reading = MatrixFromNames.Read(chosen,
                    patterns.Select(one => MatrixNamePattern.Parse(one)).ToList(), how);

                if (!reading.IsUsable)
                {
                    trouble.Children.Add(Ui.Problem(reading.Explain()));
                    return;
                }

                onDone?.Invoke(reading);
            });

            go.MinWidth = 110;

            var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

            // The screen had no title, so nothing on it said what it was. One line of orientation
            // under it; what a pattern is belongs on the pattern column, which is where it is
            // written.
            var titled = Ui.Stack(
                Ui.Heading("Read tests into a matrix"),
                Ui.Sub("Every clash test in the document, and the row and column each one is "
                       + "between."));

            titled.Margin = new Thickness(0, 0, 0, 10);

            DockPanel.SetDock(titled, Dock.Top);
            root.Children.Add(titled);

            // The two read options belong beside the button that acts on them. They were at the
            // foot of the token palette, which is about writing a pattern and not about what a
            // read does with the tests the patterns do not cover.
            // What a read does to work already on screen, beside the button that does it. It was
            // the last sentence of a four-line paragraph at the top of the other column.
            var warned = Ui.Small("Reading replaces the matrix on screen. The matrix in the "
                                  + "project file changes only when this one is saved.");

            warned.TextWrapping = TextWrapping.Wrap;
            warned.MaxWidth = 420;
            warned.Margin = new Thickness(0, 4, 0, 0);

            var choices = Ui.Stack(Ui.Across(take, names), warned);

            choices.VerticalAlignment = VerticalAlignment.Center;

            var footing = new DockPanel { LastChildFill = false };

            var doing = Ui.Actions(go, Ui.Quiet("Cancel", () => onCancel?.Invoke()));

            DockPanel.SetDock(doing, Dock.Right);
            footing.Children.Add(doing);

            DockPanel.SetDock(choices, Dock.Left);
            footing.Children.Add(choices);

            var foot = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Child = footing,
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
                                         Action<TextBox> onFocus, TextBox? focused,
                                         Action onAdd, Action redraw,
                                         Func<TextBox, UIElement> palette)
        {
            into.Children.Clear();

            // The boxes outlive a redraw. Editing one raises on every keystroke and the redraw
            // that follows rebuilds the cards, so a box made here was replaced by a new one after
            // every character typed into it: the cards could not be edited at all. One box per
            // pattern is made once and moves with it.
            while (written.Count < patterns.Count)
            {
                var made = Ui.Text(patterns[written.Count], "{row} vs {column}", null, Ui.Wide.Phrase);

                made.HorizontalAlignment = HorizontalAlignment.Stretch;

                // Wrapped: a full pattern is wider than the field and scrolled out of view on a
                // single line.
                made.TextWrapping = TextWrapping.Wrap;
                made.AcceptsReturn = false;
                made.MinHeight = 46;

                // By where the box is now, not by where it was when it was made: a pattern moved
                // up or down carries its box with it.
                made.TextChanged += (s, e) => Ui.Safely(() =>
                {
                    var mine = written.IndexOf(made);

                    if (mine < 0 || mine >= patterns.Count) return;
                    if (string.Equals(patterns[mine], made.Text, StringComparison.Ordinal)) return;

                    patterns[mine] = made.Text;
                    redraw();
                });

                made.GotKeyboardFocus += (s, e) => Ui.Safely(() =>
                {
                    if (!ReferenceEquals(focused, made)) onFocus(made);
                });

                written.Add(made);
            }

            while (written.Count > patterns.Count) written.RemoveAt(written.Count - 1);

            for (var at = 0; at < patterns.Count; at++)
            {
                var here = at;
                var one = read[at];
                var box = written[at];

                // The text can change under the box — a pattern removed shifts every one below
                // it up — and setting it while somebody is typing would fight the caret.
                if (!box.IsKeyboardFocusWithin
                    && !string.Equals(box.Text, patterns[at], StringComparison.Ordinal))
                    box.Text = patterns[at];

                // The card the caret is in, or the first card before anything has been touched.
                var live = focused == null ? here == 0 : ReferenceEquals(focused, box);

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

                // The box moves with its pattern, so a card carries its own text and its own
                // caret wherever it is put.
                void Move(int to)
                {
                    var held = patterns[here];
                    var carried = written[here];

                    patterns.RemoveAt(here);
                    written.RemoveAt(here);

                    patterns.Insert(to, held);
                    written.Insert(to, carried);

                    redraw();
                }

                var moves = Ui.Across(
                    Ui.Quiet("↑", () => { if (here > 0) Move(here - 1); }),
                    Ui.Quiet("↓", () => { if (here < patterns.Count - 1) Move(here + 1); }),
                    Ui.Quiet("✕", () =>
                    {
                        if (patterns.Count == 1) return;

                        patterns.RemoveAt(here);
                        written.RemoveAt(here);
                        redraw();
                    }));

                DockPanel.SetDock(moves, Dock.Right);
                head.Children.Add(moves);

                // Opening a card is a press, not only a click into its box. The tokens showed
                // for whichever card had the caret and there was no way to bring them back
                // without finding the box again.
                var open = Ui.Quiet(live ? "▾ Tokens" : "▸ Tokens", () => onFocus(box));

                open.ToolTip = "Show the tokens that can be written into this pattern.";
                open.Margin = new Thickness(8, 0, 0, 0);

                DockPanel.SetDock(open, Dock.Right);
                head.Children.Add(open);

                head.Children.Add(new TextBlock
                {
                    Text = "Pattern " + (here + 1).ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Body"));

                stack.Children.Add(head);
                stack.Children.Add(Ui.Loose(box));

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
                    var mine = rows.Where(r => r.Using == here + 1).ToList();
                    var byHand = mine.Count(r => r.Put != null);

                    var count = Ui.Small(mine.Count == 0
                        ? "No tests"
                        : "Reads " + Ui.Count(mine.Count, "test")
                          + (byHand == 0 ? string.Empty
                                         : "  ·  " + byHand.ToString(CultureInfo.InvariantCulture)
                                           + " assigned by hand"));

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

                if (live)
                {
                    var tokens = palette(box);

                    tokens.Opacity = one.IsUsable || box.Text.Length == 0 ? 1 : 0.85;
                    stack.Children.Add(tokens);
                }

                card.Tint(Border.BorderBrushProperty, live ? "Accent" : "Bd");

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
                                      int patterns, Action redraw, Action<Row, int?> put)
        {
            into.Children.Clear();

            var head = Line(null, "Test", "Between", null, null, "Read by", heading: true);

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

                var line = Line(one.Chosen, one.Name, one.SideA, one.SideB,
                                Assigning(one, patterns, put), null, heading: false);

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

        private static Border Line(bool? ticked, string name, string between, string? alsoBetween,
                                   UIElement? by, string? byText, bool heading)
        {
            var grid = new Grid();

            // The test name is what a pattern is written against, so it gets the room. Before,
            // the two columns were 2:3 the other way and the name — the longer of the two, and
            // the one being matched — was the one cut off.
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });

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

                    // A trimmed name is still readable on hover rather than lost.
                    ToolTip = heading ? null : text,
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

            if (string.IsNullOrEmpty(alsoBetween))
            {
                Cell(2, between, false, heading ? "Caps" : "Hint");
            }
            else
            {
                // Two lines rather than one. A saved set on a real job is thirty characters, so
                // both sides and a separator do not fit a column this table can spare, and one
                // line trimmed every row mid-word — losing the second side, which is half of
                // what the column is for.
                var sides = Sides(between, alsoBetween!);

                Grid.SetColumn(sides, 2);
                grid.Children.Add(sides);
            }

            if (by != null)
            {
                Grid.SetColumn(by, 3);
                grid.Children.Add(by);
            }
            else if (byText != null)
            {
                Cell(3, byText, false, heading ? "Caps" : "Hint");
            }

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
        private static void DrawStrip(Panel into, IReadOnlyList<Row> rows)
        {
            into.Children.Clear();

            var chosen = rows.Count(r => r.Chosen);
            var claimed = rows.Count(r => r.Chosen && r.Using > 0);
            var byHand = rows.Count(r => r.Chosen && r.Put != null);

            var said = Ui.Small(chosen.ToString(CultureInfo.InvariantCulture) + " of "
                                + rows.Count.ToString(CultureInfo.InvariantCulture)
                                + " tests selected  ·  "
                                + claimed.ToString(CultureInfo.InvariantCulture)
                                + " read by a pattern"
                                + (byHand == 0
                                    ? string.Empty
                                    : "  ·  " + byHand.ToString(CultureInfo.InvariantCulture)
                                      + " assigned by hand"));

            said.Margin = new Thickness(4, 8, 4, 0);

            into.Children.Add(said);
        }

        /// <summary>What a test is between, stacked so neither side is trimmed away.</summary>
        /// <param name="a">The first side.</param>
        /// <param name="b">The second side.</param>
        private static UIElement Sides(string a, string b)
        {
            TextBlock One(string said, bool second)
            {
                var line = new TextBlock
                {
                    Text = second ? "vs  " + said : said,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(0, second ? 1 : 0, 8, 0),
                    ToolTip = said,
                }.Wearing("Hint");

                if (second) line.Tint(TextBlock.ForegroundProperty, "Muted");

                return line;
            }

            var held = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            held.Children.Add(One(a, false));
            held.Children.Add(One(b, true));

            return held;
        }

        /// <summary>
        /// The cell that says which pattern reads a test, and changes it.
        ///
        /// A control rather than a printed index. The column previously showed the number of the
        /// pattern that had matched, which named the outcome without offering any way to disagree
        /// with it — and on a board with three patterns a bare "2" says little.
        ///
        /// The wording separates the two states that matter. A test on the proposal reads
        /// "Pattern 2" quietly; one somebody has put there reads the same in the accent colour,
        /// so a column of assignments can be scanned for the ones that were decided.
        /// </summary>
        /// <param name="row">The test.</param>
        /// <param name="patterns">How many patterns there are.</param>
        /// <param name="put">Writes the assignment.</param>
        private static UIElement Assigning(Row row, int patterns, Action<Row, int?> put)
        {
            var mine = row.Put != null;
            var at = row.Using;

            var said = at == 0
                ? (mine ? "Saved sets" : "—")
                : "Pattern " + at.ToString(CultureInfo.InvariantCulture);

            var text = new TextBlock
            {
                Text = said,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            }.Wearing("Body");

            text.Tint(TextBlock.ForegroundProperty, mine ? "Accent" : "Muted");

            var chip = new Border
            {
                Padding = new Thickness(7, 2, 7, 2),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = text,
                ToolTip = mine
                    ? "Assigned. Click to change it."
                    : "Proposed from the pattern that fits this name. Click to change it.",
            };

            chip.Tint(Border.BackgroundProperty, mine ? "AccentWash" : "Card");
            chip.Tint(Border.BorderBrushProperty, mine ? "Accent" : "FieldBd");

            chip.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
            {
                // Stops the row's own handler taking this as a tick.
                e.Handled = true;

                Parts.PopMenu(chip, Choices(patterns, row.By,
                                            onto => put(row, onto),
                                            () => put(row, null),
                                            row.Put != null));
            });

            return chip;
        }

        /// <summary>
        /// The menu behind an assignment: every pattern, the saved sets, and the way back to the
        /// proposal.
        /// </summary>
        /// <param name="patterns">How many patterns there are.</param>
        /// <param name="proposed">The pattern the names fit, from 1, or 0 for none.</param>
        /// <param name="onPut">Called with the chosen pattern, or 0 for the saved sets.</param>
        /// <param name="onClear">Called to go back to the proposal.</param>
        /// <param name="assigned">Whether this is currently assigned by hand.</param>
        private static IReadOnlyList<Parts.Entry> Choices(int patterns, int proposed,
                                                          Action<int> onPut, Action onClear,
                                                          bool assigned)
        {
            var items = new List<Parts.Entry>();

            for (var at = 1; at <= patterns; at++)
            {
                var here = at;

                items.Add(Parts.Entry.Act(
                    "Pattern " + here.ToString(CultureInfo.InvariantCulture)
                    + (here == proposed ? "  (fits this name)" : string.Empty),
                    null, () => onPut(here)));
            }

            items.Add(Parts.Entry.Act("Saved sets, not a pattern", null, () => onPut(0)));

            if (assigned)
                items.Add(Parts.Entry.Act("Back to the proposal", null, onClear));

            return items;
        }

        /// <summary>
        /// The bar that puts every ticked test on one pattern at once.
        ///
        /// Assignment is only usable at the size of a real job if it can be done to a selection.
        /// Filtering the table to "ARC-" and assigning the twenty tests that come back is the
        /// gesture this is for; without it, an explicit assignment would mean a hundred and sixty
        /// separate menus.
        /// </summary>
        /// <param name="into">Where it is drawn.</param>
        /// <param name="rows">Every test.</param>
        /// <param name="find">The current search text.</param>
        /// <param name="patterns">How many patterns there are.</param>
        /// <param name="put">Writes one assignment.</param>
        private static void DrawBulk(Panel into, IReadOnlyList<Row> rows, string? find,
                                     int patterns, Action<Row, int?> put)
        {
            into.Children.Clear();

            var mine = Shown(rows, find).Where(r => r.Chosen).ToList();

            if (mine.Count == 0) return;

            // Declared first so the menu can hang off the button that opens it.
            System.Windows.Controls.Button button = null!;

            button = Ui.Quiet("Assign " + Ui.Count(mine.Count, "shown test") + " to…", () =>
                Parts.PopMenu(button, Choices(patterns, 0,
                    onto =>
                    {
                        // The last write redraws, so the rest are set without one each.
                        for (var at = 0; at < mine.Count - 1; at++) mine[at].Put = onto;

                        put(mine[mine.Count - 1], onto);
                    },
                    () =>
                    {
                        for (var at = 0; at < mine.Count - 1; at++) mine[at].Put = null;

                        put(mine[mine.Count - 1], null);
                    },
                    assigned: mine.Any(r => r.Put != null))));

            var bar = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(6),
                Child = button,
                HorizontalAlignment = HorizontalAlignment.Left,
            };

            bar.Tint(Border.BackgroundProperty, "Card2");

            into.Children.Add(bar);
        }

        /// <summary>
        /// The token chips, typing into one pattern box.
        ///
        /// Built per card rather than once for the column, so a chip inserts into the pattern the
        /// caret is in. As a single palette at the foot of the column it had to guess which box
        /// it meant, and it left a column of empty space between itself and the cards.
        /// </summary>
        /// <param name="box">The box the chips type into.</param>
        private static UIElement Palette(TextBox box)
        {
            // Sentence case, because Ui.Eyebrow upper-cases what it is handed.
            var held = Ui.Stack(new TextBlock { Text = "Insert a token" }.Wearing("Caps"));

            held.Margin = new Thickness(0, 8, 0, 0);

            // Three groups, and each chip named by what it reads rather than by which side it
            // reads it from. Fourteen chips in one block put "Row number" beside "Row, numbered"
            // with nothing to tell them apart.
            foreach (var group in new[] { "Row", "Column", "The pair" })
            {
                var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };

                foreach (var token in MatrixNaming.Tokens)
                {
                    var one = token;

                    if (Grouped(one) != group) continue;

                    chips.Children.Add(Chip(one, Named(one), () =>
                    {
                        var at = Math.Max(0, Math.Min(box.Text.Length, box.SelectionStart));

                        box.Text = box.Text.Substring(0, at) + one.Token + box.Text.Substring(at);
                        box.SelectionStart = at + one.Token.Length;

                        box.Focus();
                    }));
                }

                if (chips.Children.Count == 0) continue;

                var said = Ui.Small(group);

                said.Margin = new Thickness(0, 6, 0, 0);

                held.Children.Add(said);
                held.Children.Add(chips);
            }

            return held;
        }

        /// <summary>Which of the three groups a token belongs in.</summary>
        /// <param name="token">The token.</param>
        private static string Grouped(NameToken token) =>
            token.Token.StartsWith("{row", StringComparison.Ordinal) ? "Row"
            : token.Token.StartsWith("{column", StringComparison.Ordinal) ? "Column"
            : "The pair";

        /// <summary>
        /// What a chip says inside its group.
        ///
        /// The token titles carry the side in the words - "Row number" and "Row, numbered" - which
        /// under a heading that already says Row leaves two chips differing by a comma. Named from
        /// the token here, because the titles are Core's and the naming pattern reads them.
        /// </summary>
        /// <param name="token">The token.</param>
        private static string Named(NameToken token)
        {
            switch (token.Token)
            {
                case "{row}":
                case "{column}": return "Name";
                case "{rowNumber}":
                case "{columnNumber}": return "Number";
                case "{rowTitle}":
                case "{columnTitle}": return "Number and name";
                case "{rowCode}":
                case "{columnCode}": return "Code";
                case "{rowFolder}":
                case "{columnFolder}": return "Folder";
                case "{n}": return "Position in the build";
                default: break;
            }

            // A field of the project's own is titled "Row phase" or "Column package" already.
            var said = token.Title ?? string.Empty;

            if (said.StartsWith("Row ", StringComparison.Ordinal)) return said.Substring(4);
            if (said.StartsWith("Column ", StringComparison.Ordinal)) return said.Substring(7);

            return said;
        }

        /// <summary>Draws one token as a clickable chip.</summary>
        /// <param name="token">The token it inserts.</param>
        /// <param name="says">What the chip reads, inside its group.</param>
        /// <param name="onPress">Inserts it.</param>
        private static UIElement Chip(NameToken token, string says, Action onPress)
        {
            var chip = new Border
            {
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(7, 3, 7, 3),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = token.Why + "  " + token.Token,
                Child = new TextBlock { Text = says, FontSize = 11 }.Wearing("Body"),
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
