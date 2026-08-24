using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using CamelWorks.UI.Views;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// The pane's chrome: a brand bar with search, a workspace switcher, a tab strip and the
    /// content.
    ///
    /// Built in code rather than XAML because the layout is driven by <see cref="Workspaces"/> —
    /// markup would either duplicate that list or bind to it through a pile of templates, and the
    /// list is the thing three separate parts of the product have to agree on. What it is painted
    /// with is not in here at all: every colour and control shape comes from <see cref="Skin"/>,
    /// which is the same token set the BIMCamel IFC exporter uses, so the two panes docked beside
    /// each other look like one product rather than two.
    ///
    /// <b>It collapses by label, never to glyphs.</b> Below about 900 device-independent pixels the
    /// switcher becomes a labelled dropdown. A row of unlabelled icons saves the same space and
    /// costs the user the ability to find anything, which is the opposite of what a product with
    /// this much surface needs.
    /// </summary>
    public sealed class ShellView : UserControl
    {
        /// <summary>Below this width the switcher collapses to a dropdown.</summary>
        public const double NarrowWidth = 900;

        private readonly string _paneId;
        private readonly StackPanel _switcher = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly ToggleButton _switcherNarrow = new ToggleButton();
        private readonly Popup _menu = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        private readonly ListBox _menuList = new ListBox { MaxHeight = 420, MinWidth = 250 };
        private readonly TextBlock _purpose = new TextBlock();
        private readonly Border _purposeBar;
        private readonly StackPanel _tabs = new StackPanel { Orientation = Orientation.Horizontal };
        private Border? _tabRule;
        private ScrollViewer? _tabsHost;
        private System.Windows.Controls.Primitives.Popup? _pop;
        private readonly Border _rail;
        private readonly TextBox _search = new TextBox { MinWidth = 130, Tag = "Find a tool" };
        private readonly ListBox _searchResults = new ListBox { MaxHeight = 260 };
        private readonly Border _results;
        private readonly ContentControl _content = new ContentControl();
        private readonly TextBlock _where = new TextBlock();
        private readonly ProgressBar _busy = new ProgressBar { Height = 3, Visibility = Visibility.Collapsed };
        private readonly System.Windows.Controls.Button _theme = new System.Windows.Controls.Button();

        private Workspace _workspace;
        private WorkspaceTab? _tab;
        private bool _narrow;

        /// <summary>Create the shell for one pane.</summary>
        /// <param name="paneId">Which pane this is, so it shows only its own workspaces.</param>
        public ShellView(string paneId)
        {
            _paneId = paneId ?? Workspaces.MainPane;
            _workspace = Workspaces.All.First(w => w.PaneId == _paneId);

            Skin.Apply(this);

            // Display-mode formatting and layout rounding are what stop 11px labels from going
            // fuzzy on a 100% display, which is what most of these panes are read on.
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Brand());

            Grid.SetRow(_busy, 1);
            root.Children.Add(_busy);

            // The workspace and its tabs share one row.
            //
            // They were two stacked strips, which spent about fifty pixels of a pane that is often
            // only six hundred tall saying twice over where you are. The workspace name is the
            // heading and the tabs are its children, so a divider between them reads them as
            // equals; side by side with a rule between, the hierarchy is the layout.
            _rail = new Border { Padding = new Thickness(8, 5, 8, 5), Child = SwitcherRow() };
            _rail.Tint(Border.BackgroundProperty, "Nav");
            _rail.Tint(Border.BorderBrushProperty, "Bd");
            _rail.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid.SetRow(_rail, 2);
            root.Children.Add(_rail);


            // One line saying what the tab is for, on the screen rather than in a tooltip.
            //
            // Workspaces.cs has always carried this sentence for every one of the nineteen tabs,
            // and it has always been a tooltip - which is to say it was written for somebody who
            // already knew enough to hover. A row of nouns is exactly what makes a first-time user
            // feel lost, and twenty pixels is what it costs to stop that.
            _purposeBar = new Border { Padding = new Thickness(11, 5, 11, 6), Child = PurposeRow() };
            _purposeBar.Tint(Border.BackgroundProperty, "Nav");
            _purposeBar.Tint(Border.BorderBrushProperty, "Bd");
            _purposeBar.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid.SetRow(_purposeBar, 4);
            root.Children.Add(_purposeBar);

            var body = new Grid();
            body.Children.Add(_content);

            _results = new Border
            {
                Child = _searchResults,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 3,
                    Opacity = 0.22,
                },
            };

            _results.Tint(Border.BackgroundProperty, "Card");
            _results.Tint(Border.BorderBrushProperty, "Bd");

            // The results hang off the search box, not off the screen.
            //
            // They used to be a panel pinned to the top-right of the content area, which in a pane
            // this narrow put them a long way from the box that produced them - far enough that
            // they read as something the current screen had done. A Popup placed under its own
            // field is the ordinary behaviour of a search box, and it draws over the content
            // instead of pushing it, so nothing below moves while you type.
            //
            // AllowsTransparency carries the rounded corners and the shadow; without it the popup
            // paints a square opaque window behind both.
            _pop = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = _search,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                HorizontalOffset = -8,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
                Child = _results,
            };

            // In the logical tree, so the palette keys inside it resolve. A Popup renders in its
            // own window but still looks its resources up through its parent.
            body.Children.Add(_pop);

            Grid.SetRow(body, 5);
            root.Children.Add(body);

            var ground = new Border { Child = root };
            ground.Tint(Border.BackgroundProperty, "Pane");
            Content = ground;

            _switcherNarrow.Checked += (s, e) => _menu.IsOpen = true;
            _switcherNarrow.Unchecked += (s, e) => _menu.IsOpen = false;
            _menu.Closed += (s, e) => _switcherNarrow.IsChecked = false;
            _menuList.SelectionChanged += (s, e) => PickNarrow();

            _search.TextChanged += (s, e) => Filter();
            _search.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Clear(); };
            _searchResults.SelectionChanged += (s, e) => Jump();

            // The list is at least as wide as the field it drops from, and never wider than the
            // pane. A fixed width looked deliberate at one pane size and wrong at the other two.
            _search.SizeChanged += (s, e) => _searchResults.MinWidth = Math.Max(0, _search.ActualWidth);
            SizeChanged += (s, e) => Reflow(e.NewSize.Width);

            // A job started on one tab must still look like it is running after you switch to
            // another. Otherwise the answer to "did I actually press it?" is to press it again,
            // which is how two exports end up fighting over one file.
            //
            // Hooked on Loaded and unhooked on Unloaded, both, rather than hooked once in here:
            // these are static events, and a pane that unhooks when it is hidden and never hooks
            // again comes back with a progress strip that has quietly stopped working.
            Loaded += (s, e) => Hook(true);
            Unloaded += (s, e) => Hook(false);

            BuildSwitcher();
            Show(_workspace.Id, null);
        }

        /// <summary>Bring the shell to a workspace and tab.</summary>
        /// <param name="workspaceId">The workspace, or null to stay where it is.</param>
        /// <param name="tabId">The tab, or null for the workspace's first.</param>
        public void Show(string? workspaceId, string? tabId)
        {
            var workspace = Workspaces.Find(workspaceId);

            // A route into the other pane's workspace is ignored rather than obeyed. The ribbon
            // opens the right pane first; obeying it here as well would leave both panes showing
            // the same screen.
            if (workspace != null && workspace.PaneId == _paneId) _workspace = workspace;

            _tab = _workspace.Tabs.FirstOrDefault(
                       t => string.Equals(t.Id, tabId, StringComparison.OrdinalIgnoreCase))
                   ?? _workspace.Tabs.FirstOrDefault();

            BuildSwitcher();
            BuildTabs();
            ShowContent();
        }

        /// <summary>
        /// The brand bar.
        ///
        /// It carries the product name, where you are, the tool search and the theme control — the
        /// four things that are true of every screen. Everything below it belongs to one workspace;
        /// this row belongs to the product, and looking like the exporter's title bar is the point,
        /// not a coincidence.
        /// </summary>
        private UIElement Brand()
        {
            var bar = new Border { Height = 44, Padding = new Thickness(10, 0, 10, 0) };
            bar.Tint(Border.BackgroundProperty, "BrandBar");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Mark(),
            };

            grid.Children.Add(mark);

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 12, 0) };

            titles.Children.Add(new TextBlock
            {
                Text = "CamelWorks",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
            });

            _where.Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
            _where.FontSize = 10.5;
            titles.Children.Add(_where);

            Grid.SetColumn(titles, 1);
            grid.Children.Add(titles);

            var search = SearchBox();
            Grid.SetColumn(search, 2);
            grid.Children.Add(search);

            _theme.Content = Ui.Glyph(Glyphs.Theme, 15, "Text");
            _theme.Wearing("IconBtn");
            _theme.ToolTip = Skin.ModeName;
            _theme.Click += (s, e) =>
            {
                Skin.Next();
                _theme.ToolTip = Skin.ModeName;
            };

            // The glyph is white on the gradient, which is one of the two places in the pane where
            // a colour is not a palette key: the bar is the brand colour in both themes.
            if (_theme.Content is Viewbox box && box.Child is System.Windows.Shapes.Path path)
                path.Stroke = Brushes.White;

            Grid.SetColumn(_theme, 3);
            grid.Children.Add(_theme);

            bar.Child = grid;
            return bar;
        }

        private Border? _searchBox;

        private Border SearchBox()
        {
            if (_searchBox != null) return _searchBox;

            _search.Background = Brushes.Transparent;
            _search.Foreground = Brushes.White;
            _search.CaretBrush = Brushes.White;
            _search.BorderThickness = new Thickness(0);
            _search.VerticalAlignment = VerticalAlignment.Center;

            // The brand bar is the one ground in the pane that is not a palette key, so the search
            // field on it is spelled out rather than tinted: white at low alpha reads on both ends
            // of the gradient and in both themes.
            var frame = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,

                // Capped, and not by much. A tool search wide enough to hold a sentence is wider
                // than any tool is named, and at the pane's narrowest it was taking the room the
                // theme control needed.
                MaxWidth = 240,
                Margin = new Thickness(0, 0, 8, 0),
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glass = Ui.Glyph(Glyphs.Find, 14, "Text", new Thickness(0, 0, 7, 0));
            if (glass.Child is System.Windows.Shapes.Path path) path.Stroke = Brushes.White;
            row.Children.Add(glass);

            Grid.SetColumn(_search, 1);
            row.Children.Add(_search);

            frame.Child = row;
            _searchBox = frame;
            return frame;
        }

        /// <summary>
        /// The switcher, wide and narrow.
        ///
        /// The narrow form used to be a bare dropdown, and 470 device pixels is where most people
        /// leave this pane — so the common case was the one where every workspace colour, and with
        /// it any sense of which part of the product you were in, disappeared. It is a marked chip
        /// now, and what it opens says what each workspace is for rather than only naming it.
        /// </summary>
        /// <summary>
        /// The BIMCamel mark, drawn as a white silhouette.
        ///
        /// The mark ships as near-black ink on transparency, which is right on paper and invisible
        /// on a blue header. Rather than keep a second, lighter copy of the same artwork in sync,
        /// the alpha channel is used as an opacity mask over a white fill: one file, and it comes
        /// out the right colour on any ground we put it on.
        ///
        /// Best-effort. A missing or unreadable file falls back to the letters, because a brand
        /// mark that fails to load should cost the header its polish, not its identity.
        /// </summary>
        private static UIElement Mark()
        {
            try
            {
                var beside = System.IO.Path.GetDirectoryName(
                    typeof(ShellView).Assembly.Location) ?? string.Empty;

                var file = System.IO.Path.Combine(beside, "Resources", "BimCamelMark.png");

                if (System.IO.File.Exists(file))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(file);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    return new System.Windows.Shapes.Rectangle
                    {
                        Width = 18,
                        Height = 18,
                        Fill = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        OpacityMask = new ImageBrush(bitmap) { Stretch = Stretch.Uniform },
                    };
                }
            }
            catch (Exception)
            {
                // Fall through to the letters.
            }

            return new TextBlock
            {
                Text = "CW",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private UIElement SwitcherRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            _switcherNarrow.Wearing("RailBtn");
            _switcherNarrow.Visibility = Visibility.Collapsed;
            _switcherNarrow.Padding = new Thickness(9, 4, 9, 4);

            _menu.PlacementTarget = _switcherNarrow;

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 4, 0, 8),
                Child = _menuList,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Opacity = 0.24,
                },
            };

            frame.Tint(Border.BackgroundProperty, "Card");
            frame.Tint(Border.BorderBrushProperty, "Bd");
            _menu.Child = frame;

            row.Children.Add(_switcherNarrow);
            row.Children.Add(_switcher);

            var rule = new Border
            {
                Width = 1,
                Margin = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            rule.Tint(Border.BackgroundProperty, "Bd");

            _tabRule = rule;
            row.Children.Add(rule);

            // The tabs scroll rather than wrap. A second line of tabs would put the row back to the
            // two-row height this change exists to reclaim.
            _tabsHost = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalAlignment = VerticalAlignment.Center,
                Content = _tabs,
            };

            var scroller = _tabsHost;
            scroller.PreviewMouseWheel += (s, e) =>
            {
                scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
                e.Handled = true;
            };

            row.Children.Add(scroller);
            return row;
        }

        private UIElement PurposeRow()
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(Ui.Glyph(Glyphs.Info, 13, "Faint", new Thickness(0, 0, 7, 0)));

            _purpose.TextWrapping = TextWrapping.Wrap;
            _purpose.VerticalAlignment = VerticalAlignment.Center;
            _purpose.Wearing("Hint");

            Grid.SetColumn(_purpose, 1);
            row.Children.Add(_purpose);
            return row;
        }

        /// <summary>A workspace's colour dot beside its name.</summary>
        private static UIElement Dotted(Workspace workspace, bool withPurpose)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Fam." + workspace.Id);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = workspace.Title, FontWeight = FontWeights.SemiBold });

            // On the chip, the count. A docked pane is narrow at every width anybody docks it at,
            // so this chip is the only door to the other six workspaces — and "Home ⌄" on its own
            // reads as a breadcrumb rather than as the thing you press to find everything else.
            if (!withPurpose)
                text.Children.Add(new TextBlock
                {
                    Text = Ui.Count(Workspaces.All.Count, "workspace"),
                    Margin = new Thickness(0, 1, 0, 0),
                }.Wearing("Hint"));

            if (withPurpose)
                text.Children.Add(new TextBlock
                {
                    Text = workspace.Tabs.FirstOrDefault()?.Summary ?? string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 260,
                    Margin = new Thickness(0, 1, 0, 0),
                }.Wearing("Hint"));

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(text);

            // Only on the chip. In the menu the row is the choice, and a chevron on each of seven
            // choices would say that every one of them opens something further.
            if (!withPurpose) row.Children.Add(Ui.Glyph(Glyphs.Down, 12, "Muted", new Thickness(8, 1, 0, 0)));

            return row;
        }

        private void BuildSwitcher()
        {
            _switcher.Children.Clear();
            _menuList.Items.Clear();

            _where.Text = _workspace.Title + (_tab == null || !_workspace.HasTabStrip ? string.Empty : "  ·  " + _tab.Title);
            _switcherNarrow.Content = Dotted(_workspace, withPurpose: false);

            foreach (var workspace in Workspaces.All.Where(w => w.PaneId == _paneId))
            {
                var current = workspace;

                // A dot in the workspace's own colour, matching the ribbon icon for the same
                // section. Eight identical grey labels is what "all the tools look the same"
                // looks like in a switcher.
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Fam." + workspace.Id);

                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(dot);
                label.Children.Add(new TextBlock { Text = workspace.Title, VerticalAlignment = VerticalAlignment.Center });

                var button = new ToggleButton
                {
                    Content = label,
                    IsChecked = ReferenceEquals(workspace, _workspace),
                    Margin = new Thickness(0, 0, 3, 0),
                    ToolTip = workspace.Tabs.FirstOrDefault()?.Summary,
                };

                button.Wearing("RailBtn");
                button.Checked += (s, e) => Show(current.Id, null);
                _switcher.Children.Add(button);

                _menuList.Items.Add(new ListBoxItem
                {
                    Content = Dotted(workspace, withPurpose: true),
                    Tag = workspace.Id,
                    IsSelected = ReferenceEquals(workspace, _workspace),
                });
            }
        }

        private void BuildTabs()
        {
            _tabs.Children.Clear();

            var strip = _workspace.HasTabStrip ? Visibility.Visible : Visibility.Collapsed;
            if (_tabRule != null) _tabRule.Visibility = strip;
            if (_tabsHost != null) _tabsHost.Visibility = strip;

            if (!_workspace.HasTabStrip) return;

            foreach (var tab in _workspace.Tabs)
            {
                var current = tab;

                var button = new ToggleButton
                {
                    Content = tab.Title,
                    IsChecked = ReferenceEquals(tab, _tab),
                    ToolTip = tab.Summary,
                };

                button.Wearing("TabBtn");
                button.Checked += (s, e) => Show(_workspace.Id, current.Id);
                _tabs.Children.Add(button);
            }
        }

        private void ShowContent()
        {
            var purpose = _workspace.HasTabStrip ? _tab?.Summary ?? string.Empty : string.Empty;

            _purpose.Text = purpose;
            _purposeBar.Visibility = purpose.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            _content.Content = TabViews.For(_workspace, _tab, Go);
        }

        /// <summary>
        /// Navigate to a "workspace/tab" route, for the screens that offer a way onward.
        ///
        /// Home is the reason this exists: a front door whose three steps do not take you to them
        /// is a poster, not a front door.
        /// </summary>
        private void Go(string route)
        {
            var (workspace, tab) = Workspaces.Route(route);
            if (workspace != null) Show(workspace.Id, tab?.Id);
        }

        private bool _hooked;

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
            var running = count > 0;

            // Left indeterminate only while it is shown: an animating bar behind a collapsed
            // element is work nobody can see.
            _busy.IsIndeterminate = running;
            _busy.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

            // Navigation is closed while work is running.
            //
            // The Navisworks API is main-thread-only, so a job runs on the UI thread and keeps the
            // pane alive by pumping the dispatcher. Pumping processes input, which is what makes
            // Stop clickable — and also what would let someone change tab or workspace half way
            // through a traversal, tearing down the screen the job is still writing into.
            //
            // Stop lives inside the content and stays enabled, so this closes the way out without
            // closing the way to cancel.
            _switcher.IsEnabled = !running;
            _switcherNarrow.IsEnabled = !running;
            _tabs.IsEnabled = !running;
            _search.IsEnabled = !running;

            if (running && _pop != null) _pop.IsOpen = false;
        }

        /// <summary>
        /// Rebuild after a theme change.
        ///
        /// Almost everything repaints itself, because it is bound to a palette key rather than to a
        /// brush. The exceptions are the parts of a screen built with a brush resolved at the time
        /// — a tone-tinted table row, a graph node — and rebuilding the current screen is cheaper
        /// than hunting for them.
        /// </summary>
        private void OnSkinChanged(object? sender, EventArgs e)
        {
            _theme.ToolTip = Skin.ModeName;
            ShowContent();
        }

        private void Reflow(double width)
        {
            var narrow = width > 0 && width < NarrowWidth;
            if (narrow == _narrow) return;

            _narrow = narrow;
            _switcher.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            _switcherNarrow.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PickNarrow()
        {
            if (!(_menuList.SelectedItem is ListBoxItem item) || !(item.Tag is string id)) return;
            if (string.Equals(id, _workspace.Id, StringComparison.Ordinal)) return;

            _menu.IsOpen = false;
            Show(id, null);
        }

        private void Filter()
        {
            var query = _search.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                if (_pop != null) _pop.IsOpen = false;
                return;
            }

            _searchResults.Items.Clear();

            foreach (var command in CommandCatalogue.Search(query).Take(12))
                _searchResults.Items.Add(new ListBoxItem { Content = Result(command), Tag = command });

            if (_pop != null) _pop.IsOpen = _searchResults.Items.Count > 0;
        }

        /// <summary>
        /// One search hit: what the tool is called, and which part of the product it is in.
        ///
        /// The section matters as much as the name. "Report" is four different things in a
        /// coordination tool, and a list of bare titles makes the user open three of them.
        /// </summary>
        private static UIElement Result(RibbonCommand command)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = command.Title, FontWeight = FontWeights.SemiBold });

            var route = command.Route;

            if (!string.IsNullOrEmpty(route))
            {
                var where = route!.Replace("/", "  ·  ");
                stack.Children.Add(new TextBlock { Text = where }.Wearing("Hint"));
            }

            return stack;
        }

        private void Clear()
        {
            _search.Text = string.Empty;
            if (_pop != null) _pop.IsOpen = false;
        }

        private void Jump()
        {
            var item = _searchResults.SelectedItem as ListBoxItem;
            var command = item?.Tag as RibbonCommand;
            if (command?.Route == null) return;

            var (workspace, tab) = Workspaces.Route(command.Route);
            if (workspace == null) return;

            Clear();
            Show(workspace.Id, tab?.Id);
        }
    }
}
