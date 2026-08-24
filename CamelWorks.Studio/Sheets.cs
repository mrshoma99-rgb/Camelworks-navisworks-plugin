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
            new Sheet { Id = "controls", Title = "Every control", Height = 2100, Builder = _ => Controls(), Widths = new[] { 470 } },
            new Sheet { Id = "sets", Title = "Sets", Height = 1500, Builder = _ => SetsPane(), Widths = new[] { 470, 640 } },
            new Sheet { Id = "appearance", Title = "Appearance", Height = 1200, Builder = _ => Appearance(), Widths = new[] { 470 } },
            new Sheet { Id = "working", Title = "Work in flight", Height = 700, Builder = Working, Widths = new[] { 470 } },
            new Sheet { Id = "states", Title = "Empty, wrong, done", Height = 900, Builder = _ => States(), Widths = new[] { 470 } },
        };

        private static UIElement Shell() => new ShellView(Workspaces.MainPane);

        private static UIElement Page(params UIElement?[] children)
        {
            var stack = Ui.Stack(children);
            return new Border { Padding = new Thickness(12, 12, 12, 16), Child = stack };
        }

        // -----------------------------------------------------------------------------------

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

                Ui.Card("Type", null,
                    Ui.Eyebrow("Eyebrow"),
                    Ui.Heading("A card heading"),
                    Ui.Sub("The line under it that says what the card is for."),
                    Ui.Line("An ordinary line of body text, which wraps when the pane is narrow enough that it has to."),
                    Ui.Line("The same line, secondary.", 0.75),
                    Ui.Small("Small print: the sentence under a control that explains what to put in it."),
                    Ui.Code("D:\\Projects\\Riverside\\Models\\ARC-Riverside-Block-B-r7.nwc")),

                Ui.Card("Numbers and status", null, figures, pills),

                Ui.Card("Fields", null,
                    Ui.Field("Set name", Ui.Text("Level 04 — mechanical", null, 200), "Shown in the Navisworks selection tree."),
                    Ui.Field("Property tab", Ui.Choice(new[] { "Element", "Item", "Revit Type", "IFC" }, "Element", null)),
                    Ui.Pair(
                        Ui.Field("Skip values under", Ui.Text("25", null, 60)),
                        Ui.Field("Start numbering at", Ui.Text("1", null, 60))),
                    Ui.Field("Split on", Ui.Segmented(new[] { "Level", "Discipline", "Zone" }, "Level", null)),
                    Ui.Toggle("Publish into folders as well as saving", true, null),
                    Ui.Check("Include elements with no value", false, null)),

                Ui.Card("Buttons", null,
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

                Ui.Card("A table", null,
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
                    Ui.Quiet("and...", () => { }),
                    Ui.Quiet("Add an OR group", () => { })));

            return Page(
                Ui.Heading("Sets"),
                Ui.Sub("Boolean set building, compiled down to native Navisworks search sets."),

                Ui.Card("Rule", null,
                    conditions,
                    Ui.Field("Name", Ui.Text("Level 04 — walls", null, 220)),
                    Ui.Actions(
                        Ui.Primary("Run", () => { }),
                        Ui.Button("Save to project", () => { }),
                        Ui.Quiet("Publish as a Navisworks set", () => { }))),

                Ui.Section("Make a family of sets", "One set per Level, named {value}, skipping under 1", false,
                    Ui.Field("Scope", Ui.Segmented(new[] { "Whole model", "Selection", "A saved set" }, "Whole model", null)),
                    Ui.Pair(
                        Ui.Field("Property tab", Ui.Text("Element", null, 120)),
                        Ui.Field("Property", Ui.Text("Level", null, 120))),
                    Ui.Field("Name pattern", Ui.Text("{value}", "{value} — walls", null, 180),
                             "{value} becomes the property's value. Everything else is kept as typed."),
                    Ui.Field("Skip values under", Ui.Text("1", null, 60), "Values with fewer elements than this make no set."),
                    Ui.Actions(Ui.Primary("See what it would make", () => { }))),

                Ui.Section("Make a whole library, in folders", "Level, then Discipline · 1 element minimum", false,
                    Ui.Field("Scope", Ui.Segmented(new[] { "Whole model", "Selection", "A saved set" }, "Whole model", null)),
                    Ui.Field("Property tab", Ui.Text("Element", null, 120)),
                    Ui.Field("Split on", Ui.Text("Level", null, 140)),
                    Ui.Field("Then on", Ui.Text("Discipline", null, 140)),
                    Ui.Field("Then on", Ui.Text(string.Empty, "nothing", null, 140)),
                    Ui.Field("Set name", Ui.Text("{value}", null, 160)),
                    Ui.Field("Skip branches under", Ui.Text("1", null, 60)),
                    Ui.Actions(Ui.Primary("See the library it would make", () => { }))),

                Ui.Card("Saved sets", "Kept in the project file, so they travel with the job.",
                    Ui.Table(Saved(), ("Set", 200), ("Elements", 80), ("Last run", 0))));
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

        private static void Add(Grid grid, int column, UIElement child)
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
                Ui.Heading("Appearance"),
                Ui.Sub("What is hidden, what is overridden, by which layer."),

                Ui.Card("Layers", "Top of the list wins.",
                    Layer("Clashing elements", "Red, opaque", Tone.Bad),
                    Layer("Level 04 only", "Everything else 85% transparent", Tone.Plain),
                    Layer("Temporary works", "Hidden", Tone.Warn),
                    Ui.Across(Ui.Quiet("Add layer", () => { }))),

                Ui.Actions(
                    Ui.Primary("Apply the stack", () => { }),
                    Ui.Button("Save the stack", () => { }),
                    Ui.Quiet("Show everything", () => { })),

                Ui.Section("What else is going on", "Not scanned yet", false,
                    Ui.Line("Navisworks keeps colour, transparency and visibility of its own, set by "
                            + "Appearance Profiler, by a saved viewpoint, or by hand. Scanning finds it "
                            + "so the stack is not fighting something invisible."),
                    Ui.Actions(Ui.Primary("Scan the model", () => { }))),

                Ui.Info("A layer only paints what it covers. Elements no layer covers keep whatever "
                        + "Navisworks already had for them."));
        }

        private static UIElement Layer(string name, string what, Tone tone)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border
            {
                Width = 10,
                Height = 34,
                CornerRadius = new CornerRadius(3),
                Background = Skin.Ink(tone),
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

            var runner = Ui.Runner("Compare with the baseline", output, job =>
            {
                job.Total = 128400;

                for (var done = 0; done <= 54000; done += 1500)
                {
                    if (!job.Step(done, "elements")) return;
                }

                // Captured here, not after: once this returns there is no run to photograph.
                Program.Log("  work reached 54,000 of 128,400; taking the picture from inside it");
                shoot();
            });

            var page = Page(
                Ui.Heading("Compare"),
                Ui.Sub("Pick a baseline and see what changed."),
                Ui.Card("Run", null,
                    Ui.Field("Baseline", Ui.Choice(new[] { "Week 32 — Friday issue", "Week 30", "Week 28" }, "Week 32 — Friday issue", null)),
                    Ui.Section("What counts as a change", "1 mm · standard properties · no triangle read", false,
                        Ui.Field("Position tolerance", Ui.Text("0.001", null, 80)),
                        Ui.Toggle("Read the actual triangles", false, null)),
                    runner,
                    output));

            // Started once the tree is up, at a priority below layout, so the picture is of a run
            // that is genuinely in flight rather than of a button that has not been drawn yet.
            if (page is FrameworkElement element)
                element.Loaded += (s, e) => element.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => Click(runner, "Compare with the baseline")));

            return page;
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

        private static UIElement States()
        {
            return Page(
                Ui.Heading("What a screen says when it has nothing to say"),
                Ui.Sub("Every one of these is an answer. A blank panel is a bug report."),

                Ui.Card("States", null,
                    Ui.Empty("No clashes matched these rules",
                             "Every clash in the four tests was suppressed by a rule. Loosen one, or clear the rules to see the raw count."),
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
