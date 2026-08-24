using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>One page the harness can draw.</summary>
    public sealed class Sheet
    {
        /// <summary>File-safe name, used for the PNG.</summary>
        public string Id { get; set; } = "sheet";

        /// <summary>How it reads in the harness's own list.</summary>
        public string Title { get; set; } = "Sheet";

        /// <summary>
        /// Build the page.
        ///
        /// The argument is "take the picture now": a page showing work in flight has to be captured
        /// from inside the work, because the moment it returns there is nothing in flight to look
        /// at. Pages with nothing moving ignore it and are captured once they have settled.
        /// </summary>
        public Func<Action, UIElement> Builder { get; set; } = _ => new Border();

        /// <summary>
        /// Something to do to the laid-out page before the picture is taken, or null.
        ///
        /// The page showing work in flight uses it to press its own button. Relying on Loaded for
        /// that did not work: the harness had finished pumping before the handler's queued click
        /// came round, and the result was a photograph of a button at rest silently standing in
        /// for a photograph of a running job.
        /// </summary>
        public Action<FrameworkElement>? Drive { get; set; }

        /// <summary>Which pane widths to draw it at, or null for all of them.</summary>
        public int[]? Widths { get; set; }

        /// <summary>How tall the window is while it is drawn.</summary>
        public int Height { get; set; } = 1100;

        /// <summary>Build it, ignoring the capture hook. For the interactive window.</summary>
        public UIElement Build() => Builder(() => { });
    }

    /// <summary>
    /// What the harness draws.
    ///
    /// Three of these are reconstructions rather than the real screens, and that is the honest
    /// trade: the real screens read the host, and there is no host here. What they are made of is
    /// real — every control below is the same <see cref="Ui"/> call the screen itself makes, on the
    /// same skin — so a spacing, weight or disclosure decision proved here is proved for all
    /// sixteen screens at once. The shell page is not a reconstruction at all: it is the shipping
    /// chrome, with the content showing what a screen does when it cannot reach Navisworks.
    /// </summary>
    public static class Sheets
    {
        /// <summary>Every page, in the order the harness lists them.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            new Sheet { Id = "shell", Title = "The pane", Height = 900, Builder = _ => Shell(), Widths = new[] { 470, 900 } },
            new Sheet { Id = "home", Title = "Home", Height = 1000, Builder = _ => HomePane(), Widths = new[] { 470 } },
            new Sheet { Id = "controls", Title = "Every control", Height = 2100, Builder = _ => Controls(), Widths = new[] { 470 } },
            new Sheet { Id = "sets", Title = "Sets", Height = 1500, Builder = _ => SetsPane(), Widths = new[] { 470, 640 } },
            new Sheet { Id = "appearance", Title = "Appearance", Height = 1200, Builder = _ => Appearance(), Widths = new[] { 470 } },
            new Sheet { Id = "takeoff", Title = "Takeoff", Height = 900, Builder = _ => Takeoff(), Widths = new[] { 470 } },
            new Sheet
            {
                Id = "working",
                Title = "Work in flight",
                Height = 700,
                Builder = Working,
                Widths = new[] { 470 },
                Drive = host => Click(host, "Compare"),
            },
            new Sheet { Id = "states", Title = "Empty, wrong, done", Height = 900, Builder = _ => States(), Widths = new[] { 470 } },
        };

        private static UIElement Shell() => new ShellView(ShellView.MainPane);

        internal static UIElement Page(params UIElement?[] children)
        {
            var stack = Ui.Stack(children);
            return new Border { Padding = new Thickness(12, 12, 12, 16), Child = stack };
        }

        // -----------------------------------------------------------------------------------

        // harness: vocabulary
        //
        // A specimen sheet of every control, not a reconstruction of a panel. Its captions are
        // invented to show the weights side by side — "Run the comparison" next to "Reset" next
        // to "Delete baseline" — so the drift check leaves them alone.
        private static UIElement Controls()
        {
            var pills = Ui.Across(
                Ui.Pill("232 elements"),
                Ui.Pill("Resolved", Tone.Good),
                Ui.Pill("Carried over", Tone.Warn),
                Ui.Pill("New this week", Tone.Bad));

            var figures = Ui.Across(
                Ui.Figure("1,284", "clashes"),
                Ui.Figure("312", "resolved", Tone.Good),
                Ui.Figure("47", "new", Tone.Bad),
                Ui.Figure("9", "models"));

            return Page(
                Ui.Heading("Every control, in one place"),
                Ui.Sub("The vocabulary all sixteen screens are built from."),

                Ui.Marked(Glyphs.Report, "Type", null,
                    Ui.Eyebrow("Eyebrow"),
                    Ui.Heading("A card heading"),
                    Ui.Sub("The line under it that says what the card is for."),
                    Ui.Line("An ordinary line of body text, which wraps when the pane is narrow enough that it has to."),
                    Ui.Line("The same line, secondary.", 0.75),
                    Ui.Small("Small print: the sentence under a control that explains what to put in it."),
                    Ui.Code("D:\\Projects\\Riverside\\Models\\ARC-Riverside-Block-B-r7.nwc")),

                Ui.Marked(Glyphs.Table, "Numbers and status", null, figures, pills),

                Ui.Marked(Glyphs.Tune, "Fields", null,
                    Ui.Form(
                    Ui.Field("Set name", Ui.Text("Level 04 — mechanical", null, 200), "Shown in the Navisworks selection tree."),
                    Ui.Field("Property tab", Ui.Choice(new[] { "Element", "Item", "Revit Type", "IFC" }, "Element", null)),
                    Ui.Pair(
                        Ui.Field("Skip values under", Ui.Text("25", null, 60)),
                        Ui.Field("Start numbering at", Ui.Text("1", null, 60))),
                    Ui.Field("Split on", Ui.Segmented(new[] { "Level", "Discipline", "Zone" }, "Level", null)),
                    Ui.Toggle("Publish into folders as well as saving", true, null),
                    Ui.Check("Include elements with no value", false, null))),

                Ui.Marked(Glyphs.Play, "Buttons", null,
                    Ui.Across(
                        Ui.Primary("Run the comparison", () => { }),
                        Ui.Button("Save to project", () => { }),
                        Ui.Quiet("Reset", () => { }),
                        Ui.Danger("Delete baseline", () => { }),
                        Ui.Icon(Glyphs.Up, "Move up", () => { }),
                        Ui.Icon(Glyphs.Down, "Move down", () => { }),
                        Ui.Icon(Glyphs.Bin, "Remove", () => { })),
                    Ui.Link("What does publishing do?", () => { })),

                Ui.Section("Advanced matching", "Tolerance 1 mm · property watch: standard", false,
                    Ui.Field("Position tolerance", Ui.Text("0.001", null, 80), "In metres. Anything moved by less than this counts as unchanged."),
                    Ui.Field("Property watch", Ui.Segmented(new[] { "None", "Standard", "All" }, "Standard", null)),
                    Ui.Toggle("Read the actual triangles", false, null),
                    Ui.Small("Slow on a large federation, and the only way to notice an element that was remodelled in place.")),

                Ui.Marked(Glyphs.Table, "A table", null,
                    Ui.Table(Rows(),
                        ("Element", 150), ("Model", 110), ("What changed", 0))));
        }

        private static IEnumerable<TableRow> Rows()
        {
            yield return new TableRow(null, "Basic Wall: 200 blockwork", "ARC r7", "Moved 340 mm east") { Tone = Tone.Warn };
            yield return new TableRow(null, "Rectangular Duct 400x250", "MEP r4", "Added");
            yield return new TableRow(null, "Pipe Type: chilled flow", "MEP r4", "Removed") { Tone = Tone.Bad };
            yield return new TableRow(null, "Structural Column UC254", "STR r2", "Type changed");
            yield return new TableRow(null, "Floor: 150 RC slab", "STR r2", "Resized") { Tone = Tone.Warn };
            yield return new TableRow(null, "Door: single leaf 926", "ARC r7", "Renamed");
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The Sets screen, rebuilt.
        ///
        /// This is the pane the redesign was called for: a rule builder and two generators, each
        /// with every field it owns on screen at once, one under another, all reading as one
        /// undifferentiated column of labels and boxes. Folded up, with each section saying what it
        /// is set to, the same screen is four lines until you want more.
        /// </summary>
        private static UIElement SetsPane()
        {
            var conditions = Ui.Stack(
                Row("Category", "is", "Walls"),
                Row("Level", "is", "Level 04"),
                Ui.Across(
                    Ui.Quiet("+ and...", () => { }),
                    Ui.Quiet("+ Add an OR group", () => { })));

            return Page(
                Ui.Form(
                    conditions,
                    Ui.Rule(),
                    Ui.Field("Name", Ui.Text("Level 04 — walls", null, 220),
                             "how it reads in the Navisworks selection tree")),

                Ui.Actions(
                    Ui.Primary("Run", () => { }),
                    Ui.Button("Save to project", () => { }),
                    Ui.Button("Publish as a Navisworks set", () => { })),

                Ui.Done("Saved \"Level 04 — walls\" to the project file."),

                Ui.Small("Groups are ORed together; conditions inside a group are ANDed. A negated "
                         + "condition compiles to a subtraction rather than a not-equals — those are "
                         + "different sets, because a host condition on a property an element does "
                         + "not have matches nothing."),

                Ui.Eyebrow("The whole library at once"),
                Ui.Small("The Selection Tree is the shape the model was exported in, not the shape "
                         + "the job is run in. Name the properties that describe the project - "
                         + "level, discipline, system, package - and it builds the library to match, "
                         + "keeps the recipe, and tells you what moved when the next revision lands."),
                Ui.Actions(Ui.Button("Rebuild the tree from properties...", () => { })),

                Ui.Rule(),

                Ui.Eyebrow("Many sets at once"),
                Ui.Small("Already have the sets as a spreadsheet - a row per condition, or a row per "
                         + "set with a column per property? Read it rather than retyping it, and run "
                         + "the same schedule again when the next revision lands."),
                Ui.Actions(Ui.Button("Open a schedule from a spreadsheet...", () => { })),

                Ui.Rule(),

                Ui.Eyebrow("Saved sets"),
                Ui.Small("Kept in the project file, so they travel with the job."),
                Ui.Table(Saved(), ("Set", 200), ("Elements", 80), ("Last run", 0)));
        }

        private static UIElement Row(string property, string test, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Add(grid, 0, Ui.Text(property, null, 60));
            Add(grid, 2, Ui.Choice(new[] { "is", "is not", "contains" }, test, null, 60));
            Add(grid, 4, Ui.Text(value, null, 60));
            Add(grid, 5, Ui.Icon(Glyphs.Close, "Remove this condition", () => { }));
            return grid;
        }

        internal static void Add(Grid grid, int column, UIElement child)
        {
            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        private static IEnumerable<TableRow> Saved()
        {
            yield return new TableRow(null, "Level 04 — walls", "412", "12 minutes ago");
            yield return new TableRow(null, "MEP — chilled water", "1,905", "Yesterday");
            yield return new TableRow(null, "Structure — transfer level", "88", "Last Tuesday");
        }

        // -----------------------------------------------------------------------------------

        private static UIElement Appearance()
        {
            return Page(
                Ui.Eyebrow("Top of the list wins"),
                Layer("Clashing elements", "Red, opaque", Color.FromRgb(0xC8, 0x10, 0x2E)),
                Layer("Level 04 only", "Everything else 85% transparent", Color.FromRgb(0x9A, 0xA3, 0xAD)),
                Layer("Temporary works", "Hidden", Color.FromRgb(0xB4, 0xBC, 0xC5)),

                Ui.Actions(
                    Ui.Primary("Apply the stack", () => { }),
                    Ui.Button("Save the stack", () => { }),
                    Ui.Button("Put the model back", () => { })),

                Ui.Info("A layer only paints what it covers. Elements no layer covers keep whatever "
                        + "Navisworks already had for them."),

                Ui.Section("Add a layer", "hidden or coloured, over whatever you point it at", false,
                    Ui.Form(
                        Ui.Pair(
                            Ui.Field("Name", Ui.Text(string.Empty, "Level 04 only", null, 160)),
                            Ui.Field("Covers", Ui.Choice(new[] { "The selection", "A saved set", "A search" }, "The selection", null))),
                        Ui.Pair(
                            Ui.Field("Visibility", Ui.Choice(new[] { "Leave it", "Hide it", "Show it" }, "Leave it", null)),
                            Ui.Field("Colour (#rrggbb)", Ui.Text(string.Empty, "#c8102e", null, 110))),
                        Ui.Pair(
                            Ui.Field("Transparency (0-1)", Ui.Text(string.Empty, "0.85", null, 90)),
                            Ui.Field("Why", Ui.Text(string.Empty, "for the Tuesday walkthrough", null, 180)))),
                    Ui.Actions(Ui.Button("Add layer", () => { }))),

                Ui.Rule(),

                Ui.Marked(Glyphs.Find, "What else is going on",
                    "Everything hidden or overridden that no layer above accounts for. Reading it is "
                    + "a pass over the whole federation, so it happens when you ask rather than when "
                    + "the tab opens.",
                    Ui.Actions(Ui.Primary("Scan the model", () => { }))));
        }

        private static UIElement Layer(string name, string what, Color paint)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The literal colour the layer writes, not a Tone. Skin.Ink is a text token and its
            // three values are the severity palette, so a layer set to red used to show salmon in
            // the dark theme and a layer with no colour showed as near-white — a preview of the
            // pane rather than of the model.
            var swatch = new Border
            {
                Width = 10,
                Height = 34,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(paint),
                Margin = new Thickness(0, 0, 10, 0),
            };

            grid.Children.Add(swatch);

            var text = Ui.Stack(
                Ui.Line(name, 1, true),
                Ui.Small(what));

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Ui.Icon(Glyphs.Up, "Move up", () => { }));
            buttons.Children.Add(Ui.Icon(Glyphs.Down, "Move down", () => { }));
            buttons.Children.Add(Ui.Icon(Glyphs.Bin, "Remove this layer", () => { }));

            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            var frame = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 6, 0, 6),
                Child = grid,
            };

            frame.SetResourceReference(Border.BorderBrushProperty, "Bd");
            return frame;
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A run, caught in the middle of itself.
        ///
        /// The complaint this whole redesign started from was that Navisworks simply freezes: no
        /// bar, no count, no way to stop. This page exists so that claim can be checked against a
        /// picture rather than argued about — it starts a real <see cref="Ui.Runner"/> and takes
        /// the screenshot from inside the work, with the bar part-way and Stop live.
        /// </summary>
        private static UIElement Working(Action shoot)
        {
            var output = Ui.Stack();

            var runner = Ui.Runner("Compare", output, job =>
            {
                job.Total = 128400;

                // Paced, because Job repaints a few times a second rather than on every call, and
                // a loop that finishes in microseconds paints once and photographs "0 of 128,400".
                // Real work has time between its steps; so does this.
                for (var done = 0; done <= 54000; done += 1500)
                {
                    if (!job.Step(done, "elements")) return;
                    System.Threading.Thread.Sleep(12);
                }

                // Captured here, not after: once this returns there is no run to photograph.
                Program.Log("  work reached 54,000 of 128,400; taking the picture from inside it");
                shoot();
            });

            return Page(
                Ui.Marked(Glyphs.Compare, "Run", "Pick a baseline and see what changed.",
                    Ui.Form(
                    Ui.Field("Baseline", Ui.Choice(new[] { "Week 32 — Friday issue", "Week 30", "Week 28" }, "Week 32 — Friday issue", null))),
                    Ui.Section("What counts as a change", "1 mm · standard properties · no triangle read", false,
                        Ui.Field("Position tolerance", Ui.Text("0.001", null, 80)),
                        Ui.Toggle("Read the actual triangles", false, null)),
                    runner,
                    output));
        }

        private static void Click(DependencyObject root, string caption)
        {
            var button = Find(root, caption);

            if (button == null)
            {
                Program.Log("  could not find the button named " + caption + "; the page will be "
                            + "photographed at rest, which is not what it is for");
                return;
            }

            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        private static System.Windows.Controls.Button? Find(DependencyObject root, string caption)
        {
            if (root is System.Windows.Controls.Button button
                && button.Content is string text
                && string.Equals(text, caption, StringComparison.Ordinal))
                return button;

            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (!(child is DependencyObject node)) continue;

                var found = Find(node, caption);
                if (found != null) return found;
            }

            return null;
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The front door, reconstructed.
        ///
        /// Worth having as a page of its own because it is the screen a first-time user sees
        /// before any other, and until now it was the one screen nobody had ever looked at: it
        /// reads the host on its very first line, so the shell page can only ever show it failing.
        /// </summary>
        private static UIElement HomePane()
        {
            var chips = Ui.Across(
                Ui.Pill("9 models"),
                Ui.Pill("Metres"),
                Ui.Pill("4 clash tests"));

            return Page(
                Ui.Stack(Ui.Eyebrow("RIVERSIDE — BLOCK B"), chips),

                Ui.Marked(Glyphs.Clock, "The week",
                    "Reconcile, regroup, report. Each opens where the work actually happens.",
                    Step(1, "Project", "Reconcile", "Six models came in on Friday; two moved.",
                         "2 days ago", false, "Check the models"),
                    Ui.Rule(),
                    Step(2, "Coordinate", "Regroup", "1,284 clashes became 96 issues.",
                         "Yesterday", false, "Open the board"),
                    Ui.Rule(),
                    Step(3, "Coordinate", "Report", "Send this week's out.",
                         null, true, "Write the report")),

                Ui.Marked(Glyphs.Report, "Recent", null,
                    Ui.Table(Activity(), ("What", 96), ("", 0), ("When", 96))));
        }

        private static UIElement Step(int number, string family, string title, string what,
                                      string? when, bool lead, string caption)
        {
            var badge = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 0, 0),
                Child = new TextBlock
                {
                    Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            badge.SetResourceReference(Border.BackgroundProperty, "Fam." + family);

            var line = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            line.Children.Add(badge);

            var text = Ui.Stack(
                Ui.Line(title, 1, true),
                Ui.Line(what, 0.75),
                Ui.Across(when == null ? Ui.Pill("Not done yet", Tone.Warn) : Ui.Pill(when, Tone.Good)));

            text.Margin = new Thickness(11, 0, 10, 0);
            Grid.SetColumn(text, 1);
            line.Children.Add(text);

            var go = lead
                ? (System.Windows.Controls.Button)Ui.Primary(caption, () => { })
                : Ui.Button(caption, () => { });

            go.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(go, 2);
            line.Children.Add(go);

            return line;
        }

        private static IEnumerable<TableRow> Activity()
        {
            yield return new TableRow(null, "Report", "Issued 96 issues to 4 parties", "Yesterday");
            yield return new TableRow(null, "Regroup", "Grouped 1,284 clashes into 96", "Yesterday");
            yield return new TableRow(null, "Reconcile", "Brought in 6 models", "2 days ago");
        }

        /// <summary>
        /// Takeoff, reconstructed.
        ///
        /// Here because it is the screen most at risk of still reading as a form: four settings, a
        /// folded group of two more, and one button. If the card, the well and the pairing do not
        /// rescue this one they have not rescued anything.
        /// </summary>
        private static UIElement Takeoff()
        {
            return Page(
                Ui.Form(
                    Ui.Field("Scope", Ui.Segmented(new[] { "Whole model", "Selection", "A saved set" }, "Whole model", null)),
                    Ui.Pair(
                        Ui.Field("Measure", Ui.Choice(new[] { "Volume", "Area", "Length", "Count" }, "Volume", null)),
                        Ui.Field("Rows", Ui.Choice(new[] { "Level", "Category", "Model", "Discipline" }, "Level", null))),
                    Ui.Field("Columns", Ui.Text(string.Empty, "nothing — a flat list", null, 140),
                             "Leave it to get a flat list.")),

                Ui.Actions(Ui.Primary("Sum it", () => { })),

                Ui.Section("If the values need tidying first", "Off — values read exactly as they are", false,
                    Ui.Field("Take the number matching", Ui.Text(string.Empty, null, 200)),
                    Ui.Field("When it has no unit, read it as", Ui.Choice(new[] { "leave it", "m3", "m2", "m" }, "leave it", null))),

                Ui.Marked(Glyphs.Table, "1,178.5 m³", "Volume by level, across 3,412 elements",
                    Ui.Across(
                        Ui.Figure("1,178.5", "m³ total"),
                        Ui.Figure("3,412", "elements"),
                        Ui.Figure("17", "unreadable", Tone.Warn)),
                    Ui.Table(Sums(), ("Level", 130), ("Volume", 0), ("Elements", 80)),
                    Ui.Tally(4, 7, "level"),
                    Ui.Actions(Ui.Quiet("Write it to a spreadsheet", () => { }))));
        }

        private static IEnumerable<TableRow> Sums()
        {
            yield return new TableRow(null, "Level 00", "412.8 m³", "1,204");
            yield return new TableRow(null, "Level 01", "388.1 m³", "1,118");
            yield return new TableRow(null, "Level 02", "377.6 m³", "1,090");
            yield return new TableRow(null, "Unreadable", "—", "17") { Tone = Tone.Warn };
        }

        private static UIElement States()
        {
            return Page(
                Ui.Heading("What a screen says when it has nothing to say"),
                Ui.Sub("Every one of these is an answer. A blank panel is a bug report."),

                Ui.Marked(Glyphs.Info, "States", null,
                    Ui.Empty("No clashes matched these rules",
                             "Every clash in the four tests was suppressed by a rule.",
                             "Clear the rules", () => { }),
                    Ui.Note("This model has no saved viewpoints yet, so there is nothing to rename. "
                            + "Save one first, or run this after a coordination session."),
                    Ui.Problem("The project file is open in another Navisworks. CamelWorks will not "
                               + "write over somebody else's work; close it there, or carry on and export "
                               + "without saving."),
                    Ui.Done("Published 46 sets into 6 folders. They are in the Sets window now."),
                    Ui.Info("Removed elements are drawn from the baseline model, which CamelWorks appends "
                            + "as a hidden model and takes back out again when you clear the comparison.")));
        }
    }
}
