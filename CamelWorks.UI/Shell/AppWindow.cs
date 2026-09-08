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
        private readonly DockPanel _strip = new DockPanel { LastChildFill = false };
        private readonly ContentControl _surface = new ContentControl();
        private string? _toolId;
        private bool _hooked;

        /// <summary>
        /// Every tab this window has already built, so switching back is immediate.
        ///
        /// Switching a tab used to rebuild it. Every visit to Manage built the rail again — a row
        /// per test, a hundred and sixty of them — then the board, the columns and the legend, from
        /// nothing. The state behind them survives a switch, because these screens keep it; what did
        /// not survive was the several thousand elements drawn from it, and rebuilding those is the
        /// lag. A tab that has been built once is kept and restored.
        ///
        /// The cache is dropped when the document changes. A surface built against one federation is
        /// full of another model's tests, sets and levels, so the whole cache goes with the
        /// document, which is the rule the pickers already follow.
        /// </summary>
        private readonly Dictionary<string, UIElement> _built =
            new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase);

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
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Header());

            Grid.SetRow(_surface, 1);
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
        /// <param name="afresh">
        /// True to draw the tab again rather than serve the surface it was left as. For a tab
        /// asking to be rebuilt because a setting it reads at build time has changed.
        /// </param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show(string? appId, string? toolId = null, bool afresh = false)
        {
            var app = Apps.Find(appId);

            if (app == null) return "There is no app called \"" + (appId ?? "nothing") + "\".";

            if (!Open.TryGetValue(app.Id, out var window) || window == null)
            {
                window = new AppWindow(app);
                // An app window stands beside the others, not over one of them.
                HostWindow.Own(window, topLevel: true);

                // Before it is up: a modeless window inside the host's WinForms loop gets no
                // keys until it is bridged, and a search box you cannot type in is what that
                // looks like from the outside.
                HostWindow.Typing(window);

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
                       ?? app.Tabs[0].ToolId,
                       afresh);

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
                Margin = new Thickness(16, 6, 16, 5),
                VerticalAlignment = VerticalAlignment.Center,
            };

            // The app's name and the suite's, in one line of text rather than two blocks side by
            // side. Two blocks are two boxes, and centring two boxes of different font sizes
            // against each other lines up their middles, not their letters: "CamelWorks" sat
            // visibly low against "Clash Manager". One block with two runs sits them on the same
            // baseline, which is what the eye reads as level.
            var name = new TextBlock
            {
                FontSize = 17,
                VerticalAlignment = VerticalAlignment.Center,
            };

            name.Tint(TextBlock.ForegroundProperty, "Text");

            name.Inlines.Add(new System.Windows.Documents.Run(_app.Title)
            {
                FontWeight = FontWeights.SemiBold,
            });

            var suite = new System.Windows.Documents.Run("   CamelWorks")
            {
                FontSize = 12,
            };

            suite.SetResourceReference(System.Windows.Documents.Run.ForegroundProperty, "Faint");
            name.Inlines.Add(suite);

            titles.Children.Add(name);

            // The tabs sit on the title row rather than in a band under it.
            //
            // A band of its own cost a strip of window on every app for a row that is four words
            // wide, and the app's name and its tabs are the same thing said twice - which of this
            // app's screens you are on. On one line they read as one thing, and the screen below
            // gets the band back.
            _strip.VerticalAlignment = VerticalAlignment.Center;
            _strip.Margin = new Thickness(18, 0, 0, 0);
            titles.Children.Add(Ui.Loose(_strip));

            // No search box here any more. It was a tool finder in the title strip of every app,
            // and what it mostly did was sit in the corner of a screen somebody was already on,
            // looking like a search of the thing they were looking at.
            var bar = new Border
            {
                Child = titles,
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


        /// <summary>Show a tab and mark it on the strip.</summary>
        /// <param name="toolId">The tab's tool.</param>
        /// <param name="afresh">True to discard the surface this tab was left as and build it again.</param>
        private void Tab(string toolId, bool afresh = false)
        {
            var tool = Tools.Find(toolId);
            if (tool == null) return;

            _toolId = tool.Id;
            LastTab[_app.Id] = tool.Id;

            BuildStrip();

            // A built tab is kept, so coming back to one finds it as it was left. That is right
            // for navigation and wrong for a tab asking to be drawn again because a setting it
            // reads at build time has changed: the cached surface came back unchanged and the
            // setting looked like it did nothing. Three controls had that fault - the Manage
            // check picker, the Exchange kind picker and the board's fold - and each was read
            // as a control that does not work.
            if (afresh) _built.Remove(tool.Id);

            if (!_built.TryGetValue(tool.Id, out var surface))
            {
                try
                {
                    surface = Surfaces.Build(tool.Id);

                    // Only a surface that built is worth keeping. One that threw is kept out, so
                    // the next visit tries again rather than showing the same failure for ever.
                    _built[tool.Id] = surface;
                }
                catch (Exception e)
                {
                    // A surface that throws while being built would otherwise take the app with it.
                    surface = Ui.Scroll(Ui.Stack(
                        Ui.Problem("This tab could not be opened: " + Ui.Describe(e))));
                }
            }

            _surface.Content = surface;
        }

        /// <summary>
        /// Rebuild the strip: the steps of the job in order, then the tools, apart from them.
        ///
        /// An app whose tabs are one flat row reads as a sequence, and for most of them that is
        /// true. The Clash Manager's are not all steps: the matrix and the settings are gone to
        /// and come back from at any point, and listing them in line put two of them between
        /// Manage and Report. The steps stay left in their order; anything marked
        /// <see cref="AppTab.IsAside"/> goes right. An app with no aside tab draws as before.
        /// </summary>
        private void BuildStrip()
        {
            _strip.Children.Clear();

            // Built fresh on every draw rather than kept: a kept panel added to a parent twice
            // throws on the second draw, and these have no state worth carrying across one.
            var steps = new StackPanel { Orientation = Orientation.Horizontal };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };

            DockPanel.SetDock(steps, Dock.Left);
            DockPanel.SetDock(tools, Dock.Right);
            _strip.Children.Add(steps);
            _strip.Children.Add(tools);

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

                (here.IsAside ? tools : steps).Children.Add(button);
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
                Host.WhenDocumentChanges(Forget);
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
            Forget();

            if (_toolId != null) Tab(_toolId);
        }

        /// <summary>
        /// Throw every built tab away.
        ///
        /// The document changed, or the theme did. Either makes a kept surface a picture of
        /// something that is no longer true - another federation's tests, or a pane of brushes
        /// resolved against the old sheet - and showing one of those is worse than the second
        /// somebody waits for it to be built again.
        /// </summary>
        private void Forget()
        {
            _built.Clear();

            // The one on screen goes too, or the tab that is open stays stale until somebody
            // leaves it and comes back.
            if (_surface.Content != null && _toolId != null) Tab(_toolId);
        }
    }
}
