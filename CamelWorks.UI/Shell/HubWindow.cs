using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CamelWorks.UI.Views;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// The front door of the suite: the week, the search, and the way to every app.
    ///
    /// The one window that is not a tool. It answers the two questions a coordinator has before
    /// any other — "where does my Monday start" and "which of these does the thing I need" — and
    /// then gets out of the way. The search matches symptom phrasing through <see cref="Finder"/>,
    /// because nobody looking for the appearance manager types "appearance manager".
    /// </summary>
    public sealed class HubWindow : Window
    {
        private static HubWindow? _open;

        private readonly TextBox _search = new TextBox { MinWidth = 220 };
        private readonly StackPanel _results = new StackPanel();
        private readonly ContentControl _home = new ContentControl();
        private bool _hooked;

        private HubWindow()
        {
            Title = "CamelWorks";
            Width = 560;
            Height = 720;
            MinWidth = 470;
            MinHeight = 480;
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

            root.Children.Add(Brand());

            var body = new Grid();

            var page = new StackPanel();
            page.Children.Add(_results);
            page.Children.Add(_home);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = page,
                Padding = new Thickness(14, 10, 14, 14),
            };

            body.Children.Add(scroll);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var ground = new Border { Child = LeaseStrip.Over(root) };
            ground.Tint(Border.BackgroundProperty, "Bg");
            Content = ground;

            _search.TextChanged += (s, e) => Filter();
            _search.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) _search.Text = string.Empty; };

            Loaded += (s, e) => Hook(true);
            Unloaded += (s, e) => Hook(false);
            Closed += (s, e) => { Hook(false); _open = null; };

            Redraw();
        }

        /// <summary>Open the hub, or raise the one already open.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Open()
        {
            if (_open == null)
            {
                _open = new HubWindow();
                _open.Show();
                return null;
            }

            _open.Activate();
            return null;
        }

        /// <summary>
        /// The brand bar: the mark, the name, and the search — the exporter's title bar, kept, so
        /// two products from one publisher read as one.
        /// </summary>
        private UIElement Brand()
        {
            var bar = new Border { Height = 44, Padding = new Thickness(12, 0, 12, 0) };
            bar.Tint(Border.BackgroundProperty, "BrandBar");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var name = new TextBlock
            {
                Text = "CamelWorks",
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };

            grid.Children.Add(name);

            _search.Background = Brushes.Transparent;
            _search.Foreground = Brushes.White;
            _search.CaretBrush = Brushes.White;
            _search.BorderThickness = new Thickness(0);
            _search.VerticalAlignment = VerticalAlignment.Center;

            var frame = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _search,
            };

            Grid.SetColumn(frame, 1);
            grid.Children.Add(frame);

            bar.Child = grid;
            return bar;
        }

        /// <summary>Search results over the page, or the page when the box is empty.</summary>
        private void Filter()
        {
            _results.Children.Clear();

            var query = _search.Text;
            var searching = !string.IsNullOrWhiteSpace(query);

            _home.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;

            if (!searching) return;

            var hits = Finder.Search(query).Take(10).ToList();

            if (hits.Count == 0)
            {
                _results.Children.Add(Ui.Empty("Nothing matches \"" + query.Trim() + "\".",
                    "Search knows every tool by name, by other names for the same thing, and by "
                    + "the problem it solves."));
                return;
            }

            foreach (var tool in hits)
            {
                var here = tool;
                var app = Apps.Of(here.Id);

                var text = Ui.Stack(
                    Ui.Line(here.Title + (app == null ? string.Empty : "  ·  " + app.Title), 1, true),
                    Ui.Line(here.Purpose, 0.7));

                var row = new Button
                {
                    Content = text,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 2),
                };

                row.Wearing("RailRow");
                row.Click += (s, e) => Ui.Told(ToolViews.Open(here.Id));

                _results.Children.Add(row);
            }
        }

        private void Redraw() =>
            _home.Content = HomeView.Build(toolId => Ui.Told(ToolViews.Open(toolId)));

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;

            if (on) Skin.Changed += OnSkinChanged;
            else Skin.Changed -= OnSkinChanged;
        }

        private void OnSkinChanged(object? sender, EventArgs e) => Redraw();
    }
}
