using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CamelWorks.UI.Views;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// The pane's chrome: a brand bar with search, the open tool's name, and the tool.
    ///
    /// <b>One tool at a time, and no tabs.</b> This used to carry a workspace switcher over a tab
    /// strip - seven workspaces, eighteen tabs - and twenty-one of the twenty-seven ribbon buttons
    /// opened this same pane on a different one of them. Three opened a tab another button already
    /// opened. The result was that every tool looked like the same form with different fields, and
    /// the ribbon was a menu wearing a ribbon's clothes.
    ///
    /// Now a ribbon button opens its own tool. Six tools are panels and come up here, because
    /// their work genuinely happens beside the model for an hour at a time; everything else opens
    /// its own window. At rest this shows the launcher: every tool, what it is for, and what it
    /// will do.
    ///
    /// What it is painted with is not in here at all: every colour and control shape comes from
    /// <see cref="Skin"/>, the same token set the BIMCamel IFC exporter uses, so two panes docked
    /// beside each other look like one product rather than two.
    /// </summary>
    public sealed class ShellView : UserControl
    {
        private readonly string _paneId;
        private readonly TextBlock _purpose = new TextBlock();
        private readonly Border _purposeBar;
        private readonly System.Windows.Controls.Button _back = new System.Windows.Controls.Button();
        private readonly TextBlock _open = new TextBlock();
        private System.Windows.Controls.Primitives.Popup? _pop;
        private readonly Border _rail;
        private readonly TextBox _search = new TextBox { MinWidth = 130, Tag = "Find a tool" };
        private readonly ListBox _searchResults = new ListBox { MaxHeight = 260 };
        private readonly Border _results;
        private readonly ContentControl _content = new ContentControl();
        private readonly TextBlock _where = new TextBlock();
        private readonly ProgressBar _busy = new ProgressBar { Height = 3, Visibility = Visibility.Collapsed };
        private readonly System.Windows.Controls.Button _theme = new System.Windows.Controls.Button();

        private Tool? _tool;

        /// <summary>Create the shell for one pane.</summary>
        /// <param name="paneId">Which pane this is.</param>
        public ShellView(string paneId)
        {
            _paneId = paneId ?? MainPane;

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

            // One row saying which tool is open, and the way back to all of them. What was here
            // before was a switcher over a tab strip, which spent about fifty pixels of a pane
            // often only six hundred tall on navigation between screens that a ribbon button
            // already opens directly.
            _rail = new Border { Padding = new Thickness(8, 5, 8, 5), Child = HeaderRow() };
            _rail.Tint(Border.BackgroundProperty, "Nav");
            _rail.Tint(Border.BorderBrushProperty, "Bd");
            _rail.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid.SetRow(_rail, 2);
            root.Children.Add(_rail);


            // One line saying what the tool is for, on the screen rather than in a tooltip.
            //
            // Every tool has always carried this sentence and it has always been a tooltip - which
            // is to say it was written for somebody who already knew enough to hover over the
            // right thing. A row of nouns is what makes a first-time user feel lost, and twenty
            // pixels is what it costs to stop that.
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

            _search.TextChanged += (s, e) => Filter();
            _search.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Clear(); };
            _searchResults.SelectionChanged += (s, e) => Jump();

            // The list is at least as wide as the field it drops from, and never wider than the
            // pane. A fixed width looked deliberate at one pane size and wrong at the other two.
            _search.SizeChanged += (s, e) => _searchResults.MinWidth = Math.Max(0, _search.ActualWidth);

            // A job started in one tool must still look like it is running after you open
            // another. Otherwise the answer to "did I actually press it?" is to press it again,
            // which is how two exports end up fighting over one file.
            //
            // Hooked on Loaded and unhooked on Unloaded, both, rather than hooked once in here:
            // these are static events, and a pane that unhooks when it is hidden and never hooks
            // again comes back with a progress strip that has quietly stopped working.
            Loaded += (s, e) => Hook(true);
            Unloaded += (s, e) => Hook(false);

            Show(null);
        }

        /// <summary>The main pane's id.</summary>
        public const string MainPane = "CamelWorks";

        /// <summary>The graph editor's pane id. Separate because the canvas needs full width.</summary>
        public const string AutomatePane = "CamelWorks.Automate";

        /// <summary>
        /// Show a tool in this pane, or the launcher when there is none.
        /// </summary>
        /// <param name="toolId">Which tool, or null for the launcher.</param>
        public void Show(string? toolId)
        {
            var tool = Tools.Find(toolId);

            // Only panel tools live in a pane. Anything else arriving here is a routing mistake,
            // and showing the launcher is a better answer than showing the wrong tool.
            _tool = tool != null && tool.Shape == ToolShape.Panel ? tool : null;

            BuildHeader();
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

        /// <summary>
        /// Which tool is open, and the way back to all of them.
        ///
        /// Two controls where there were a switcher, a dropdown for when the pane is narrow, and a
        /// scrolling strip of tabs. There is nothing to switch between here any more: a ribbon
        /// button opens the tool it names, and this row says which one answered.
        /// </summary>
        private UIElement HeaderRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            // QuietBtn, not RailBtn. RailBtn targets ToggleButton - it dressed the workspace
            // switcher, which was a set of toggles and no longer exists - and WPF refuses a style
            // whose TargetType is derived from the element's own type. Applying it to a Button
            // threw at layout, which took the whole dock pane with it: Triage, Changes, Data,
            // Sets, Appearance and the graph all failed to open on "'ToggleButton' TargetType does
            // not match type of element 'Button'".
            _back.Wearing("QuietBtn");
            _back.Padding = new Thickness(9, 4, 9, 4);
            _back.Margin = new Thickness(0, 0, 8, 0);
            _back.ToolTip = "Home — the week, the desks and the search";
            _back.Click += (s, e) => Show(null);

            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(Ui.Glyph(Glyphs.Backward, 13, "Muted", new Thickness(0, 0, 6, 0)));
            label.Children.Add(new TextBlock
            {
                Text = "Home",
                VerticalAlignment = VerticalAlignment.Center,
            });

            _back.Content = label;

            _open.VerticalAlignment = VerticalAlignment.Center;
            _open.FontWeight = FontWeights.SemiBold;
            _open.TextTrimming = TextTrimming.CharacterEllipsis;

            row.Children.Add(_back);
            row.Children.Add(_open);

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

        private void BuildHeader()
        {
            var launcher = _tool == null;

            _open.Text = launcher ? "Home" : _tool!.Title;
            _where.Text = launcher ? "Home" : _tool!.Title;

            // At the launcher there is nowhere back to, so the button that goes there is not
            // offered - a control that does nothing is worse than one that is not there.
            _back.Visibility = launcher ? Visibility.Collapsed : Visibility.Visible;
            _open.Margin = new Thickness(launcher ? 2 : 0, 0, 0, 0);
        }

        private void ShowContent()
        {
            var purpose = _tool?.Purpose ?? string.Empty;

            _purpose.Text = purpose;
            _purposeBar.Visibility = purpose.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            _content.Content = LeaseStrip.Over(ToolViews.Panel(_tool, Go));
        }

        /// <summary>
        /// Open a tool by name, from the launcher or from a screen that offers a way onward.
        ///
        /// A panel tool takes over this pane; anything else opens its own window. Either way the
        /// caller says a tool's name and does not have to know which of the two it is.
        /// </summary>
        /// <param name="toolId">Which tool.</param>
        private void Go(string toolId)
        {
            var problem = ToolViews.Open(toolId, tool => Show(tool.Id));

            if (problem != null) Ui.Told(problem);
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

            // The way out is closed while work is running.
            //
            // The Navisworks API is main-thread-only, so a job runs on the UI thread and keeps the
            // pane alive by pumping the dispatcher. Pumping processes input, which is what makes
            // Stop clickable — and also what would let someone leave for another tool half way
            // through a traversal, tearing down the screen the job is still writing into.
            //
            // Stop lives inside the content and stays enabled, so this closes the way out without
            // closing the way to cancel.
            _back.IsEnabled = !running;
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

        private void Filter()
        {
            var query = _search.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                if (_pop != null) _pop.IsOpen = false;
                return;
            }

            _searchResults.Items.Clear();

            foreach (var tool in Finder.Search(query).Take(12))
                _searchResults.Items.Add(new ListBoxItem { Content = Result(tool), Tag = tool });

            if (_pop != null) _pop.IsOpen = _searchResults.Items.Count > 0;
        }

        /// <summary>
        /// One search hit: what the tool is called, and what it is trying to do.
        ///
        /// Only the sentence tells "Report" apart from the three other reports a coordination
        /// tool has, so it is the sentence that is shown.
        /// </summary>
        private static UIElement Result(Tool tool)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = tool.Title, FontWeight = FontWeights.SemiBold });

            stack.Children.Add(new TextBlock
            {
                Text = tool.Purpose,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
            }.Wearing("Hint"));

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
            var tool = item?.Tag as Tool;

            if (tool == null) return;

            Clear();
            Go(tool.Id);
        }
    }
}
