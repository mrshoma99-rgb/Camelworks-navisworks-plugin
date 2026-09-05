using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CamelWorks.Core.Clash;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>Which pane the naming window opens on.</summary>
    public enum NamingPane
    {
        /// <summary>Which pattern reads each test.</summary>
        Tests = 0,

        /// <summary>The patterns themselves.</summary>
        Patterns = 1,

        /// <summary>The fields a pattern's tokens name.</summary>
        Fields = 2,

        /// <summary>Writing one field across a run of tests.</summary>
        Write = 3,
    }

    /// <summary>
    /// How this project's tests are named, and the one place that changes it.
    ///
    /// The Clash Manager can band, filter and sort by any field a test's name carries, and it can
    /// renumber a run of tests by rewriting one field of each name. Both need the same three
    /// things: the fields, the patterns those fields are written into, and which pattern reads
    /// which test. Splitting them across three surfaces would mean a coordinator who wants to
    /// renumber sixty tests finds out here that a pattern is missing and has to go somewhere else
    /// to write it.
    ///
    /// The naming is the project's, not the matrix's. A coordinator working through last month's
    /// results has a document full of tests and no board open at all, so it is read from and
    /// written to the project file.
    /// </summary>
    public static class ClashNaming
    {
        private const string TestsPane = "Tests";
        private const string PatternsPane = "Patterns";
        private const string FieldsPane = "Fields";
        private const string WritePane = "Renumber";

        private const string Counted = "Counted";
        private const string Everywhere = "Same on all";

        /// <summary>One test as this window holds it, so a rename shows without a re-read.</summary>
        private sealed class Line
        {
            internal Line(string id, string name) { Id = id; Name = name; }

            internal string Id { get; }

            internal string Name { get; set; }
        }

        /// <summary>
        /// Show the naming and let it be changed.
        /// </summary>
        /// <param name="book">The project's naming, edited in place.</param>
        /// <param name="tests">The tests, in the order the rail shows them.</param>
        /// <param name="rename">Renames one test, and says whether it landed.</param>
        /// <param name="start">Which pane to open on.</param>
        /// <returns>True when something changed, so the caller can save and redraw.</returns>
        internal static bool Ask(NamingBook book, IReadOnlyList<(string Id, string Name)> tests,
                                 Func<string, string, bool> rename,
                                 NamingPane start = NamingPane.Tests)
        {
            if (book == null) return false;

            var window = new Window
            {
                Title = "CamelWorks - Naming",
                Width = 720,
                Height = 660,
                MinWidth = 560,
                MinHeight = 440,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var touched = false;

            var ground = new Border
            {
                Child = Body(book, Held(tests), rename, start, window,
                             () => touched = true, () => window.Close()),
            };

            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            Skin.Apply(window);
            HostWindow.Own(window);

            window.ShowDialog();

            return touched;
        }

        /// <summary>
        /// The window's content as a panel, so the render harness can capture it.
        /// </summary>
        /// <param name="book">The project's naming.</param>
        /// <param name="tests">The tests, in the order the rail shows them.</param>
        /// <param name="start">Which pane to draw.</param>
        public static UIElement Drawn(NamingBook book,
                                      IReadOnlyList<(string Id, string Name)> tests,
                                      NamingPane start = NamingPane.Tests) =>
            Body(book, Held(tests), (id, name) => { _ = id; _ = name; return true; },
                 start, null, null, null);

        /// <summary>The tests as lines this window can rename in place.</summary>
        private static List<Line> Held(IReadOnlyList<(string Id, string Name)>? tests) =>
            (tests ?? Array.Empty<(string, string)>())
            .Select(one => new Line(one.Id ?? string.Empty, one.Item2 ?? string.Empty))
            .Where(one => one.Id.Length > 0)
            .ToList();

        /// <summary>Build the surface.</summary>
        private static UIElement Body(NamingBook book, List<Line> tests,
                                      Func<string, string, bool> rename, NamingPane start,
                                      FrameworkElement? near, Action? onTouched, Action? onDone)
        {
            var pane = start;

            // Which pattern card carries the token palette. Held here rather than inside the
            // pane, because the pane is rebuilt on every edit and would put it back on the first.
            var live = 0;

            // The series the write pane counts in. A field rather than three loose boxes, because
            // MatrixField already knows how to count in numbers and letters, how to pad and what
            // "the same on every line" means.
            var how = new MatrixField("value", "Value") { Fill = MatrixFill.Series, Pad = 2 };

            string? token = null;

            var head = Ui.Stack();
            var held = Ui.Stack();
            var foot = Ui.Stack();

            void Touch() => onTouched?.Invoke();

            void Redraw()
            {
                head.Children.Clear();
                head.Children.Add(Switcher(pane, said =>
                {
                    pane = said == PatternsPane ? NamingPane.Patterns
                        : said == FieldsPane ? NamingPane.Fields
                        : said == WritePane ? NamingPane.Write
                        : NamingPane.Tests;

                    Redraw();
                }));

                held.Children.Clear();
                foot.Children.Clear();

                switch (pane)
                {
                    case NamingPane.Patterns:
                        Patterns(held, book, tests, near, live,
                                 at => { live = at; Redraw(); }, Touch, Redraw);
                        break;

                    case NamingPane.Fields:
                        Fields(held, book, near, Touch, Redraw);
                        break;

                    case NamingPane.Write:
                        Writing(held, foot, book, tests, rename, how, token,
                                said => token = said, Touch);
                        break;

                    default:
                        Tests(held, foot, book, tests, Touch);
                        break;
                }
            }

            Redraw();

            var says = Ui.Small(
                "A pattern says how a test's name is put together, in fields the project names. "
                + "Once a test is on a pattern, the manager can band, filter and sort by any "
                + "field the name carries, and renumber the tests by rewriting one of them.");

            says.TextWrapping = TextWrapping.Wrap;
            says.Margin = new Thickness(0, 0, 0, 10);

            var top = Ui.Stack(says, head);

            top.Margin = new Thickness(0, 0, 0, 10);

            var body = new DockPanel { LastChildFill = true, Margin = new Thickness(16, 14, 16, 0) };

            DockPanel.SetDock(top, Dock.Top);
            body.Children.Add(top);

            DockPanel.SetDock(foot, Dock.Bottom);
            body.Children.Add(Ui.Loose(foot));

            body.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(held),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var root = new DockPanel { LastChildFill = true, MaxWidth = 700 };

            var acts = new Border
            {
                Margin = new Thickness(16, 10, 16, 14),
                Child = Ui.Actions(Ui.Primary("Done", () => onDone?.Invoke())),
            };

            DockPanel.SetDock(acts, Dock.Bottom);
            root.Children.Add(acts);
            root.Children.Add(body);

            return root;
        }

        /// <summary>The four panes, as one control.</summary>
        private static UIElement Switcher(NamingPane pane, Action<string> onChanged)
        {
            var chosen = pane switch
            {
                NamingPane.Patterns => PatternsPane,
                NamingPane.Fields => FieldsPane,
                NamingPane.Write => WritePane,
                _ => TestsPane,
            };

            var pick = Ui.Segmented(new[] { TestsPane, PatternsPane, FieldsPane, WritePane },
                                    chosen, onChanged);

            if (pick is FrameworkElement held) held.HorizontalAlignment = HorizontalAlignment.Left;

            return pick;
        }

        // -------------------------------------------------------------------------------
        // Tests: which pattern reads each one
        // -------------------------------------------------------------------------------

        /// <summary>
        /// Every test, and the pattern that reads it.
        ///
        /// The assignment is explicit. Matching every pattern against every name afresh would
        /// mean that where two patterns both fit, the order of the list silently decides, and a
        /// test somebody assigned by hand would not stay assigned. What a pattern would fit is
        /// still offered, as a proposal, which is the one place order is allowed to decide
        /// anything.
        /// </summary>
        private static void Tests(Panel into, Panel foot, NamingBook book, List<Line> tests,
                                  Action touch)
        {
            var find = string.Empty;
            var list = Ui.Stack();

            var box = Ui.Text(string.Empty, "Find a test", typed => { }, 240);

            box.HorizontalAlignment = HorizontalAlignment.Stretch;

            void Fill()
            {
                list.Children.Clear();

                var said = find.Trim();

                var shown = tests
                    .Where(one => said.Length == 0
                                  || one.Name.IndexOf(said, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (shown.Count == 0)
                {
                    list.Children.Add(Ui.Empty(
                        tests.Count == 0 ? "No tests" : "Nothing matches",
                        tests.Count == 0
                            ? "Read the tests on the Manage tab first."
                            : "No test's name contains " + Quoted(said) + "."));

                    return;
                }

                foreach (var line in shown) list.Children.Add(Row(book, line, touch, Fill, Foot));

                Foot();
            }

            void Foot()
            {
                foot.Children.Clear();

                var unread = tests.Count(one => book.Read(one.Id, one.Name) == null);

                if (tests.Count == 0) return;

                var says = Ui.Small(unread == 0
                    ? "Every test is on a pattern."
                    : Ui.Count(unread, "test") + " on no pattern. "
                      + "Banding, filtering and renumbering leave them alone.");

                says.TextWrapping = TextWrapping.Wrap;
                says.Margin = new Thickness(0, 8, 0, 0);

                foot.Children.Add(says);

                if (unread == 0 || book.Patterns.Count == 0) return;

                var fits = tests.Count(one => book.Read(one.Id, one.Name) == null
                                              && book.Fitting(one.Name) > 0);

                if (fits == 0) return;

                var act = Ui.Quiet("Take the proposal for " + Ui.Count(fits, "test"), () =>
                    Ui.Safely(() =>
                    {
                        foreach (var line in tests)
                        {
                            if (book.Read(line.Id, line.Name) != null) continue;

                            var at = book.Fitting(line.Name);

                            if (at > 0) book.Bind(line.Id, at);
                        }

                        touch();
                        Fill();
                    }));

                act.HorizontalAlignment = HorizontalAlignment.Left;
                act.Margin = new Thickness(0, 6, 0, 0);

                foot.Children.Add(act);
            }

            box.TextChanged += (s, e) => Ui.Safely(() =>
            {
                find = box.Text ?? string.Empty;
                Fill();
            });

            Fill();

            box.Margin = new Thickness(0, 0, 0, 8);

            into.Children.Add(box);
            into.Children.Add(list);
        }

        /// <summary>One test: its name, its pattern, and what that pattern reads.</summary>
        private static UIElement Row(NamingBook book, Line line, Action touch, Action redraw,
                                     Action foot)
        {
            var read = book.Read(line.Id, line.Name);
            var bound = book.Bound.TryGetValue(line.Id, out var at) ? at : 0;
            var fits = bound == 0 ? book.Fitting(line.Name) : 0;

            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 9),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
            };

            card.Tint(Border.BackgroundProperty, "Card2");
            card.Tint(Border.BorderBrushProperty, "Bd");

            var top = new DockPanel { LastChildFill = true };

            var chip = Chip(bound == 0
                                ? (fits > 0 ? "Pattern " + Numbered(fits) + " fits" : "No pattern")
                                : "Pattern " + Numbered(bound),
                            bound != 0);

            chip.MouseLeftButtonUp += (s, e) => Ui.Safely(() =>
            {
                e.Handled = true;

                Parts.PopMenu(chip, Choices(book.Patterns.Count, fits, onto =>
                {
                    book.Bind(line.Id, onto);
                    touch();
                    redraw();
                }));
            });

            DockPanel.SetDock(chip, Dock.Right);
            top.Children.Add(chip);

            top.Children.Add(new TextBlock
            {
                Text = line.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            }.Wearing("Body"));

            var stack = Ui.Stack(top);

            var said = read == null
                ? (bound == 0
                    ? "No pattern reads this name."
                    : "Pattern " + Numbered(bound) + " does not fit this name.")
                : Reads(book, read, book.Compiled(read.Pattern));

            // One line, trimmed, with the whole of it on the tip. Six fields wrapped to a second
            // line under every row, which doubled the height of the list and buried the names
            // the list is about.
            var under = new TextBlock
            {
                Text = said,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
                ToolTip = said,
            }.Wearing("Body");

            under.Tint(TextBlock.ForegroundProperty, "Muted");

            stack.Children.Add(under);

            card.Child = stack;

            foot();

            return card;
        }

        /// <summary>
        /// What a pattern read out of a name, field by field, in the project's own words.
        ///
        /// Titled rather than tokenised. The whole point of the fields is that a project names
        /// them, so a line reading "rowCode = ARC" hands back the internal spelling of a word the
        /// project already has a word for.
        /// </summary>
        /// <param name="book">The project's naming, for the titles.</param>
        /// <param name="read">What the pattern found.</param>
        /// <param name="pattern">The pattern that found it.</param>
        private static string Reads(NamingBook book, NamingRead read, MatrixNamePattern? pattern)
        {
            if (pattern == null) return string.Empty;

            var held = pattern.Tokens
                .Select(one => Titled(book, one) + ": "
                               + (read.Value(one).Length == 0 ? "\u2014" : read.Value(one)))
                .ToList();

            return held.Count == 0 ? string.Empty : string.Join("   \u00b7   ", held);
        }

        /// <summary>The menu behind an assignment: every pattern, and none.</summary>
        private static IReadOnlyList<Parts.Entry> Choices(int patterns, int fits, Action<int> onPut)
        {
            var items = new List<Parts.Entry>();

            if (patterns == 0)
                return new List<Parts.Entry>
                {
                    Parts.Entry.Barred("No patterns yet", null,
                                       "Write one on the Patterns pane first."),
                };

            for (var at = 1; at <= patterns; at++)
            {
                var here = at;

                items.Add(Parts.Entry.Act(
                    "Pattern " + Numbered(here)
                    + (here == fits ? "  (fits this name)" : string.Empty),
                    null, () => onPut(here)));
            }

            items.Add(Parts.Entry.Rule());
            items.Add(Parts.Entry.Act("No pattern", null, () => onPut(0)));

            return items;
        }

        // -------------------------------------------------------------------------------
        // Patterns
        // -------------------------------------------------------------------------------

        /// <summary>The patterns, with what each one reads on this job.</summary>
        private static void Patterns(Panel into, NamingBook book, List<Line> tests,
                                     FrameworkElement? near, int live, Action<int> onLive,
                                     Action touch, Action redraw)
        {
            var patterns = book.Patterns.ToList();

            if (patterns.Count == 0)
            {
                into.Children.Add(Ui.Empty(
                    "No patterns yet",
                    "A pattern is a test's name with its changing parts written as fields: "
                    + MatrixNaming.Default + ". Write one and the manager can read every name "
                    + "that follows it."));
            }

            // The palette goes under one card, not under every one. Fourteen chips repeated on
            // each pattern is a wall of them: two patterns filled the window and pushed "Add a
            // pattern" off the bottom of it.
            for (var at = 0; at < patterns.Count; at++)
                into.Children.Add(Pattern(book, patterns, at, tests, at == live, onLive,
                                          touch, redraw));

            var add = Ui.Quiet("Add a pattern", () => Ui.Safely(() =>
            {
                patterns.Add(MatrixNaming.Default);
                book.Patterned(patterns);
                touch();

                // The palette follows the card that was just added, which is the one about to be
                // typed into.
                onLive(patterns.Count - 1);
            }));

            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 4, 0, 0);

            into.Children.Add(add);

            _ = near;
        }

        /// <summary>One pattern: the text, what is wrong with it, and how many tests it reads.</summary>
        private static UIElement Pattern(NamingBook book, List<string> patterns, int at,
                                         List<Line> tests, bool live, Action<int> onLive,
                                         Action touch, Action redraw)
        {
            var here = at;
            var made = MatrixNamePattern.Parse(patterns[here], book.Scheme);

            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 9, 10, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
            };

            card.Tint(Border.BackgroundProperty, "Card2");
            card.Tint(Border.BorderBrushProperty, "Bd");

            var box = Ui.Text(patterns[here], MatrixNaming.Default, typed => { }, 240);

            box.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.TextWrapping = TextWrapping.Wrap;
            box.MinHeight = 44;

            // Written back on the way out rather than on every keystroke, so the card is not
            // rebuilt under the caret while a pattern is being typed.
            box.GotKeyboardFocus += (s, e) => Ui.Safely(() =>
            {
                if (!live) onLive(here);
            });

            box.LostFocus += (s, e) => Ui.Safely(() =>
            {
                if (string.Equals(box.Text, patterns[here], StringComparison.Ordinal)) return;

                patterns[here] = box.Text ?? string.Empty;
                book.Patterned(patterns);
                touch();
                redraw();
            });

            var top = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };

            var acts = Ui.Across(
                Ui.Quiet("Up", () => Ui.Safely(() => Move(book, patterns, here, -1, touch, redraw))),
                Ui.Quiet("Down", () => Ui.Safely(() => Move(book, patterns, here, 1, touch, redraw))),
                Ui.Quiet("Remove", () => Ui.Safely(() =>
                {
                    patterns.RemoveAt(here);
                    book.Patterned(patterns);
                    touch();
                    redraw();
                })));

            DockPanel.SetDock(acts, Dock.Right);
            top.Children.Add(acts);

            top.Children.Add(new TextBlock
            {
                Text = "Pattern " + Numbered(here + 1),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            }.Wearing("Body"));

            var stack = Ui.Stack(top, box);

            var says = Ui.Small(made.IsUsable
                ? Standing(book, here + 1, tests)
                : made.Trouble ?? string.Empty);

            says.TextWrapping = TextWrapping.Wrap;
            says.Margin = new Thickness(0, 6, 0, 0);
            says.Tint(TextBlock.ForegroundProperty, made.IsUsable ? "Muted" : "RedTx");

            stack.Children.Add(says);

            if (live) stack.Children.Add(Palette(book, box, patterns, here, touch, redraw));

            card.Child = stack;

            return card;
        }

        /// <summary>Move a pattern up or down, keeping the bindings pointing where they did.</summary>
        private static void Move(NamingBook book, List<string> patterns, int at, int by,
                                 Action touch, Action redraw)
        {
            var to = at + by;

            if (to < 0 || to >= patterns.Count) return;

            // The bindings are read out by pattern text and put back by position, because the
            // positions are what has just moved. Rebinding by text is the only way a test stays
            // on the pattern it was put on.
            var bound = book.Bound.ToDictionary(one => one.Key, one => one.Value,
                                                StringComparer.Ordinal);

            var was = patterns.ToList();
            var held = patterns[at];

            patterns.RemoveAt(at);
            patterns.Insert(to, held);

            book.Patterned(patterns);

            foreach (var one in bound)
            {
                if (one.Value <= 0 || one.Value > was.Count) continue;

                var text = was[one.Value - 1];
                var now = patterns.FindIndex(p => string.Equals(p, text, StringComparison.Ordinal));

                book.Bind(one.Key, now + 1);
            }

            touch();
            redraw();
        }

        /// <summary>How many of this job's tests a pattern is reading.</summary>
        private static string Standing(NamingBook book, int at, List<Line> tests)
        {
            var reads = tests.Count(one => book.Bound.TryGetValue(one.Id, out var on) && on == at
                                           && book.Read(one.Id, one.Name) != null);

            var bound = tests.Count(one => book.Bound.TryGetValue(one.Id, out var on) && on == at);

            if (bound == 0) return "No test is on this pattern yet.";

            return reads == bound
                ? "Reads " + Ui.Count(bound, "test") + "."
                : "On " + Ui.Count(bound, "test") + ", and reads " + reads + " of them.";
        }

        /// <summary>The tokens, as chips that write themselves into the pattern.</summary>
        private static UIElement Palette(NamingBook book, TextBox box, List<string> patterns,
                                         int at, Action touch, Action redraw)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

            foreach (var token in MatrixNaming.Offered(book.Scheme))
            {
                var one = token;

                chips.Children.Add(Chip(one, () =>
                {
                    var where = Math.Max(0, Math.Min(box.Text.Length, box.SelectionStart));

                    box.Text = box.Text.Substring(0, where) + one.Token + box.Text.Substring(where);
                    box.SelectionStart = where + one.Token.Length;

                    patterns[at] = box.Text;
                    book.Patterned(patterns);
                    touch();
                    redraw();
                }));
            }

            var held = Ui.Stack(Ui.Eyebrow("Insert a field"), chips);

            held.Margin = new Thickness(0, 8, 0, 0);

            return held;
        }

        // -------------------------------------------------------------------------------
        // Fields
        // -------------------------------------------------------------------------------

        /// <summary>
        /// The fields a pattern's tokens name.
        ///
        /// Only what a field is called and whether it exists. How a field is filled in — counted
        /// down the axis, the same on every line — is about a matrix's rows and columns, and is
        /// set on the matrix's own Fields window rather than here, where there are no rows.
        /// </summary>
        private static void Fields(Panel into, NamingBook book, FrameworkElement? near,
                                   Action touch, Action redraw)
        {
            foreach (var field in book.Scheme.Fields.ToList())
                into.Children.Add(Field(book, field, touch, redraw));

            var add = Ui.Quiet("Add a field", () => Ui.Safely(() =>
            {
                if (near == null) return;

                Parts.Ask(near, "What is the field called? “Phase”, “Sub-discipline”, “Package”.",
                          string.Empty, "Add", said =>
                {
                    var title = (said ?? string.Empty).Trim();

                    if (title.Length == 0) return;

                    var key = new string(title.Where(char.IsLetterOrDigit).ToArray());

                    if (!MatrixField.IsUsable(key))
                    {
                        Ui.Told("A field's name has to start with a letter.");
                        return;
                    }

                    if (book.Scheme.Has(key))
                    {
                        Ui.Told("There is already a field called “" + title + "”.");
                        return;
                    }

                    book.Scheme.Add(key, title);
                    touch();
                    redraw();
                });
            }));

            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 4, 0, 0);

            into.Children.Add(add);
        }

        /// <summary>One field: what it is called, and the tokens it writes.</summary>
        private static UIElement Field(NamingBook book, MatrixField field, Action touch,
                                       Action redraw)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 8, 10, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
            };

            card.Tint(Border.BackgroundProperty, "Card2");
            card.Tint(Border.BorderBrushProperty, "Bd");

            var row = new Grid();

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = Ui.Text(field.Title, "What it is called", typed =>
            {
                var said = (typed ?? string.Empty).Trim();

                if (said.Length == 0 || said == field.Title) return;

                field.Title = said;
                touch();
            }, 170);

            title.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(title, 0);
            row.Children.Add(title);

            var tokens = new TextBlock
            {
                Text = Tokens(field),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Write these into a pattern to put this field in a test's name.",
            }.Wearing("Mono");

            tokens.Tint(TextBlock.ForegroundProperty, "Muted");

            Grid.SetColumn(tokens, 1);
            row.Children.Add(tokens);

            if (!field.IsBuiltIn)
            {
                var drop = Ui.Quiet("Remove", () => Ui.Safely(() =>
                {
                    book.Scheme.Remove(field.Key);
                    touch();
                    redraw();
                }));

                drop.VerticalAlignment = VerticalAlignment.Center;
                drop.ToolTip = "Remove this field. Patterns that used it stop reading until "
                               + "the token is taken out of them.";

                Grid.SetColumn(drop, 2);
                row.Children.Add(drop);
            }

            card.Child = row;

            return card;
        }

        /// <summary>The two tokens a field writes, as a pattern carries them.</summary>
        private static string Tokens(MatrixField field)
        {
            foreach (var one in MatrixNaming.Tokens)
            {
                // The four built-in fields already have tokens of their own, which are not built
                // from the key: the name field is {row}, not {rowName}.
                if (field.Key == MatrixField.NameKey) return "{row}   {column}";
                if (field.Key == MatrixField.NumberKey) return "{rowNumber}   {columnNumber}";
                if (field.Key == MatrixField.CodeKey) return "{rowCode}   {columnCode}";
                if (field.Key == MatrixField.FolderKey) return "{rowFolder}   {columnFolder}";

                _ = one;
                break;
            }

            return field.RowToken + "   " + field.ColumnToken;
        }

        // -------------------------------------------------------------------------------
        // Renumber: write one field across a run of tests
        // -------------------------------------------------------------------------------

        /// <summary>
        /// Write one field across the tests, and show what that would do before it is done.
        ///
        /// Every line of this is a rename of a host test, so the plan is on the screen first,
        /// refusals and all. A test the pattern cannot write still takes its place in the count,
        /// so refusing the third of ten does not shift the seven under it.
        /// </summary>
        private static void Writing(Panel into, Panel foot, NamingBook book, List<Line> tests,
                                    Func<string, string, bool> rename, MatrixField how,
                                    string? token, Action<string?> onToken, Action touch)
        {
            var offered = Offered(book);

            if (offered.Count == 0)
            {
                into.Children.Add(Ui.Empty(
                    "Nothing to write",
                    "The tests are on no pattern, or the patterns carry no fields. Write a "
                    + "pattern and put the tests on it first."));

                return;
            }

            if (token == null || !offered.Any(one => one.Key == token))
            {
                // A number, not whatever the token list happens to lead with. The pane is reached
                // by pressing Renumber, and the token list leads with the row's name — so the
                // default plan was to write a series over every discipline name on the job.
                var wanted = offered.Where(one => Numbered(one.Key)).ToList();

                token = wanted.Count > 0 ? wanted[0].Key : offered[0].Key;
                onToken(token);
            }

            var chosen = token;
            var preview = Ui.Stack();
            var says = Ui.Stack();

            void Fill()
            {
                var plan = NamingWrite.Plan(book, tests.Select(one => (one.Id, one.Name)),
                                            chosen, how);

                preview.Children.Clear();
                says.Children.Clear();

                foreach (var step in plan.Take(200))
                    preview.Children.Add(Step(step));

                if (plan.Count > 200)
                    preview.Children.Add(Ui.Small(
                        "and " + Ui.Count(plan.Count - 200, "more test") + ", not drawn here."));

                var changing = NamingWrite.Changing(plan).Count;
                var refused = NamingWrite.Refused(plan).Count;

                var line = Ui.Small(changing == 0
                    ? "No name would change."
                    : Ui.Count(changing, "test") + " would be renamed"
                      + (refused == 0
                          ? "."
                          : ", and " + refused + " left alone."));

                line.TextWrapping = TextWrapping.Wrap;
                line.Margin = new Thickness(0, 8, 0, 6);

                says.Children.Add(line);

                var act = Ui.Primary("Rename " + Ui.Count(changing, "test"), () => Ui.Safely(() =>
                {
                    var landed = 0;

                    foreach (var step in NamingWrite.Changing(plan))
                    {
                        if (!rename(step.Id, step.Now!)) continue;

                        var line2 = tests.FirstOrDefault(one => one.Id == step.Id);

                        if (line2 != null) line2.Name = step.Now!;

                        landed++;
                    }

                    if (landed > 0) touch();

                    Ui.Told(landed == 0
                        ? "Nothing was renamed."
                        : "Renamed " + Ui.Count(landed, "test") + ".");

                    Fill();
                }));

                act.IsEnabled = changing > 0;
                act.HorizontalAlignment = HorizontalAlignment.Left;

                says.Children.Add(act);
            }

            var field = Parts.Presser(Titled(book, chosen), Glyphs.Tune, "Pick a field",
                                      on => Parts.PopMenu(on, Picking(offered, one =>
                                      {
                                          onToken(one);
                                          chosen = one;
                                          Fill();
                                      })));

            var kinds = Ui.Segmented(new[] { Counted, Everywhere },
                                     how.Fill == MatrixFill.Same ? Everywhere : Counted,
                                     said =>
                                     {
                                         how.Fill = said == Everywhere
                                             ? MatrixFill.Same
                                             : MatrixFill.Series;

                                         Fill();
                                     });

            var top = Ui.Across(Ui.Field("Field", field, null),
                                Ui.Field("Values", kinds, null));

            top.Margin = new Thickness(0, 0, 0, 8);

            into.Children.Add(top);
            into.Children.Add(how.Fill == MatrixFill.Same
                ? Constant(how, Fill)
                : Series(how, Fill));

            var note = Ui.Small(how.Fill == MatrixFill.Same
                ? "Every test gets the same value."
                : "Counted in the order the list shows them, which is the order the rail is "
                  + "sorted in.");

            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 8, 0, 8);

            into.Children.Add(note);
            into.Children.Add(Ui.Eyebrow("What it would write"));
            into.Children.Add(preview);

            foot.Children.Add(says);

            Fill();
        }

        /// <summary>One test's line in the plan: what it is called, and what it would become.</summary>
        private static UIElement Step(NamingStep step)
        {
            // A grid of three, not two docked halves. Docked, the two names sat at opposite edges
            // of a seven-hundred-pixel window with nothing between them, so reading a rename meant
            // crossing the gap and guessing which right-hand line belonged to which left-hand one.
            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };

            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });

            var left = new TextBlock
            {
                Text = step.Was,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = step.Was,
            }.Wearing("Body");

            left.Tint(TextBlock.ForegroundProperty, "Muted");

            Grid.SetColumn(left, 0);
            row.Children.Add(left);

            var arrow = new TextBlock
            {
                Text = "\u2192",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
            }.Wearing("Body");

            arrow.Tint(TextBlock.ForegroundProperty, "Faint");

            Grid.SetColumn(arrow, 1);
            row.Children.Add(arrow);

            var right = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = step.Why != NamingRefusal.None
                    ? NamingWrite.Said(step.Why)
                    : step.Changes ? step.Now : "unchanged",
            }.Wearing("Body");

            right.ToolTip = right.Text;

            right.Tint(TextBlock.ForegroundProperty,
                       step.Why != NamingRefusal.None ? "RedTx"
                       : step.Changes ? "Accent" : "Muted");

            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            return row;
        }

        /// <summary>
        /// The fields to pick from, numbers first.
        ///
        /// Under two headings rather than one list, because writing a series into a discipline's
        /// name is a different act from renumbering it and a flat list puts them a keystroke
        /// apart.
        /// </summary>
        /// <param name="offered">The fields the patterns carry.</param>
        /// <param name="onPick">Called with the token that was picked.</param>
        private static IReadOnlyList<Parts.Entry> Picking(
            IReadOnlyList<KeyValuePair<string, string>> offered, Action<string> onPick)
        {
            var items = new List<Parts.Entry>();

            void Add(IEnumerable<KeyValuePair<string, string>> these)
            {
                foreach (var one in these)
                {
                    var here = one.Key;

                    items.Add(Parts.Entry.Act(one.Value, null, () => onPick(here)));
                }
            }

            var numbers = offered.Where(one => Numbered(one.Key)).ToList();
            var rest = offered.Where(one => !Numbered(one.Key)).ToList();

            if (numbers.Count > 0)
            {
                items.Add(Parts.Entry.Heading("Numbers"));
                Add(numbers);
            }

            if (rest.Count > 0)
            {
                if (numbers.Count > 0) items.Add(Parts.Entry.Rule());

                items.Add(Parts.Entry.Heading("Every other field"));
                Add(rest);
            }

            return items;
        }

        /// <summary>Whether a token holds a number, and is what Renumber should reach for.</summary>
        /// <param name="token">The token, without braces.</param>
        private static bool Numbered(string token) =>
            token.IndexOf("number", StringComparison.OrdinalIgnoreCase) >= 0
            || string.Equals(token, "n", StringComparison.OrdinalIgnoreCase);

        /// <summary>The fields the project's patterns carry, as token and title.</summary>
        private static List<KeyValuePair<string, string>> Offered(NamingBook book)
        {
            var carried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var at = 1; at <= book.Patterns.Count; at++)
            {
                var pattern = book.Compiled(at);

                if (pattern == null || !pattern.IsUsable) continue;

                foreach (var one in pattern.Tokens) carried.Add(one);
            }

            return MatrixNaming.Offered(book.Scheme)
                .Where(one => carried.Contains(Bare(one.Token)))
                .Select(one => new KeyValuePair<string, string>(Bare(one.Token), one.Title))
                .ToList();
        }

        /// <summary>What a token is called, in the words its chip uses.</summary>
        private static string Titled(NamingBook book, string? token)
        {
            foreach (var one in MatrixNaming.Offered(book.Scheme))
                if (string.Equals(Bare(one.Token), token, StringComparison.OrdinalIgnoreCase))
                    return one.Title;

            return token ?? string.Empty;
        }

        /// <summary>A token without its braces.</summary>
        private static string Bare(string? token) =>
            (token ?? string.Empty).Trim().Trim('{', '}');

        /// <summary>The controls a counted write needs, on one line.</summary>
        private static UIElement Series(MatrixField how, Action onChange)
        {
            var kinds = Ui.Segmented(new[] { "1, 2, 3", "A, B, C" },
                how.Steps == MatrixSteps.Letters ? "A, B, C" : "1, 2, 3",
                said =>
                {
                    var wanted = said == "A, B, C" ? MatrixSteps.Letters : MatrixSteps.Numbers;

                    if (wanted == how.Steps) return;

                    how.Steps = wanted;
                    how.Start = wanted == MatrixSteps.Letters ? "A" : "10";
                    onChange();
                });

            var start = Ui.Text(how.Start, "10", typed =>
            {
                how.Start = (typed ?? string.Empty).Trim();
                onChange();
            }, 62);

            var step = Ui.Text(how.Step.ToString(CultureInfo.InvariantCulture), "1", typed =>
            {
                if (!int.TryParse((typed ?? string.Empty).Trim(), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out var held) || held == 0)
                    return;

                how.Step = held;
                onChange();
            }, 52);

            if (how.Steps == MatrixSteps.Letters)
                return Ui.Across(kinds, Ui.Field("Start at", start, null),
                                 Ui.Field("Step", step, null));

            var pad = Ui.Text(how.Pad == 0
                                  ? string.Empty
                                  : how.Pad.ToString(CultureInfo.InvariantCulture),
                "none", typed =>
                {
                    var said = (typed ?? string.Empty).Trim();

                    if (said.Length == 0) { how.Pad = 0; onChange(); return; }

                    if (!int.TryParse(said, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                      out var held) || held < 0 || held > 9)
                        return;

                    how.Pad = held;
                    onChange();
                }, 52);

            pad.ToolTip = "Leading zeros, so 1 reads as 01.";

            return Ui.Across(kinds, Ui.Field("Start at", start, null),
                             Ui.Field("Step", step, null), Ui.Field("Digits", pad, null));
        }

        /// <summary>The one box a same-on-all write needs.</summary>
        private static UIElement Constant(MatrixField how, Action onChange)
        {
            var box = Ui.Text(how.Same, "P2", typed =>
            {
                how.Same = (typed ?? string.Empty).Trim();
                onChange();
            }, 150);

            var held = Ui.Field("Value", box, null);

            held.HorizontalAlignment = HorizontalAlignment.Left;

            return held;
        }

        // -------------------------------------------------------------------------------
        // Small parts
        // -------------------------------------------------------------------------------

        /// <summary>A pressable chip saying what a test is on.</summary>
        private static Border Chip(string said, bool assigned)
        {
            var text = new TextBlock
            {
                Text = said,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            }.Wearing("Body");

            text.Tint(TextBlock.ForegroundProperty, assigned ? "Accent" : "Muted");

            var chip = new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = text,
                ToolTip = assigned
                    ? "On this pattern. Click to change it."
                    : "Not on a pattern. Click to put it on one.",
            };

            chip.Tint(Border.BackgroundProperty, assigned ? "AccentWash" : "Card");
            chip.Tint(Border.BorderBrushProperty, assigned ? "Accent" : "FieldBd");

            return chip;
        }

        /// <summary>One token as a clickable chip.</summary>
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

        /// <summary>A number as it is written.</summary>
        private static string Numbered(int at) => at.ToString(CultureInfo.InvariantCulture);

        /// <summary>Something in the quotes the rest of the product uses.</summary>
        private static string Quoted(string said) => "“" + said + "”";
    }
}
