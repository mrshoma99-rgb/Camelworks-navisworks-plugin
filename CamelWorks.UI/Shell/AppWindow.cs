using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CamelWorks.UI.Views;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// One app of the suite, in its own window.
    ///
    /// The chrome every app shares: a title strip naming the app with its accent under it, a flat
    /// tab strip across the top — never collapsible, never nested — and the current tab's surface
    /// filling the rest, with its one action bottom-right in the surface's own footer. Opening an
    /// app again raises the one already open, on the tab its work stopped at.
    /// </summary>
    public sealed class AppWindow : Window
    {
        private static readonly Dictionary<string, AppWindow> Open =
            new Dictionary<string, AppWindow>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> LastTab =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly App _app;
        private readonly StackPanel _strip = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly ContentControl _surface = new ContentControl();
        private readonly TextBox _search = new TextBox { MinWidth = 200 };
        private readonly Popup _hits = new Popup { StaysOpen = false, AllowsTransparency = true };
        private string? _toolId;
        private bool _hooked;

        private AppWindow(App app)
        {
            _app = app;

            Title = app.Title + " — CamelWorks";
            Width = 1180;
            Height = 760;
            MinWidth = 900;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            Skin.Apply(this);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Header());

            var tabs = new Border
            {
                Padding = new Thickness(10, 0, 10, 0),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = _strip,
            };

            tabs.Tint(Border.BackgroundProperty, "Pane");
            tabs.Tint(Border.BorderBrushProperty, "Bd");
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);

            Grid.SetRow(_surface, 2);
            root.Children.Add(Ui.Loose(_surface));

            var ground = new Border { Child = LeaseStrip.Over(root) };
            ground.Tint(Border.BackgroundProperty, "Bg");
            Content = ground;

            // A job started on any tab keeps the strip closed while it runs: the Navisworks API
            // is main-thread-only, so the work pumps this window's dispatcher too, and switching
            // tabs mid-run would tear down the surface the job is still writing into.
            Loaded += (s, e) => Hook(true);
            Unloaded += (s, e) => Hook(false);
            Closed += (s, e) => { Hook(false); Open.Remove(_app.Id); };
        }

        /// <summary>
        /// Open an app, or raise the one already open, on a tab.
        /// </summary>
        /// <param name="appId">Which app.</param>
        /// <param name="toolId">Which tab's tool, or null for wherever the app's work stopped.</param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show(string? appId, string? toolId = null)
        {
            var app = Apps.Find(appId);

            if (app == null) return "There is no app called \"" + (appId ?? "nothing") + "\".";

            if (!Open.TryGetValue(app.Id, out var window) || window == null)
            {
                window = new AppWindow(app);
                // An app window stands beside the others, not over one of them.
                HostWindow.Own(window, topLevel: true);
                window.Show();

                // Remembered only once it is up. Written before the Show, a window that failed
                // on the way to the screen stayed in this list for the rest of the session: its
                // Closed never fires, so it is never removed, and every later press takes the
                // Activate branch below on a window nobody can see. The app then genuinely
                // cannot be opened again until Navisworks restarts.
                Open[app.Id] = window;
            }
            else
            {
                window.Activate();
            }

            window.Tab(toolId
                       ?? (LastTab.TryGetValue(app.Id, out var last) ? last : null)
                       ?? app.Tabs[0].ToolId);

            return null;
        }

        /// <summary>
        /// The app's name with the suite's under it, and the app's accent below both — the one
        /// piece of chrome that lets two open apps be told apart from across a room.
        /// </summary>
        private UIElement Header()
        {
            var titles = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 10, 16, 9),
            };

            var name = new TextBlock
            {
                Text = _app.Title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };

            name.Tint(TextBlock.ForegroundProperty, "Text");
            titles.Children.Add(name);

            var suite = new TextBlock
            {
                Text = "CamelWorks",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            suite.Wearing("Hint");
            titles.Children.Add(suite);

            // The suite is thirteen apps of five tabs each, and nobody looking for the appearance
            // manager types "appearance manager". Finder matches a tool by its name, by the other
            // names for the same thing, and by the problem it solves. It was on a front door of
            // its own; a search you have to open a window to reach is a search nobody uses, so it
            // is here instead, in the title strip of whichever app is already open.
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(titles);

            var frame = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _search,
            };

            frame.Tint(Border.BackgroundProperty, "Card");
            frame.Tint(Border.BorderBrushProperty, "Bd");

            _search.BorderThickness = new Thickness(0);
            _search.Background = System.Windows.Media.Brushes.Transparent;
            _search.VerticalAlignment = VerticalAlignment.Center;
            _search.ToolTip = "Find a tool by what it does";
            _search.TextChanged += (s, e) => Look();
            _search.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape) return;

                _search.Text = string.Empty;
                _hits.IsOpen = false;
            };

            _hits.PlacementTarget = frame;
            _hits.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            Grid.SetColumn(frame, 1);
            line.Children.Add(frame);

            var bar = new Border
            {
                Child = line,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };

            bar.Tint(Border.BackgroundProperty, "Nav");
            bar.Tint(Border.BorderBrushProperty, "Bd");

            var accent = new Border { Height = 2 };
            accent.Tint(Border.BackgroundProperty, "Fam." + _app.Family);

            var header = new Grid();
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.Children.Add(bar);
            Grid.SetRow(accent, 1);
            header.Children.Add(accent);

            return header;
        }

        /// <summary>
        /// What the search box matches now, as a short list under it.
        ///
        /// A tool in this app opens as a tab; one belonging to another app opens that app. Either
        /// way the press lands on the tool, which is the thing the search was about.
        /// </summary>
        private void Look()
        {
            var query = _search.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                _hits.IsOpen = false;
                return;
            }

            var found = Finder.Search(query).Take(8).ToList();
            var list = new StackPanel { Margin = new Thickness(4) };

            if (found.Count == 0)
            {
                list.Children.Add(Ui.Empty("Nothing matches \"" + query.Trim() + "\".",
                    "Search knows every tool by name, by other names for the same thing, and by "
                    + "the problem it solves."));
            }

            foreach (var tool in found)
            {
                var here = tool;
                var app = Apps.Of(here.Id);

                var row = new Button
                {
                    Content = Ui.Stack(
                        Ui.Line(here.Title + (app == null ? string.Empty : "  ·  " + app.Title), 1, true),
                        Ui.Line(here.Purpose, 0.7)),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 2),
                };

                row.Wearing("RailRow");
                row.Click += (s, e) =>
                {
                    _hits.IsOpen = false;
                    _search.Text = string.Empty;
                    Ui.Told(ToolViews.Open(here.Id));
                };

                list.Children.Add(row);
            }

            var card = new Border
            {
                Width = 380,
                MaxHeight = 420,
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = list,
                },
            };

            card.Tint(Border.BackgroundProperty, "Card");
            card.Tint(Border.BorderBrushProperty, "Bd");

            Skin.Apply(card);

            _hits.Child = card;
            _hits.IsOpen = true;
        }

        /// <summary>Show a tab and mark it on the strip.</summary>
        /// <param name="toolId">The tab's tool.</param>
        private void Tab(string toolId)
        {
            var tool = Tools.Find(toolId);
            if (tool == null) return;

            _toolId = tool.Id;
            LastTab[_app.Id] = tool.Id;

            BuildStrip();

            UIElement surface;

            try
            {
                surface = Surfaces.Build(tool.Id);
            }
            catch (Exception e)
            {
                // A surface that throws while being built would otherwise take the app with it.
                surface = Ui.Scroll(Ui.Stack(
                    Ui.Problem("This tab could not be opened: " + Ui.Describe(e))));
            }

            _surface.Content = surface;
        }

        /// <summary>
        /// Rebuild the strip: every tab, flat, the current one carrying the app's accent.
        /// </summary>
        private void BuildStrip()
        {
            _strip.Children.Clear();

            foreach (var tab in _app.Tabs)
            {
                var here = tab;
                var current = string.Equals(tab.ToolId, _toolId, StringComparison.OrdinalIgnoreCase);

                var label = new TextBlock
                {
                    Text = tab.Title,
                    FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                    Margin = new Thickness(12, 8, 12, 7),
                };

                label.Tint(TextBlock.ForegroundProperty, current ? "Text" : "Muted");

                var mark = new Border { Height = 2, Background = Brushes.Transparent };
                if (current) mark.Tint(Border.BackgroundProperty, "Fam." + _app.Family);

                var face = new Grid();
                face.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                face.Children.Add(label);
                Grid.SetRow(mark, 1);
                face.Children.Add(mark);

                var button = new Button
                {
                    Content = face,
                    Padding = new Thickness(0),
                    ToolTip = Tools.Find(here.ToolId)?.Purpose,
                };

                button.Wearing("RailRow");
                button.Click += (s, e) => Tab(here.ToolId);

                _strip.Children.Add(button);
            }
        }

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;

            if (on)
            {
                Ui.Running += OnRunning;
                Skin.Changed += OnSkinChanged;
            }
            else
            {
                Ui.Running -= OnRunning;
                Skin.Changed -= OnSkinChanged;
            }
        }

        private void OnRunning(object? sender, int count)
        {
            // A run holds the Navisworks API for its whole length, and a tab switch mid-run
            // would ask it for something it cannot answer.
            _strip.IsEnabled = count == 0;
        }

        /// <summary>
        /// Rebuild after a theme change, for the parts built with a brush resolved at the time.
        /// </summary>
        private void OnSkinChanged(object? sender, EventArgs e)
        {
            if (_toolId != null) Tab(_toolId);
        }
    }
}
