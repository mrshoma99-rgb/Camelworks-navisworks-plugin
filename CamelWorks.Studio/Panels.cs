using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
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
            Page("app-clash-manage", "Clash Manager · Manage", 980, () => Coordination(false)),
            Page("app-clash-finder", "Clash Manager · Manage, the filter panel open", 980,
                 () => Coordination(true)),
            Page("app-issue-register", "Issue Manager · Issues", 780, Issues),
            Page("app-audit", "Model Audit · Health", 780, Federation),
            Page("app-data", "Data Manager · Properties", 780, Data),
            Page("app-scenes-appearance", "Scene Kit · Appearance", 780, Appearance),
            Page("app-scenes-viewpoints", "Scene Kit · Viewpoints", 780, Scenes),
            Page("app-automate-jobs", "Automate · Jobs", 780, Deliver),

            // <b>The one page here that is the shipping screen rather than a drawing of it.</b>
            // Every other page composes the real controls into an arrangement written here, which
            // catches a wrong colour or a clipped word but cannot catch a fault in the layout code
            // itself - and the matrix's faults have all been in the layout code: a header sized
            // without its own padding, a rotated name given a box longer than the space it has, a
            // folder band collapsed to no height at all. None of those are visible in an
            // arrangement written by hand, because the hand writes what it meant.
            //
            // MatrixWindow.Surface takes a seat now, so this is the tab itself, measured by WPF at
            // the width WPF gives it, in the font it will really be read in.
            Page("app-clash-matrix", "Clash Manager · Matrix", 820,
                 () => MatrixWindow.Surface(Seated())),

            Page("app-clash-matrix-drawn", "Clash Manager · Matrix, drawn", 820,
                 () => MatrixWindow.Surface(Seated(), Drawn())),
        };

        /// <summary>
        /// A seat with nothing in it, and a scratch project beside the images.
        ///
        /// <b>Empty on purpose.</b> This is the state a screen faces before a document is open,
        /// and the one nobody remembers to check - so it is the one the harness draws. A screen
        /// that reads badly here reads badly on somebody's first minute of the day.
        /// </summary>
        private static ISeat Seated() => new EmptySeat(
            ProjectStore.Open(PhysicalFileSystem.Instance,
                              System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                     "camelworks-shots", "shots.nwf"),
                              System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                     "camelworks-shots")),
            Host.WithoutClash());

        /// <summary>
        /// A matrix of the size and shape a real one is, so the grid can be looked at.
        ///
        /// <b>Every picture of this tab was the empty start screen.</b> A seat with no document
        /// has no board, so the grid - the whole of the tab, and where every layout fault
        /// reported against it has been - was never photographed once. The clipped column names,
        /// the row band that ran out of names, the dead strip beside the last column: all of them
        /// were described in words by somebody on a licensed seat, and fixed by reasoning about
        /// arithmetic rather than by looking.
        ///
        /// So the shapes that break it are here on purpose. Names long enough to have to be
        /// turned and long enough to be cut off if the band is short by a pixel; enough
        /// disciplines that the cells reach their smallest size; folders on both axes, one of
        /// them holding a run; codes that are not the front of their names, so the code gutter is
        /// drawn; and a number that disagrees with its position, so the number gutter is drawn
        /// too. A matrix that exercises none of those proves nothing.
        /// </summary>
        private static MatrixBoard Drawn()
        {
            var board = new MatrixBoard();

            var named = new[]
            {
                ("ARC", "ARC_General Elements", "Architecture"),
                ("ARC", "ARC_Internal Partitions", "Architecture"),
                ("ARC", "ARC_Ceilings and Soffits", "Architecture"),
                ("STR", "STR_Structural Frame", "Structure"),
                ("STR", "STR_Slabs and Decks", "Structure"),
                ("STR", "STR_Foundations", "Structure"),
                ("FGT", "FGT_Equipment", string.Empty),
                ("FGT", "FGT_Insulation", string.Empty),
                ("WSC", "WSC_Pipes", "Wet services"),
                ("WSC", "WSC_Ducts", "Wet services"),
                ("WSC", "WSC_Equipment", "Wet services"),
                ("WSC", "WSC_Insulation", "Wet services"),
                ("AUX", "AUX_General Purpose Pipes", string.Empty),
                ("AUX", "AUX_Equipment", string.Empty),
                ("AUX", "AUX_Insulation", string.Empty),
                ("EIC", "EIC_Cable Trays and Supports", "Electrical"),
                ("EIC", "EIC_Containment", "Electrical"),
                ("EIC", "EIC_Lighting", "Electrical"),
                (string.Empty, string.Empty, string.Empty),
                (string.Empty, string.Empty, string.Empty),
            };

            for (var at = 0; at < named.Length; at++)
            {
                var (code, name, folder) = named[at];

                // The numbers skip one, so they stop agreeing with the position in the list and
                // the gutter that prints them is drawn - which is a column of the header the
                // empty screen never had to lay out.
                var row = board.AddPair(new MatrixLine(name)
                {
                    Code = code,
                    Number = (at + (at > 8 ? 2 : 1)).ToString(CultureInfo.InvariantCulture),
                });

                if (folder.Length > 0) board.File(row, folder);
            }

            // Coloured, because a folder that can carry a colour and is never drawn with one is
            // a feature no picture has ever checked. Two of the four, so the render shows both a
            // folder that has been given one and folders that have not.
            board.Paint("Architecture", "#B3E5FC");
            board.Paint("Wet services", "#C8E6C9");

            // Enough decided cells that the sheet is read as a pattern rather than as a grid of
            // blanks, and a diagonal that has to be drawn as one.
            var marks = new[] { "A", "B", "C", "D" };

            for (var down = 0; down < board.Rows.Count; down++)
                for (var across = 0; across < board.Columns.Count; across++)
                {
                    if (down == across) continue;
                    if (((down * 7) + (across * 3)) % 5 == 0) continue;

                    board.Set(board.Rows[down], board.Columns[across],
                              marks[((down * 3) + across) % marks.Length]);
                }

            return board;
        }

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
                           "Most are one system with a typo in its family; the table below names them."),
                Ui.Eyebrow("Sets"),
                Ui.Finding(true, "Sets", "\"Level 3 Ductwork\" returns nothing. It held 412 elements "
                           + "when it was last read.",
                           "clash test \"MEP vs Structure\" and rule \"Ducts carry a system name\" "
                           + "are pointed at this set, and now cover nothing. A check over nothing "
                           + "does not fail — it passes.",
                           "Open the set and see what it matches now. A set is a saved query, so "
                           + "what changed is usually a property the model stopped carrying.",
                           "clash test \"MEP vs Structure\"; rule \"Ducts carry a system name\""));

            return Deck("audit", "health",
                Bench.Surface(setup, Ui.Runner("Check this federation", Ui.Stack(), _ => { }), stage));
        }

        // -----------------------------------------------------------------------------------
        // Coordination — the clash results bench
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Clash Manager ▸ Manage: the test finder, the board, and the bar for what is picked.
        ///
        /// Reconstructed from <c>ClashManageViews</c>, which needs a live document and cannot run
        /// here. Everything structural is the real thing — <see cref="Parts"/> draws the chips,
        /// the bands and the bar, and the columns are the surface's own widths — so what this
        /// picture gets wrong is the data and never the design.
        /// </summary>
        private static UIElement Coordination(bool panel)
        {
            var rail = Rail(panel);
            var board = Board();

            var foot = new DockPanel { LastChildFill = false };

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var runner = Ui.Runner("Refresh", Ui.Stack(), _ => { }, primary: false);
            runner.Margin = new Thickness(0, 0, 10, 0);
            runner.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(runner);

            right.Children.Add(Parts.MenuButton("Export", Glyphs.Onward, new[]
            {
                Parts.Entry.Barred("Sync into the document", Glyphs.Save,
                    "This is a published NWD — its clash results are read-only"),
                Parts.Entry.Saying("Save as a clash file (BCF)", Glyphs.Round,
                                   "The offline review round — out and back", null),
                Parts.Entry.Saying("Write a report", Glyphs.Report,
                                   "One document per party, revision-lettered", null),
            }));

            // The caption the product declares. Its counted form — "Save 14 changes into the
            // document" — is built at run time from what the document refused, and a picture
            // cannot claim a caption no panel contains.
            var save = Ui.Primary("Save changes", () => { });
            save.Margin = new Thickness(8, 0, 0, 0);
            save.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(save);

            DockPanel.SetDock(right, Dock.Right);
            foot.Children.Add(right);

            return Deck("clash", "manage", Bench.Split(null, rail, board, foot, 372));
        }

        // -----------------------------------------------------------------------------------
        // Region 1 — the test finder
        // -----------------------------------------------------------------------------------

        private static UIElement Rail(bool panel)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tools = new StackPanel { Orientation = Orientation.Horizontal };

            tools.Children.Add(Parts.MoreMenu(Glyphs.Tune, "Sort: Most new",
                new[] { Parts.Entry.Act("Most new", null, null) }));

            // Accented, because a filter is in force and the chips that used to say so are gone.
            var narrow = Ui.Icon(Glyphs.Filter, "Narrowed to Mechanical. Press to change it.",
                                 () => { });

            narrow.Content = Ui.Glyph(Glyphs.Filter, 15, "Accent");
            tools.Children.Add(narrow);

            var top = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(tools, Dock.Right);
            top.Children.Add(tools);
            top.Children.Add(Ui.Text(string.Empty, "Search tests by name or discipline", null, 150));

            var head = new Border
            {
                Padding = new Thickness(14, 10, 14, 10),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = panel ? Ui.Stack(top, Narrowing()) : Ui.Stack(top),
            };

            head.Tint(Border.BorderBrushProperty, "Bd");
            grid.Children.Add(head);

            var rows = Ui.Stack(RailLabels());

            rows.Children.Add(RailRow("L03 — Mech v Struct", "25 mm", true, 18, 42, 12, 31, 65));
            rows.Children.Add(RailRow("L03 — Elec v Struct", "25 mm", false, 6, 11, 4, 8, 13));
            rows.Children.Add(RailRow("L04 — Mech v Arch", "10 mm", false, 24, 61, 30, 4, 8));
            rows.Children.Add(RailRow("Risers — All v All", "25 mm", false, 0, 0, 0, 40, 56));
            rows.Children.Add(RailRow("B1 — Drainage v Struct", "50 mm · run Mon 08:40", false, 3, 7, 2, 1, 3));
            rows.Children.Add(RailRow("L05 — Fire v Struct", "25 mm · run Mon 08:41", false, 0, 4, 9, 11, 20));

            var list = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = rows,
            };

            Grid.SetRow(list, 1);
            grid.Children.Add(list);

            var verbs = new StackPanel { Orientation = Orientation.Horizontal };
            verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run all the tests again", () => { }));

            verbs.Children.Add(Parts.MoreMenu(Glyphs.More, "Everything else you can do to tests",
                new[] { Parts.Entry.Act("Rename this test", Glyphs.Pencil, null) }, up: true));

            var bar = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(verbs, Dock.Right);
            bar.Children.Add(verbs);

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 5, 0),
            };

            dot.Tint(System.Windows.Shapes.Shape.FillProperty, "RedTx");

            var held = Ui.Link("14 waiting", () => { });            // harness: data
            held.Margin = new Thickness(0);
            held.VerticalAlignment = VerticalAlignment.Center;

            DockPanel.SetDock(dot, Dock.Right);
            DockPanel.SetDock(held, Dock.Right);
            bar.Children.Add(held);
            bar.Children.Add(dot);

            var say = new TextBlock
            {
                Text = "3 of 6 tests · 168 results, 143 loose",           // harness: data
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            bar.Children.Add(say.Wearing("Hint"));

            var foot = new Border
            {
                Padding = new Thickness(14, 8, 14, 8),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = bar,
            };

            foot.Tint(Border.BackgroundProperty, "Pane");
            foot.Tint(Border.BorderBrushProperty, "Bd");

            Grid.SetRow(foot, 2);
            grid.Children.Add(foot);

            var pane = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
            pane.Tint(Border.BackgroundProperty, "Card");
            pane.Tint(Border.BorderBrushProperty, "Bd");

            return pane;
        }

        /// <summary>The rail's filter panel, which is where a filter is made and taken off.</summary>
        private static UIElement Narrowing()
        {
            var body = Ui.Stack(Ui.Eyebrow("Discipline"));

            var parties = new WrapPanel();

            foreach (var party in new[] { "Mechanical", "Structures", "Electrical", "Architecture" })
                parties.Children.Add(Parts.Pick(party, party == "Mechanical", () => { }));

            body.Children.Add(parties);
            body.Children.Add(Ui.Eyebrow("Folder"));

            var folders = new WrapPanel();

            foreach (var folder in new[] { "Week 33", "Week 32" })
                folders.Children.Add(Parts.Pick(folder, false, () => { }));

            body.Children.Add(folders);
            body.Children.Add(Ui.Check("Only tests with something open", false, null));
            body.Children.Add(Ui.Link("Clear all", () => { }));

            var panel = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0),
                Child = body,
            };

            panel.Tint(Border.BackgroundProperty, "Bg");
            panel.Tint(Border.BorderBrushProperty, "Bd");

            return panel;
        }

        private static UIElement RailLabels()
        {
            var row = new DockPanel { LastChildFill = true };
            var figures = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var label in new[] { "New", "Active", "Rev.", "Appr.", "Res." })
            {
                var head = new TextBlock
                {
                    Text = label,
                    MinWidth = 26,
                    Margin = new Thickness(10, 0, 0, 0),
                    TextAlignment = TextAlignment.Right,
                };

                figures.Children.Add(head.Wearing("Hint"));
            }

            DockPanel.SetDock(figures, Dock.Right);
            row.Children.Add(figures);
            row.Children.Add(new TextBlock { Text = "Test" }.Wearing("Hint"));

            var band = new Border
            {
                Padding = new Thickness(27, 5, 14, 5),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
            };

            band.Tint(Border.BackgroundProperty, "Card2");
            band.Tint(Border.BorderBrushProperty, "Bd");

            return band;
        }

        private static UIElement RailRow(string name, string hint, bool on,
                                         int fresh, int active, int reviewed, int approved,
                                         int closed)
        {
            var title = new TextBlock
            {
                Text = name,                                        // harness: data
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            var under = new TextBlock
            {
                Text = hint,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 1, 0, 0),
            };

            var row = new DockPanel { LastChildFill = true };

            var band = Parts.CountBand(new[] { fresh, active, reviewed, approved, closed },
                                       fresh + active == 0);

            DockPanel.SetDock(band, Dock.Right);
            row.Children.Add(band);
            row.Children.Add(Ui.Stack(title.Wearing("Body"), under.Wearing("Hint")));

            var tone = new Border
            {
                Padding = new Thickness(12, 8, 14, 8),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Child = row,
            };

            tone.BorderBrush = on ? Skin.Of("Accent")
                : fresh > 0 ? Skin.Of("RedTx")
                : fresh + active > 0 ? Skin.Of("AmberTx")
                : Skin.Of("Ok");

            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tone };
            line.Tint(Border.BorderBrushProperty, "Bd");

            if (on) line.Tint(Border.BackgroundProperty, "NavActive");

            return line;
        }

        // -----------------------------------------------------------------------------------
        // Region 2 and 3 — the board, and the bar for what is picked
        // -----------------------------------------------------------------------------------

        private static UIElement Board()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Border
            {
                Padding = new Thickness(16, 10, 16, 0),
                Child = Ui.Stack(BoardTitle(), BoardStrip()),
            };

            grid.Children.Add(head);

            var body = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 10, 16, 16),
                Content = BoardTable(),
            };

            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var bar = Parts.SelectionBar(3, "row", () => { },
                Parts.InlineChip(null, new[] { "New", "Active" }, _ => { }, Tone.Plain, "Set status"),
                Parts.InlineChip(null, new[] { "Mechanical" }, _ => { }, Tone.Plain, "Assign to"),
                Parts.Verb("Group", Glyphs.Group, true, () => { }),
                Parts.Verb("Raise as issues", Glyphs.Issue, false, () => { }),
                Parts.MoreMenu(Glyphs.More, "Everything else for the picked rows",
                    new[] { Parts.Entry.Act("Focus all of them", Glyphs.Focus, null) }, up: true));

            Grid.SetRow(bar, 2);
            grid.Children.Add(bar);

            var ground = new Border { Child = grid };
            ground.Tint(Border.BackgroundProperty, "Bg");

            return ground;
        }

        private static UIElement BoardTitle()
        {
            var row = new DockPanel { LastChildFill = true };

            var verbs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            verbs.Children.Add(Ui.Icon(Glyphs.Play, "Run this test again", () => { }));
            verbs.Children.Add(Ui.Icon(Glyphs.Read, "Walk the board one clash at a time", () => { }));
            verbs.Children.Add(Ui.Icon(Glyphs.Columns, "Which columns the board shows", () => { }));

            var layers = Parts.MoreMenu(Glyphs.Layers,
                "Showing sharing an item with Pipe CHW-31 and Wall PC-200. Press to change it.",
                new[]
                {
                    Parts.Entry.Heading("Show: Both"),
                    Parts.Entry.Rule(),
                    Parts.Entry.Heading("Sharing an item with Pipe CHW-31 and Wall PC-200"),
                    Parts.Entry.Act("Every clash again, not just these", Glyphs.Close, null),
                });

            verbs.Children.Add(layers);

            verbs.Children.Add(Ui.Icon(Glyphs.Tune, "Settings", () => { }));

            DockPanel.SetDock(verbs, Dock.Right);
            row.Children.Add(verbs);

            var said = new TextBlock
            {
                Text = "Showing 18 of 168",                         // harness: data
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            var head = new TextBlock
            {
                Text = "L03 — Mech v Struct",                       // harness: data
                VerticalAlignment = VerticalAlignment.Bottom,
            };

            var words = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(head, Dock.Left);
            words.Children.Add(head.Wearing("Head"));
            words.Children.Add(said.Wearing("Hint"));

            row.Children.Add(words);

            return row;
        }

        private static UIElement BoardStrip()
        {
            var strip = new StackPanel { Orientation = Orientation.Horizontal };

            var tabs = new (string Label, int Count, string Key, bool On)[]
            {
                ("all", 168, "Text", false),
                ("new", 18, "RedTx", true),
                ("active", 42, "Text", false),
                ("reviewed", 12, "AmberTx", false),
                ("approved", 31, "Ok", false),
                ("resolved", 65, "Ok", false),
            };

            for (var i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];

                var figure = new TextBlock
                {
                    Text = tab.Count.ToString(CultureInfo.InvariantCulture),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };

                figure.Tint(TextBlock.ForegroundProperty, tab.Key);

                var label = new TextBlock
                {
                    Text = tab.Label,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(4, 0, 0, 0),
                };

                label.Tint(TextBlock.ForegroundProperty, tab.On ? "Text" : "Faint");

                var words = new StackPanel { Orientation = Orientation.Horizontal };
                words.Children.Add(figure.Wearing("Body"));
                words.Children.Add(label);

                var segment = new Border
                {
                    Padding = new Thickness(8, 6, 8, tab.On ? 4 : 6),
                    BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, tab.On ? 2 : 0),
                    Child = words,
                };

                segment.Tint(Border.BorderBrushProperty, tab.On ? "Accent" : "Bd");
                if (tab.On) segment.Tint(Border.BackgroundProperty, "AccentWash");

                strip.Children.Add(segment);
            }

            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = strip,
            };

            pill.Tint(Border.BackgroundProperty, "Card");
            pill.Tint(Border.BorderBrushProperty, "Bd");
            pill.SetValue(UIElement.ClipToBoundsProperty, true);

            var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };

            var find = Ui.Text(string.Empty, "Search", null, 128);
            find.MaxWidth = 128;
            find.VerticalAlignment = VerticalAlignment.Center;
            find.Margin = new Thickness(0, 0, 6, 0);

            DockPanel.SetDock(find, Dock.Right);
            row.Children.Add(find);
            row.Children.Add(pill);

            return row;
        }

        /// <summary>The nine columns, in the order the surface builds them.</summary>
        private static Grid Cells()
        {
            var grid = new Grid();

            void Fixed(double width) =>
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            void Flexible(double share, double least) =>
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(share, GridUnitType.Star),
                    MinWidth = least,
                });

            Fixed(26);
            Flexible(2, 110);
            Flexible(1, 58);
            Flexible(1, 58);
            Fixed(44);
            Fixed(60);
            Flexible(1, 60);    // one property column, as somebody's own column would be
            Fixed(104);
            Fixed(104);
            Fixed(120);

            return grid;
        }

        private static void Put(Grid grid, UIElement cell, int column)
        {
            if (cell is FrameworkElement element)
            {
                element.Margin = new Thickness(0, 0, 8, 0);
                element.VerticalAlignment = VerticalAlignment.Center;
            }

            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        /// <summary>A column header: pressable to sort, with a handle to resize it.</summary>
        private static UIElement Head(string column, bool right, bool on)
        {
            var label = new TextBlock
            {
                Text = column,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            label.Wearing("Hint");
            if (on) label.Tint(TextBlock.ForegroundProperty, "Accent");

            var words = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var mark = on
                ? Ui.Glyph(Glyphs.Down, 10, "Accent",
                           right ? new Thickness(0, 0, 3, 0) : new Thickness(4, 0, 0, 0))
                : null;

            if (right && mark != null) words.Children.Add(mark);
            words.Children.Add(label);
            if (!right && mark != null) words.Children.Add(mark);

            return new Border
            {
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = on ? "Sorted by " + column + ". Press again to turn it round."
                             : "Sort by " + column,
                Child = words,
            };
        }

        private static TextBlock Hint(string text) =>
            new TextBlock
            {
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            }.Wearing("Hint");

        private static TextBlock Right(TextBlock cell)
        {
            cell.TextAlignment = TextAlignment.Right;
            return cell;
        }

        private static UIElement BoardTable()
        {
            var body = Ui.Stack(TableHead());

            body.Children.Add(GroupRow("Corridor C — duct against beams", 7, "-48 mm", true, true));
            body.Children.Add(ClashRow("Clash 121", "Duct 400×250", "Beam 610UB", "-48 mm",
                                       "New", Tone.Bad, "Structures", "CHW Supply", true, false));
            body.Children.Add(ClashRow("Clash 122", "Duct 600×300", "Beam 460UB", "-31 mm",
                                       "Active", Tone.Plain, "Mechanical", "CHW Supply", true, false));
            body.Children.Add(GroupRow("Riser 2 — pipework against slab edge", 5, "-22 mm", false, false));

            body.Children.Add(Parts.ViewBand(Glyphs.Clash, "Not grouped", "143 loose results",
                                             "of 143 — tick two or more and press Group", () => { }));

            body.Children.Add(ClashRow("Clash 208", "Pipe CHW-31", "Wall PC-200", "-52 mm",
                                       "New", Tone.Bad, string.Empty, "CHW Return", false, true));
            body.Children.Add(ClashRow("Clash 209", "Tray CT-9", "Bracing BR-3", "-14 mm",
                                       "Reviewed", Tone.Warn, "Electrical", "Small Power", false, true));
            body.Children.Add(ClashRow("Clash 210", "Sprinkler SP-88", "Slab L03", "-9 mm",
                                       "Resolved", Tone.Good, "Fire", "Wet Riser", false, false));

            var card = new Border
            {
                MinWidth = 772,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = body,
            };

            card.Tint(Border.BackgroundProperty, "Card");
            card.Tint(Border.BorderBrushProperty, "Bd");
            card.SetValue(UIElement.ClipToBoundsProperty, true);

            var sideways = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = card,
            };

            card.SetBinding(FrameworkElement.WidthProperty,
                            new System.Windows.Data.Binding("ViewportWidth") { Source = sideways });

            return sideways;
        }

        private static UIElement TableHead()
        {
            var grid = Cells();

            var all = new CheckBox
            {
                IsThreeState = true,
                IsChecked = null,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Put(grid, all, 0);
            Put(grid, Head("Group or result", false, false), 1);
            Put(grid, Head("Item 1", false, false), 2);
            Put(grid, Head("Item 2", false, false), 3);
            Put(grid, Head("Clashes", true, false), 4);
            Put(grid, Head("Worst", true, true), 5);
            Put(grid, Head("System Name", false, false), 6);
            Put(grid, Head("Status", false, false), 7);
            Put(grid, Head("Assigned", false, false), 8);

            var band = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid,
            };

            band.Tint(Border.BackgroundProperty, "Card2");
            band.Tint(Border.BorderBrushProperty, "Bd");

            return band;
        }

        private static UIElement GroupRow(string name, int count, string worst, bool open, bool made)
        {
            var grid = Cells();

            Put(grid, new CheckBox { IsChecked = false, VerticalAlignment = VerticalAlignment.Center }, 0);

            var words = new StackPanel { Orientation = Orientation.Horizontal };
            words.Children.Add(Ui.Glyph(open ? Glyphs.Down : Glyphs.Right, 13, "Muted",
                                        new Thickness(0, 0, 7, 0)));

            words.Children.Add(Ui.Glyph(Glyphs.Group, 15, "Accent", new Thickness(0, 0, 7, 0)));

            var title = new TextBlock
            {
                Text = name,                                        // harness: data
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };

            words.Children.Add(title.Wearing("Body"));

            if (made) words.Children.Add(Parts.Note("grouped by you"));

            Put(grid, words, 1);
            Put(grid, Right(Hint("—")), 2);
            Put(grid, Right(Hint("—")), 3);
            Put(grid, Figure(count.ToString(CultureInfo.InvariantCulture), false), 4);
            Put(grid, Figure(worst, true), 5);

            Put(grid, Hint("—"), 6);
            Put(grid, Parts.InlineChip("New", new[] { "New" }, _ => { }, Tone.Bad, "Set status"), 7);
            Put(grid, Parts.InlineChip("Structures", new[] { "Structures" }, _ => { }, Tone.Plain, "Assign"), 8);
            var groupVerbs = Verbs();
            Put(grid, groupVerbs, 9);
            groupVerbs.Margin = new Thickness(0);

            var tone = new Border
            {
                Padding = new Thickness(12, 9, 12, 9),
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = Skin.Of("RedTx"),
                Child = grid,
            };

            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = tone };
            line.Tint(Border.BorderBrushProperty, "Bd");

            return line;
        }

        private static UIElement ClashRow(string name, string a, string b, string worst,
                                          string status, Tone tone, string who, string system,
                                          bool inside, bool picked)
        {
            var grid = Cells();

            Put(grid, new CheckBox { IsChecked = picked, VerticalAlignment = VerticalAlignment.Center }, 0);

            var title = new TextBlock
            {
                Text = name,                                        // harness: data
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(inside ? 22 : 0, 0, 8, 0),
            };

            title.Wearing("Mono");
            title.Tint(TextBlock.ForegroundProperty, "Faint");
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            Put(grid, Item(a), 2);
            Put(grid, Item(b), 3);

            Put(grid, Right(Hint("—")), 4);
            Put(grid, Figure(worst, worst.StartsWith("-4", StringComparison.Ordinal)
                                    || worst.StartsWith("-5", StringComparison.Ordinal)), 5);

            Put(grid, Item(system), 6);
            Put(grid, Parts.InlineChip(status, new[] { status }, _ => { }, tone, "Set status"), 7);
            Put(grid, Parts.InlineChip(who, new[] { "Mechanical" }, _ => { }, Tone.Plain, "Assign"), 8);

            var verbs = Verbs();
            verbs.Visibility = picked ? Visibility.Visible : Visibility.Hidden;
            Put(grid, verbs, 9);
            verbs.Margin = new Thickness(0);

            var edge = status == "New" ? Skin.Of("RedTx")
                : status == "Reviewed" ? Skin.Of("AmberTx")
                : status == "Resolved" ? Skin.Of("Ok")
                : Brushes.Transparent;

            var toned = new Border
            {
                Padding = new Thickness(12, inside ? 7 : 9, 12, inside ? 7 : 9),
                BorderThickness = new Thickness(inside ? 0 : 3, 0, 0, 0),
                BorderBrush = inside ? Brushes.Transparent : edge,
                Child = grid,
            };

            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = toned };
            line.Tint(Border.BorderBrushProperty, "Bd");

            if (picked) line.Tint(Border.BackgroundProperty, "NavActive");
            else if (inside) line.Tint(Border.BackgroundProperty, "Card2");

            return line;
        }

        private static TextBlock Item(string name) =>
            new TextBlock
            {
                Text = name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            }.Wearing("Lbl");

        private static FrameworkElement Verbs()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            bar.Children.Add(Ui.Icon(Glyphs.Focus, "Focus this clash", () => { }));
            bar.Children.Add(Ui.Icon(Glyphs.Eye, "Isolate this clash", () => { }));
            bar.Children.Add(Ui.Icon(Glyphs.Comment, "Comment", () => { }));

            bar.Children.Add(Parts.MoreMenu(Glyphs.More, "Everything else for this clash",
                new[]
                {
                    Parts.Entry.Act("Snapshot", Glyphs.Camera, null),
                    Parts.Entry.Rule(),
                    Parts.Entry.Act("Find other clashes with these items", Glyphs.Find, null),
                }));

            foreach (var verb in bar.Children.OfType<FrameworkElement>())
                verb.Margin = new Thickness(2, 2, 2, 2);

            return bar;
        }

        private static TextBlock Figure(string text, bool bad)
        {
            var cell = new TextBlock { Text = text, TextAlignment = TextAlignment.Right };
            cell.Wearing("Body");
            cell.Tint(TextBlock.ForegroundProperty, bad ? "RedTx" : "Muted");
            return cell;
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
