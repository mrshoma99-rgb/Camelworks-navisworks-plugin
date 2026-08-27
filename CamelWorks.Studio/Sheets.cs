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

        /// <summary>Which widths to draw it at, or null for all of them.</summary>
        public int[]? Widths { get; set; }

        /// <summary>How tall the window is while it is drawn.</summary>
        public int Height { get; set; } = 1100;

        /// <summary>Build it, ignoring the capture hook. For the interactive window.</summary>
        public UIElement Build() => Builder(() => { });
    }

    /// <summary>
    /// What the harness draws besides the desks.
    ///
    /// The shell page is not a reconstruction at all: it is the shipping chrome, with the content
    /// showing what a screen does when it cannot reach Navisworks. Home and the working page are
    /// reconstructions — the real ones read the host on their first line — and the vocabulary
    /// pages are a specimen sheet, because a spacing or weight decision proved there is proved
    /// for every screen at once.
    /// </summary>
    public static class Sheets
    {
        /// <summary>Every page, in the order the harness lists them.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            new Sheet { Id = "shell", Title = "The pane", Height = 900, Builder = _ => Shell(), Widths = new[] { 470, 900 } },
            new Sheet { Id = "home", Title = "Home", Height = 1150, Builder = _ => HomePane(), Widths = new[] { 470 } },
            new Sheet { Id = "controls", Title = "Every control", Height = 2100, Builder = _ => Controls(), Widths = new[] { 470 } },
            new Sheet { Id = "bench", Title = "The anatomy", Height = 700, Builder = _ => BenchSheet(), Widths = new[] { 470, 900 } },
            new Sheet
            {
                Id = "working",
                Title = "Work in flight",
                Height = 700,
                Builder = Working,
                Widths = new[] { 640 },
                Drive = host => Click(host, "Run the comparison"),
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
                Ui.Sub("The vocabulary every surface is built from."),

                Ui.Eyebrow("Type"),
                Ui.Heading("A heading"),
                Ui.Sub("The line under it that says what a screen is for."),
                Ui.Line("An ordinary line of body text, which wraps when the pane is narrow enough that it has to."),
                Ui.Line("The same line, secondary.", 0.75),
                Ui.Small("Small print: the sentence under a control that explains what to put in it."),
                Ui.Code("D:\\Projects\\Riverside\\Models\\ARC-Riverside-Block-B-r7.nwc"),
                Ui.Rule(),

                Ui.Eyebrow("Numbers and status"),
                figures,
                pills,
                Ui.Rule(),

                Ui.Eyebrow("Fields"),
                Ui.Field("Set name", Ui.Text("Level 04 — mechanical", null, 200), "Shown in the Navisworks selection tree."),
                Ui.Field("Property tab", Ui.Choice(new[] { "Element", "Item", "Revit Type", "IFC" }, "Element", null)),
                Ui.Pair(
                    Ui.Field("Skip values under", Ui.Text("25", null, 60)),
                    Ui.Field("Start numbering at", Ui.Text("1", null, 60))),
                Ui.Field("Split on", Ui.Segmented(new[] { "Level", "Discipline", "Zone" }, "Level", null)),
                Ui.Toggle("Publish into folders as well as saving", true, null),
                Ui.Check("Include elements with no value", false, null),
                Ui.Rule(),

                Ui.Eyebrow("Buttons"),
                Ui.Across(
                    Ui.Primary("Run the comparison", () => { }),
                    Ui.Button("Save to project", () => { }),
                    Ui.Quiet("Reset", () => { }),
                    Ui.Danger("Delete baseline", () => { }),
                    Ui.Icon(Glyphs.Up, "Move up", () => { }),
                    Ui.Icon(Glyphs.Down, "Move down", () => { }),
                    Ui.Icon(Glyphs.Bin, "Remove", () => { })),
                Ui.Link("What does publishing do?", () => { }),
                Ui.Rule(),

                Ui.Eyebrow("A table"),
                Ui.Table(Rows(),
                    ("Element", 150), ("Model", 110), ("What changed", 0)));
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

        // harness: vocabulary
        //
        // The surface anatomy itself, drawn empty enough to read as a diagram: setup column, the
        // one action at its foot, the stage. At 470 the same page proves the stacked shape.
        private static UIElement BenchSheet()
        {
            var setup = Ui.Stack(
                Bench.Scope("3,120 elements in the selection"),
                Bench.Group("The inputs",
                    Ui.Field("A field", Ui.Text("its value", null, 160)),
                    Ui.Field("A choice", Ui.Choice(new[] { "This", "That" }, "This", null)),
                    Ui.Check("A yes or no", true, null)),
                Bench.Group("More of them",
                    Ui.Field("Another", Ui.Text(string.Empty, "empty, with a hint", null, 160),
                             "Under ten words, or it is prose.")));

            var stage = Ui.Stack(
                Bench.Ready("What pressing the action will do, to what.",
                            "One quieter line of consequence — computed from the live model, "
                            + "never boilerplate."),
                Ui.Rule(),
                Bench.Verdict("The answer, stated in one phrase", Tone.Good),
                Ui.Line("Everything a run produces lands here, under the verdict.", 0.75));

            return Bench.Surface(setup, Ui.Runner("The one action", Ui.Stack(), _ => { }), stage);
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

            var runner = Ui.Runner("Run the comparison", output, job =>
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

            var setup = Ui.Stack(
                Bench.Group(string.Empty,
                    Ui.Field("Compare against",
                        Ui.Choice(new[] { "Week 32 — Friday issue", "Week 30", "Week 28" },
                                  "Week 32 — Friday issue", null))),
                Bench.Group("What counts as a change",
                    Ui.Field("Position tolerance", Ui.Text("0.001", null, 80)),
                    Ui.Toggle("Read the actual triangles", false, null)));

            return Bench.Surface(setup, runner, Ui.Stack(output));
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
        /// before any other — and the real one reads the host on its very first line, so the
        /// shell page can only ever show it failing.
        /// </summary>
        private static UIElement HomePane()
        {
            var chips = Ui.Across(
                Ui.Pill("9 models"),
                Ui.Pill("Metres"),
                Ui.Pill("4 clash tests"));

            var desks = Ui.Stack();
            var label = Ui.Eyebrow("The desks");
            label.Margin = new Thickness(0, 12, 0, 4);
            desks.Children.Add(label);

            foreach (var desk in Desks.All)
                desks.Children.Add(DeskRow(desk));

            return Page(
                Ui.Stack(Ui.Eyebrow("RIVERSIDE — BLOCK B"), chips),

                Ui.Eyebrow("The week"),
                Step(1, "Project", "Reconcile", "Six models came in on Friday; two moved.",
                     "2 days ago", false, "Check the models"),
                Ui.Rule(),
                Step(2, "Coordinate", "Regroup", "1,284 clashes became 96 issues.",
                     "Yesterday", false, "Open the board"),
                Ui.Rule(),
                Step(3, "Coordinate", "Report", "Send this week's out.",
                     null, true, "Write the report"),

                Ui.Rule(),
                desks,

                Ui.Rule(),
                Ui.Eyebrow("Recent"),
                Ui.Table(Activity(), ("What", 96), ("", 0), ("When", 96)));
        }

        /// <summary>One desk on the front door: its colour, its name, and the job it is.</summary>
        private static UIElement DeskRow(Desk desk)
        {
            var marker = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 2, 9, 2),
            };

            marker.SetResourceReference(Border.BackgroundProperty, "Fam." + desk.Family);

            var text = Ui.Stack(
                Ui.Line(desk.Title, 1, true),
                Ui.Line(desk.Job, 0.7));

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(marker);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var button = new System.Windows.Controls.Button
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 2),
            };

            button.Wearing("RailRow");
            return button;
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

        private static UIElement States()
        {
            return Page(
                Ui.Heading("What a screen says when it has nothing to say"),
                Ui.Sub("Every one of these is an answer. A blank panel is a bug report."),

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
                        + "as a hidden model and takes back out again when you clear the comparison."));
        }
    }
}
