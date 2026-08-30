using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelWorks.UI.Shell;
using CamelWorks.UI.Views;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The apps, reconstructed.
    ///
    /// One page per app worth looking at, each drawn as the app window draws it — the title
    /// strip with the app's accent, the flat tab strip read from the real <see cref="Apps"/>
    /// registry, and a flagship tab built to the anatomy in <c>CAMELWORKS_DESIGN.md</c>: the
    /// data filling the window, the options on the right, the one action bottom-right.
    ///
    /// Same rule as ever: same captions, same weights, plausible data where the real surface
    /// reads the host. Where a reconstruction and its surface disagree the reconstruction is
    /// wrong, and fixing it is part of changing the surface.
    /// </summary>
    public static class Panels
    {
        /// <summary>Every desk page, in ribbon order.</summary>
        /// <summary>Every app page, in ribbon order.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Page("app-clash-manage", "Clash Manager · Manage", 980, Coordination),
            Page("app-issue-register", "Issue Manager · Issues", 780, Issues),
            Page("app-audit", "Model Audit · Health", 780, Federation),
            Page("app-data", "Data Manager · Properties", 780, Data),
            Page("app-scenes-appearance", "Scene Kit · Appearance", 780, Appearance),
            Page("app-scenes-viewpoints", "Scene Kit · Viewpoints", 780, Scenes),
            Page("app-automate-jobs", "Automate · Jobs", 780, Deliver),
        };

        private static Sheet Page(string id, string title, int height, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { 1180 } };

        // -----------------------------------------------------------------------------------
        // The desk chrome
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// An app window's content: title strip, tab strip, and the tab's surface.
        ///
        /// The strip is read from the shipping registry rather than restated, so a tab renamed or
        /// moved changes here by itself — the one honest way to keep a picture true.
        /// </summary>
        private static UIElement Deck(string appId, string toolId, UIElement bench)
        {
            var app = Apps.Find(appId)!;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Header(app));

            var strip = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var tab in app.Tabs)
            {
                var current = string.Equals(tab.ToolId, toolId, StringComparison.OrdinalIgnoreCase);

                var label = new TextBlock
                {
                    Text = tab.Title,
                    FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = new Thickness(12, 8, 12, 7),
                };

                label.SetResourceReference(TextBlock.ForegroundProperty, current ? "Text" : "Muted");

                var mark = new Border { Height = 2, Background = Brushes.Transparent };
                if (current) mark.SetResourceReference(Border.BackgroundProperty, "Fam." + app.Family);

                var face = new Grid();
                face.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                face.Children.Add(label);
                Grid.SetRow(mark, 1);
                face.Children.Add(mark);

                var button = new Button { Content = face, Padding = new Thickness(0) };
                button.Wearing("RailRow");
                strip.Children.Add(button);
            }

            var tabs = new Border
            {
                Padding = new Thickness(10, 0, 10, 0),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = strip,
            };

            tabs.SetResourceReference(Border.BackgroundProperty, "Pane");
            tabs.SetResourceReference(Border.BorderBrushProperty, "Bd");
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            Grid.SetRow(bench, 2);
            root.Children.Add(bench);

            var ground = new Border { Child = root };
            ground.SetResourceReference(Border.BackgroundProperty, "Bg");
            return ground;
        }

        private static UIElement Header(App app)
        {
            var titles = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 10, 16, 9),
            };

            var name = new TextBlock
            {
                Text = app.Title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            titles.Children.Add(name);

            var suite = new TextBlock
            {
                Text = "CamelWorks",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            suite.Wearing("Hint");
            titles.Children.Add(suite);

            var bar = new Border { Child = titles, BorderThickness = new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "Nav");
            bar.SetResourceReference(Border.BorderBrushProperty, "Bd");

            var accent = new Border { Height = 2 };
            accent.SetResourceReference(Border.BackgroundProperty, "Fam." + app.Family);

            var header = new Grid();
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.Children.Add(bar);
            Grid.SetRow(accent, 1);
            header.Children.Add(accent);

            return header;
        }

        // -----------------------------------------------------------------------------------
        // Federation — Health Check on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Federation()
        {
            var setup = Ui.Stack(
                Bench.Group("The rules",
                    Ui.Field("Rules from", Ui.Choice(
                        new[] { "This project's rules", "An IDS file" }, "An IDS file", null, 200),
                        "this project's own, or a file somebody sent"),
                    Ui.PathField("An .ids file", Ui.Text(string.Empty, "none — built-in rules only",
                                                         null, 200), PathKind.Open),
                    Ui.Field("Check over", Ui.Choice(
                        new[] { "Everything loaded", "What is selected", "Level 3 Ductwork" },
                        "Everything loaded", null, 200),
                        "the whole federation, or one part of it"),
                    Ui.Runner("Check the rules", Ui.Stack(), _ => { }, primary: false),
                    Ui.Quiet("Write IDS...", () => { })));

            var stage = Ui.Stack(
                Bench.Verdict("Watch — two models disagree about where the site is", Tone.Warn),
                Ui.Across(
                    Ui.Figure("9", "models"),
                    Ui.Figure("2", "adrift", Tone.Warn),
                    Ui.Figure("41,880", "elements"),
                    Ui.Figure("96%", "coded", Tone.Good)),
                Ui.Finding(true, "Placement", "STR-Block-B sits 340 mm east of where it arrived",
                           "Ask structures whether the move was meant before anything is coordinated against it."),
                Ui.Finding(false, "Codes", "412 elements carry a value Uniclass does not list",
                           "Most are one system with a typo in its family; the table below names them."));

            return Deck("audit", "health",
                Bench.Surface(setup, Ui.Runner("Check this federation", Ui.Stack(), _ => { }), stage));
        }

        // -----------------------------------------------------------------------------------
        // Coordination — the clash results bench
        // -----------------------------------------------------------------------------------

        private static UIElement Coordination()
        {
            var strip = new WrapPanel();

            strip.Children.Add(Ui.Icon(Glyphs.Find, "Search both tables", () => { }));
            strip.Children.Add(Ui.Text(string.Empty, "test, result, item, assignee or comment", null, 260));
            strip.Children.Add(new Border { Width = 8 });
            strip.Children.Add(Ui.Segmented(new[] { "Both", "Tests", "Results" }, "Both", null));
            strip.Children.Add(new Border { Width = 16 });

            foreach (var status in new[] { "New", "Active", "Reviewed", "Approved", "Resolved" })
            {
                strip.Children.Add(new Border { Width = 10 });
                strip.Children.Add(Ui.Check(status, status != "Resolved", null));
            }

            var testTools = new WrapPanel();
            testTools.Children.Add(Ui.Small("1 picked"));

            foreach (var (glyph, tip) in new[]
                     {
                         (Glyphs.Play, "Run"), (Glyphs.Pencil, "Rename"),
                         (Glyphs.Number, "Renumber the results"), (Glyphs.MoveIn, "Move to a folder"),
                         (Glyphs.Compact, "Compact"), (Glyphs.Bin, "Delete"), (Glyphs.Copy, "Copy"),
                     })
                testTools.Children.Add(Ui.Icon(glyph, tip, () => { }));

            var resultTools = new WrapPanel();
            resultTools.Children.Add(Ui.Small("3 picked"));

            foreach (var (glyph, tip) in new[]
                     {
                         (Glyphs.Focus, "Focus"), (Glyphs.Eye, "Isolate"),
                         (Glyphs.EyeOff, "Show everything again"), (Glyphs.Tick, "Set the status"),
                         (Glyphs.Person, "Assign"), (Glyphs.Comment, "Comment"),
                         (Glyphs.Group, "Group"), (Glyphs.Sphere, "Marker sphere"),
                         (Glyphs.Camera, "Snapshot"), (Glyphs.Issue, "Create an issue"),
                         (Glyphs.Copy, "Copy"),
                     })
                resultTools.Children.Add(Ui.Icon(glyph, tip, () => { }));

            var left = Bench.Pane("Tests", Ui.Small("160 tests, 18,844 results"),
                Ui.Table(TestRows(), ("Test", 200), ("Folder", 80), ("Results", 65),
                         ("New · active · closed", 0)),
                testTools);

            var right = Bench.Pane("Results", Ui.Small("of 1 picked test — 218 shown"),
                Ui.Table(ResultRows(), ("Result", 110), ("Status", 70), ("Worst", 65),
                         ("Assigned", 85), ("Item 1", 110), ("Item 2", 0)),
                resultTools);

            var foot = new WrapPanel();
            foot.Children.Add(Ui.Runner("Read the results", Ui.Stack(), _ => { }));
            foot.Children.Add(new Border { Width = 14 });

            foreach (var (glyph, tip) in new[]
                     {
                         (Glyphs.Read, "Source"), (Glyphs.Filter, "Filters"),
                         (Glyphs.Columns, "Columns and grouping"),
                         (Glyphs.Person, "Status and assignment"), (Glyphs.Sphere, "Markers"),
                         (Glyphs.Round, "The review round"), (Glyphs.More, "Beyond the engine"),
                     })
                foot.Children.Add(Ui.Icon(glyph, tip, () => { }));

            return Deck("clash", "manage", Bench.Split(strip, left, right, foot));
        }

        private static IEnumerable<TableRow> ResultRows()
        {
            yield return new TableRow(null, "Clash119", "Active", "315 mm", "Mechanical", "Basic Wall", "BOX 300x200x8") { Tone = Tone.Warn };
            yield return new TableRow(null, "Clash120", "New", "300 mm", "—", "Basic Wall", "BOX 200x200x8") { Tone = Tone.Bad };
            yield return new TableRow(null, "Clash121", "Reviewed", "250 mm", "Structures", "HANDRAIL", "UB 305x165");
            yield return new TableRow(null, "Clash122", "Approved", "222 mm", "Structures", "Concrete-Rectangular-Beam", "Duct 600x400") { Tone = Tone.Good };
        }

        private static IEnumerable<TableRow> TestRows()
        {
            yield return new TableRow(null, "ARC vs STR", "Week 33", "218", "Yesterday");
            yield return new TableRow(null, "ARC vs MEP", "Week 33", "604", "Yesterday");
            yield return new TableRow(null, "STR vs MEP", "Week 33", "462", "Yesterday");
            yield return new TableRow(null, "MEP vs MEP", "", "—", "never run") { Tone = Tone.Warn };
        }

        // -----------------------------------------------------------------------------------
        // Data — the Data Manager's shape, at desk width
        // -----------------------------------------------------------------------------------

        private static UIElement Data()
        {
            var setup = Ui.Stack(
                Bench.Group(string.Empty,
                    Ui.Field("Scope", Ui.Choice(new[] { "Current selection", "Everything loaded" },
                                                "Current selection", null))),
                Bench.Group("Write something",
                    Ui.Small("Read first; every writer previews as a diff before it touches the model.")));

            var stage = Ui.Stack(
                Ui.Across(
                    Ui.Figure("232", "elements"),
                    Ui.Figure("41", "properties"),
                    Ui.Figure("3", "part-filled", Tone.Warn)),
                Ui.Table(PropertyRows(), ("Property", 180), ("Tab", 100), ("Carried by", 110), ("Looks like", 0)),
                Ui.Eyebrow("A typed value"),
                Ui.Commit("Show me what it would write", "Write", "element",
                          (job, into) => 0, job => { }));

            return Deck("data", "data",
                Bench.Surface(setup, Ui.Runner("Read properties", Ui.Stack(), _ => { }), stage));
        }

        private static IEnumerable<TableRow> PropertyRows()
        {
            yield return new TableRow(null, "Level", "Element", "232 of 232", "Level 04");
            yield return new TableRow(null, "System Name", "Element", "198 of 232", "CHW-F") { Tone = Tone.Warn };
            yield return new TableRow(null, "Uniclass", "CamelWorks", "232 of 232", "Ss_60_40_15");
        }

        // -----------------------------------------------------------------------------------
        // Scenes — Viewpoints on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Scenes()
        {
            var setup = Ui.Stack(
                Bench.Group("Rename",
                    Ui.Field("Pattern", Ui.Text("{folder} {n:00} {name}", null, 180)),
                    Ui.Field("Start numbering at", Ui.Text("1", null, 60)),
                    Ui.Runner("Rename them", Ui.Stack(), _ => { }, primary: false)),
                Bench.Group("Render to images",
                    Ui.PathField("Into folder", Ui.Text(string.Empty, null, 180), PathKind.Folder),
                    Ui.Runner("Render them", Ui.Stack(), _ => { }, primary: false)));

            var stage = Ui.Stack(
                Ui.Across(Ui.Pill("218 viewpoints"), Ui.Pill("6 folders")),
                Ui.Table(ViewRows(), ("Viewpoint", 240), ("Folder", 140), ("Camera", 0)));

            return Deck("scenes", "viewpoints",
                Bench.Surface(setup, Ui.Runner("Save this view", Ui.Stack(), _ => { }), stage));
        }

        private static IEnumerable<TableRow> ViewRows()
        {
            yield return new TableRow(null, "Viewpoint", "Clash review", "12.4, 8.1, 15.7");
            yield return new TableRow(null, "Viewpoint (2)", "Clash review", "10.0, 3.5, 15.7");
            yield return new TableRow(null, "L04 riser", "Week 32", "9.2, 4.4, 16.0");
        }

        // -----------------------------------------------------------------------------------
        // Deliver — Batch on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Deliver()
        {
            var setup = Ui.Stack(
                Bench.Group("Job from this document",
                    Ui.Line("9 models loaded, all with a file to re-append:", 0.8),
                    Ui.Line("ARC-Riverside-Block-B-r7.nwc", 0.6),
                    Ui.Line("STR-Riverside-Block-B-r4.nwc", 0.6),
                    Ui.Field("Call it", Ui.Text("Weekly coordination", null, 200),
                             "The Monday morning job, from what is open."),
                    Ui.Across(Ui.Button("Job from this...", () => { }))));

            var stage = Ui.Stack(
                Ui.Table(JobRows(), ("Job", 170), ("Size", 90), ("What it does", 0)),
                Ui.Danger("Delete", () => { }),
                Ui.Small("A job runs on the model that is open now, using whatever is selected now."),
                Ui.Rule(),
                Ui.Eyebrow("What has run"),
                Ui.Table(RunRows(), ("Run", 0), ("When", 110)));

            return Deck("automate", "batch",
                Bench.Surface(setup, Ui.Runner("Run the selected job", Ui.Stack(), _ => { }), stage));
        }

        private static IEnumerable<TableRow> JobRows()
        {
            yield return new TableRow(null, "Weekly coordination", "12 nodes", "Append, Refresh, Run tests, Report");
            yield return new TableRow(null, "Friday NWD", "4 nodes", "Append, Save NWD");
        }

        private static IEnumerable<TableRow> RunRows()
        {
            yield return new TableRow(null, "ran \"Weekly coordination\": 12 steps, all ran", "Yesterday");
            yield return new TableRow(null, "ran \"Friday NWD\": stopped at step 2 — file locked", "Friday") { Tone = Tone.Bad };
        }

        // -----------------------------------------------------------------------------------
        // The pane shapes — the same anatomy, stacked at 470
        // -----------------------------------------------------------------------------------

        private static UIElement Issues()
        {
            var setup = Ui.Stack(
                Bench.Group("Where it stands",
                    Ui.Split("Open", "24"),
                    Ui.Split("Overdue", "3", Tone.Bad),
                    Ui.Split("Closed", "71", Tone.Good)),
                Bench.Group("Narrow it",
                    Ui.Field("Assigned to", Ui.Choice(new[] { "Anyone", "Mechanical", "Structures" },
                                                      "Anyone", null)),
                    Ui.Field("Raised by", Ui.Choice(new[] { "Any source", "Raised", "Clash",
                                                            "Clearance", "Imported" },
                                                    "Any source", null)),
                    Ui.Field("Find", Ui.Text(string.Empty, "title, number or assignee", null, 160))));

            var stage = Ui.Stack(
                Ui.Eyebrow("24 issues of 95 on this project"),
                Ui.Table(IssueRows(), ("Number", 80), ("Title", 220), ("Status", 90),
                         ("Assigned", 110), ("Due", 100), ("Raised by", 0)));

            return Deck("issuemgr", "issuelist",
                Bench.Surface(setup, Ui.Actions(Ui.Primary("New issue", () => { })), stage));
        }

        private static IEnumerable<TableRow> IssueRows()
        {
            yield return new TableRow(null, "CW-14", "L04 riser has no opening", "Open",
                                      "Structures", "4 Mar 2026", "Raised") { Tone = Tone.Bad };
            yield return new TableRow(null, "CW-15", "9 clashes in L03 MEP v STR", "Open",
                                      "Mechanical", "11 Mar 2026", "Clash");
            yield return new TableRow(null, "CW-16", "Clear height 1.85 m under the CHW main",
                                      "Answered", "Mechanical", "—", "Clearance") { Tone = Tone.Warn };
            yield return new TableRow(null, "CW-17", "Duct through beam", "Closed",
                                      "Structures", "—", "Imported") { Tone = Tone.Good };
        }

        private static UIElement Appearance()
        {
            var setup = Ui.Stack(
                Bench.Group("Add a layer",
                    Ui.Field("Name", Ui.Text(string.Empty, "Level 04 only", null, 160)),
                    Ui.Field("Covers", Ui.Choice(new[] { "The selection", "A saved set", "A search" },
                                                 "The selection", null)),
                    Ui.Field("Visibility", Ui.Choice(new[] { "Leave it", "Hide it", "Show it" },
                                                     "Leave it", null)),
                    Ui.Field("Colour (#rrggbb)", Ui.Text(string.Empty, "#c8102e", null, 110)),
                    Ui.Field("Transparency (0-1)", Ui.Text(string.Empty, "0.85", null, 90)),
                    Ui.Across(Ui.Button("Add layer", () => { }))));

            var stage = Ui.Stack(
                Ui.Eyebrow("Top of the list wins"),
                Layer("Clashing elements", "Red, opaque", Color.FromRgb(0xC8, 0x10, 0x2E)),
                Layer("Level 04 only", "Everything else 85% transparent", Color.FromRgb(0x9A, 0xA3, 0xAD)),
                Layer("Temporary works", "Hidden", Color.FromRgb(0xB4, 0xBC, 0xC5)),
                Ui.Across(
                    Ui.Button("Save the stack", () => { }),
                    Ui.Runner("Put the model back", Ui.Stack(), _ => { }, primary: false)),
                Ui.Rule(),
                Ui.Eyebrow("What else is going on"),
                Ui.Small("Everything hidden or overridden that no layer above accounts for."),
                Ui.Runner("Scan the model", Ui.Stack(), _ => { }, primary: false));

            return Deck("scenes", "appearance",
                Bench.Surface(setup, Ui.Runner("Apply the stack", Ui.Stack(), _ => { }), stage));
        }

        private static UIElement Layer(string name, string what, Color paint)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The literal colour the layer writes, not a Tone: a preview of the model, not of
            // the pane.
            var swatch = new Border
            {
                Width = 10,
                Height = 34,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(paint),
                Margin = new Thickness(0, 0, 10, 0),
            };

            grid.Children.Add(swatch);

            var text = Ui.Stack(Ui.Line(name, 1, true), Ui.Small(what));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

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
    }
}
