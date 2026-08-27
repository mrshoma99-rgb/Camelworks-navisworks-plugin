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
    /// The desks, reconstructed.
    ///
    /// One page per desk, each drawn as the desk window draws it — the header with the desk's
    /// colour, the rail read from the real <see cref="Desks"/> registry, and a flagship surface
    /// on the bench built to the anatomy in <c>CAMELWORKS_DESIGN.md</c>: setup column, one action
    /// at its foot, the answer on the stage. Two more pages show the beside-the-model shape at
    /// pane width, where the same anatomy stacks.
    ///
    /// Same rule as ever: same captions, same weights, plausible data where the real surface
    /// reads the host. Where a reconstruction and its surface disagree the reconstruction is
    /// wrong, and fixing it is part of changing the surface.
    /// </summary>
    public static class Panels
    {
        /// <summary>Every desk page, in ribbon order.</summary>
        public static IReadOnlyList<Sheet> All { get; } = new[]
        {
            Desk("desk-federation", "Desk · Federation", 780, Federation),
            Desk("desk-coordination", "Desk · Coordination", 820, Coordination),
            Desk("desk-data", "Desk · Data", 780, Data),
            Desk("desk-scenes", "Desk · Scenes", 780, Scenes),
            Desk("desk-deliver", "Desk · Deliver", 780, Deliver),
            new Sheet
            {
                Id = "board",
                Title = "Pane · Clash Board",
                Height = 1000,
                Builder = _ => Board(),
                Widths = new[] { 470, 640 },
            },
            new Sheet
            {
                Id = "appearance",
                Title = "Pane · Appearance",
                Height = 1000,
                Builder = _ => Appearance(),
                Widths = new[] { 470 },
            },
        };

        private static Sheet Desk(string id, string title, int height, Func<UIElement> build) =>
            new Sheet { Id = id, Title = title, Height = height, Builder = _ => build(), Widths = new[] { 1180 } };

        // -----------------------------------------------------------------------------------
        // The desk chrome
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A desk window's content: header, rail, bench strip, surface.
        ///
        /// The rail is read from the shipping registry rather than restated, so a tool moved from
        /// one stage to another moves here by itself — the one honest way to keep a picture true.
        /// </summary>
        private static UIElement Deck(string deskId, string toolId, UIElement bench)
        {
            var desk = Desks.Find(deskId)!;
            var tool = Tools.Find(toolId)!;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Header(desk));

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new Border
            {
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = Rail(desk, tool),
                    Padding = new Thickness(10, 10, 10, 10),
                },
                BorderThickness = new Thickness(0, 0, 1, 0),
            };

            rail.SetResourceReference(Border.BackgroundProperty, "Pane");
            rail.SetResourceReference(Border.BorderBrushProperty, "Bd");
            body.Children.Add(rail);

            var benched = new Grid();
            benched.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            benched.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            benched.Children.Add(Strip(tool));
            Grid.SetRow(bench, 1);
            benched.Children.Add(bench);

            Grid.SetColumn(benched, 1);
            body.Children.Add(benched);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var ground = new Border { Child = root };
            ground.SetResourceReference(Border.BackgroundProperty, "Bg");
            return ground;
        }

        private static UIElement Header(Desk desk)
        {
            var titles = new StackPanel { Margin = new Thickness(16, 12, 16, 11) };

            var name = new TextBlock { Text = desk.Title, FontSize = 17, FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            titles.Children.Add(name);

            var job = Ui.Sub(desk.Job);
            job.Margin = new Thickness(0, 1, 0, 0);
            titles.Children.Add(job);

            var bar = new Border { Child = titles, BorderThickness = new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "Nav");
            bar.SetResourceReference(Border.BorderBrushProperty, "Bd");

            var accent = new Border { Height = 2 };
            accent.SetResourceReference(Border.BackgroundProperty, "Fam." + desk.Family);

            var header = new Grid();
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.Children.Add(bar);
            Grid.SetRow(accent, 1);
            header.Children.Add(accent);

            return header;
        }

        private static UIElement Rail(Desk desk, Tool current)
        {
            var rail = new StackPanel();
            var first = true;

            foreach (var stage in desk.Stages)
            {
                var label = Ui.Eyebrow(stage.Title);
                label.Margin = new Thickness(8, first ? 2 : 16, 0, 5);
                rail.Children.Add(label);
                first = false;

                foreach (var toolId in stage.ToolIds)
                {
                    var tool = Tools.Find(toolId);
                    if (tool == null) continue;

                    rail.Children.Add(Row(desk, tool, tool.Id == current.Id));
                }
            }

            return rail;
        }

        private static UIElement Row(Desk desk, Tool tool, bool current)
        {
            var text = new StackPanel();

            var name = new TextBlock
            {
                Text = tool.Title,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            text.Children.Add(name);

            var aside = tool.Shape == ToolShape.Panel ? "opens beside the model"
                : tool.Shape == ToolShape.Act ? "runs now"
                : null;

            if (aside != null)
            {
                var hint = new TextBlock { Text = aside, FontSize = 10.5 };
                hint.Wearing("Hint");
                text.Children.Add(hint);
            }

            var marker = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Margin = new Thickness(0, 2, 7, 2),
                Background = Brushes.Transparent,
            };

            if (current) marker.SetResourceReference(Border.BackgroundProperty, "Fam." + desk.Family);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(marker);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var button = new Button
            {
                Content = row,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(7, 5, 7, 5),
                Margin = new Thickness(0, 0, 0, 1),
            };

            button.Wearing(current ? "RailRowOn" : "RailRow");
            return button;
        }

        private static UIElement Strip(Tool tool)
        {
            var titles = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 8, 16, 8),
            };

            var name = new TextBlock
            {
                Text = tool.Title,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            titles.Children.Add(name);

            var purpose = new TextBlock
            {
                Text = "—  " + tool.Purpose,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            purpose.Wearing("Hint");
            titles.Children.Add(purpose);

            var strip = new Border { Child = titles, BorderThickness = new Thickness(0, 0, 0, 1) };
            strip.SetResourceReference(Border.BackgroundProperty, "Pane");
            strip.SetResourceReference(Border.BorderBrushProperty, "Bd");
            return strip;
        }

        // -----------------------------------------------------------------------------------
        // Federation — Health Check on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Federation()
        {
            var setup = Ui.Stack(
                Bench.Group("The client's rules",
                    Ui.PathField("An .ids file", Ui.Text(string.Empty, "none — built-in rules only",
                                                         null, 200), PathKind.Open),
                    Ui.Runner("Check against an IDS", Ui.Stack(), _ => { }, primary: false),
                    Ui.Quiet("Write an IDS of our own...", () => { })));

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

            return Deck("federation", "health",
                Bench.Surface(setup, Ui.Runner("Check this federation", Ui.Stack(), _ => { }), stage));
        }

        // -----------------------------------------------------------------------------------
        // Coordination — Clash Tests on the bench
        // -----------------------------------------------------------------------------------

        private static UIElement Coordination()
        {
            var setup = Ui.Stack(
                Bench.Group("Build a matrix",
                    Ui.Field("Rows", Ui.Choice(new[] { "Discipline", "Model" }, "Discipline", null)),
                    Ui.Field("Columns", Ui.Choice(new[] { "Discipline", "Model" }, "Discipline", null)),
                    Ui.Check("Include each side against itself", false, null),
                    Ui.Runner("Show me the grid", Ui.Stack(), _ => { }, primary: false)),
                Bench.Group("How the tests are made",
                    Ui.Field("Name pattern", Ui.Text("{a} vs {b}", null, 180)),
                    Ui.Field("Tolerance (m)", Ui.Text("0.010", null, 80)),
                    Ui.Field("Put them in folder", Ui.Text("Week 33", null, 160),
                             "Empty keeps them beside the existing tests.")),
                Bench.Group("Already drawn by the client?",
                    Ui.Small("A matrix that arrives as a spreadsheet opens in its own reader."),
                    Ui.Quiet("Read their spreadsheet...", () => { })));

            var stage = Ui.Stack(
                Ui.Across(
                    Ui.Pill("4 tests"),
                    Ui.Pill("1,284 results"),
                    Ui.Pill("1 never run", Tone.Warn)),
                Ui.Table(TestRows(), ("Test", 220), ("Folder", 110), ("Results", 90), ("Last run", 150)),
                Ui.Runner("Run only the 1 that never have", Ui.Stack(), _ => { }, primary: false),
                Ui.Rule(),
                Ui.Line("36 pairings, 30 to make — 6 are already in the document and left alone.", 1, true),
                Ui.Runner("Create the ticked ones", Ui.Stack(), _ => { }));

            return Deck("coordination", "tests",
                Bench.Surface(setup, Ui.Runner("Run them", Ui.Stack(), _ => { }), stage));
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
                    Ui.Across(Ui.Button("Make a job from what is open", () => { }))));

            var stage = Ui.Stack(
                Ui.Table(JobRows(), ("Job", 170), ("Size", 90), ("What it does", 0)),
                Ui.Danger("Delete the selected job", () => { }),
                Ui.Small("A job runs on the model that is open now, using whatever is selected now."),
                Ui.Rule(),
                Ui.Eyebrow("What has run"),
                Ui.Table(RunRows(), ("Run", 0), ("When", 110)));

            return Deck("deliver", "batch",
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

        private static UIElement Board()
        {
            var setup = Ui.Stack(
                Bench.Group("Narrow the board",
                    Ui.Field("Show", Ui.Choice(new[] { "Everything", "Open only", "Regressed only" },
                                               "Open only", null)),
                    Ui.Field("Find", Ui.Text(string.Empty, "a word from any column", null, 160))));

            var stage = Ui.Stack(
                Ui.Across(
                    Ui.Figure("96", "issues"),
                    Ui.Figure("12", "new", Tone.Bad),
                    Ui.Figure("31", "resolved", Tone.Good),
                    Ui.Figure("3", "regressed", Tone.Bad)),
                Ui.Quiet("Undo the last board edit", () => { }),
                Ui.Table(BoardRows(), ("Issue", 150), ("Δ", 40), ("Owner", 90), ("Status", 0)));

            return Bench.Surface(setup, Ui.Runner("Load the board", Ui.Stack(), _ => { }), stage);
        }

        private static IEnumerable<TableRow> BoardRows()
        {
            yield return new TableRow(null, "L04 riser vs frame", "new", "Structures", "Active") { Tone = Tone.Bad };
            yield return new TableRow(null, "CHW main vs beam", "", "Mechanical", "Reviewed");
            yield return new TableRow(null, "Cable tray vs duct", "back", "Electrical", "Active") { Tone = Tone.Warn };
            yield return new TableRow(null, "Sprinkler vs soffit", "", "Fire", "Approved") { Tone = Tone.Good };
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

            return Bench.Surface(setup, Ui.Runner("Apply the stack", Ui.Stack(), _ => { }), stage);
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
