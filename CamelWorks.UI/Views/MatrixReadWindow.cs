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
    /// Choosing which of the document's tests become a matrix, and saying how they are named.
    ///
    /// <b>Two things were being decided for the person rather than by them.</b>
    ///
    /// The first was which tests. "Read the matrix out of the model" took every test in the
    /// document - the current round and the three abandoned ones, the sandbox tests somebody set
    /// up on a Friday, the duplicate of a duplicate - and made one grid out of the lot. On a real
    /// job that is a matrix of forty rows where the coordination has eleven, and the only way
    /// back was to delete rows afterwards and hope none of them was real.
    ///
    /// The second was what the names mean. Every test carries what it is between, so a matrix
    /// could always be rebuilt from the saved sets - and the grid that came back was named after
    /// whatever those sets are called, which on a real job is "ARC_GENERAL_ELEMENTS_R21_ISSUED"
    /// and not what anybody puts in front of a client. Meanwhile the discipline codes, the
    /// numbers, the packages and the kind of each test were sitting in the test's own name, in an
    /// order that job agreed on months ago, and none of it was read. The tool guessed; the person
    /// already knew.
    ///
    /// So: a table of the tests with a search and ticks, and beside it the naming conventions,
    /// written in the same tokens the matrix uses to write names. The table says live which
    /// pattern claimed each test, which is the only honest way to check a convention against a
    /// hundred and sixty names.
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

            /// <summary>Which pattern claimed it, from one, or nought for none.</summary>
            internal int By { get; set; }

            internal string Between =>
                Read.Shape == null || !Read.Shape.KnowsA || !Read.Shape.KnowsB
                    ? "does not say"
                    : Side(Read.Shape.SetA, Read.Shape.ScopesA) + "  vs  "
                      + Side(Read.Shape.SetB, Read.Shape.ScopesB);

            private static string Side(string? set, IReadOnlyList<string> scopes) =>
                !string.IsNullOrWhiteSpace(set)
                    ? set!.Trim()
                    : scopes != null && scopes.Count > 0
                        ? (scopes.Count == 1 ? scopes[0] : Ui.Count(scopes.Count, "model"))
                        : "nothing";
        }

        /// <summary>
        /// Ask which tests, and how they are named.
        /// </summary>
        /// <param name="tests">Every test the document holds, already read.</param>
        /// <param name="pattern">The pattern the matrix writes names with, as a starting point.</param>
        /// <returns>The matrix that was read, or null when the window was closed without one.</returns>
        public static MatrixReading? Ask(IReadOnlyList<ClashTestRead> tests, string? pattern)
        {
            if (tests == null || tests.Count == 0) return null;

            MatrixReading? done = null;

            var window = new Window
            {
                Title = "CamelWorks - Read the matrix out of the model",
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
        /// The screen itself, without the window round it.
        ///
        /// <b>Split out so it can be photographed.</b> A WPF <c>Window</c> needs to be shown to
        /// exist, and the render harness has no screen to show one on - so every dialogue in this
        /// product was checked by a coordinator on a licensed seat and described to me in words,
        /// which is the slowest and least exact way a layout fault can be reported. What is
        /// wrapped in a window here is an ordinary panel, and the harness draws the panel.
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

            // <b>The pattern the matrix writes with is the pattern it should read with.</b>
            // Somebody who has set this job's naming up once has said how their tests are named;
            // starting from anything else would ask them to say it twice.
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
            var take = Ui.Check("Read tests no pattern fits from their sets", true, null);
            var names = Ui.Check("Let the names decide what a row is called", true, null);

            TextBox? focused = null;

            // Which pattern claims which test, worked out from what is in the boxes now. Every
            // redraw of either side runs this, because a pattern somebody is halfway through
            // typing has to say what it would catch while they are still typing it.
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
                Ui.Eyebrow("Drop a token in"),
                chips,
                take,
                names);

            under.Margin = new Thickness(0, 8, 0, 0);

            DockPanel.SetDock(under, Dock.Bottom);
            side.Children.Add(under);

            var says = Ui.Small("A pattern says what a test's name looks like. The first one that "
                                + "fits a name claims it, so put the exceptions above the general "
                                + "one.");

            says.TextWrapping = TextWrapping.Wrap;
            says.Margin = new Thickness(0, 2, 0, 8);

            var top = Ui.Stack(Ui.Eyebrow("How your tests are named"), says);

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
                Ui.Quiet("Tick shown", () => { Sweep(rows, find.Text, true); Redraw(); }),
                Ui.Quiet("Untick shown", () => { Sweep(rows, find.Text, false); Redraw(); }),
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

            var go = Ui.Primary("Read them", () =>
            {
                trouble.Children.Clear();

                var chosen = rows.Where(r => r.Chosen).Select(r => r.Read).ToList();

                if (chosen.Count == 0)
                {
                    trouble.Children.Add(Ui.Problem("Tick at least one test."));
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

        /// <summary>The tests the search leaves on the screen.</summary>
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
        /// The patterns, each with what it catches under it.
        ///
        /// <b>The count is the whole feedback loop.</b> A pattern is right or wrong against a
        /// hundred and sixty names nobody is going to read, so what has to be on the screen while
        /// somebody types is how many of them it fits and what it pulled out of the first one.
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

                // <b>A pattern is longer than a field.</b> Written on one line it scrolled out of
                // its own box - "{columnCode}-{columnNumb" - so the half somebody was checking
                // was the half they could not see, on the one control whose exact text is the
                // whole point of the screen.
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
                    var why = Ui.Small(one.Trouble ?? "This pattern cannot be read.");

                    why.TextWrapping = TextWrapping.Wrap;
                    why.Margin = new Thickness(0, 5, 0, 0);
                    why.Tint(TextBlock.ForegroundProperty, "RedTx");

                    stack.Children.Add(why);
                }
                else
                {
                    var mine = rows.Where(r => r.By == here + 1).ToList();

                    var count = Ui.Small(mine.Count == 0
                        ? "Fits none of these tests"
                        : "Claims " + Ui.Count(mine.Count, "test"));

                    count.Margin = new Thickness(0, 5, 0, 0);
                    stack.Children.Add(count);

                    // What it actually pulled out of a real name, which is the difference
                    // between a pattern that matches and a pattern that is right.
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
        /// The tests, ticked or not, with the pattern that claimed each one.
        ///
        /// A row is pressed rather than only its box, because a table of a hundred and sixty
        /// where only a twelve-pixel square is live is a table nobody can work through.
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
                var none = Ui.Small("No test's name has that in it.");

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

                    // Shift takes the run from the last one pressed, the way every list of
                    // things to tick has worked for thirty years.
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

        /// <summary>The last row pressed, so shift can take the run from it.</summary>
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

        /// <summary>What the read would come to, under the table.</summary>
        private static void DrawStrip(Panel into, IReadOnlyList<Row> rows,
                                      IReadOnlyList<MatrixNamePattern> read)
        {
            into.Children.Clear();

            var chosen = rows.Count(r => r.Chosen);
            var claimed = rows.Count(r => r.Chosen && r.By > 0);

            var said = Ui.Small(Ui.Count(chosen, "test") + " ticked of "
                                + rows.Count.ToString(CultureInfo.InvariantCulture) + "  ·  "
                                + claimed.ToString(CultureInfo.InvariantCulture)
                                + " named by a pattern");

            said.Margin = new Thickness(4, 8, 4, 0);

            into.Children.Add(said);
        }

        /// <summary>One token, as something to press.</summary>
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
