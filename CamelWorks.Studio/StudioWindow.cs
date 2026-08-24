using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelWorks.UI.Views;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace CamelWorks.Studio
{
    /// <summary>
    /// The harness window: a list of pages on the left, one of them drawn on the right at a
    /// chosen pane width.
    ///
    /// The width chooser is the point of it. A dock pane is 470 device pixels at its narrowest and
    /// most of the time that is exactly where it is left, so a layout that only works at 900 is a
    /// layout that does not work. Being able to drag between the three in a second is what makes
    /// that checkable rather than assumed.
    /// </summary>
    public sealed class StudioWindow : Window
    {
        private readonly ContentControl _stage = new ContentControl();
        private readonly ScrollViewer _scroll;
        private readonly Border _frame;
        private readonly TextBlock _caption = new TextBlock();

        private Sheet _sheet = Program.Everything[0];
        private double _width = 470;

        /// <summary>Open the harness.</summary>
        public StudioWindow()
        {
            Title = "CamelWorks — design harness";
            Width = 1180;
            Height = 940;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Skin.Apply(this);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _stage,
            };

            _frame = new Border
            {
                Width = _width,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 12, 0, 16),
                Child = _scroll,
            };

            _frame.SetResourceReference(Border.BackgroundProperty, "Pane");
            _frame.SetResourceReference(Border.BorderBrushProperty, "BdStrong");

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            layout.Children.Add(Side());

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.Children.Add(Bar());

            Grid.SetRow(_frame, 1);
            right.Children.Add(_frame);

            Grid.SetColumn(right, 1);
            layout.Children.Add(right);

            var ground = new Border { Child = layout };
            ground.SetResourceReference(Border.BackgroundProperty, "Bg");
            Content = ground;

            Skin.Changed += (s, e) => Draw();
            Draw();
        }

        private UIElement Side()
        {
            var stack = new StackPanel { Margin = new Thickness(12) };

            stack.Children.Add(new TextBlock
            {
                Text = "CamelWorks",
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 2),
            });

            stack.Children.Add(new TextBlock { Text = "Design harness", Margin = new Thickness(0, 0, 0, 14) }.Wearing("Lbl"));

            foreach (var sheet in Program.Everything)
            {
                var current = sheet;

                var button = new System.Windows.Controls.Button
                {
                    Content = sheet.Title,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 5),
                };

                button.Click += (s, e) => { _sheet = current; Draw(); };
                stack.Children.Add(button);
            }

            stack.Children.Add(new Border { Height = 16 });
            stack.Children.Add(new TextBlock { Text = "Theme" }.Wearing("Caps"));
            stack.Children.Add(new Border { Height = 6 });

            var themes = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var mode in new[] { SkinMode.Light, SkinMode.Dark })
            {
                var pick = mode;

                var button = new System.Windows.Controls.Button
                {
                    Content = mode.ToString(),
                    Margin = new Thickness(0, 0, 5, 0),
                };

                button.Click += (s, e) => Skin.Mode = pick;
                themes.Children.Add(button);
            }

            stack.Children.Add(themes);

            stack.Children.Add(new Border { Height = 16 });
            stack.Children.Add(new TextBlock
            {
                Text = "No Navisworks is loaded, so any screen that reads the host shows its own "
                       + "failure message. That is the real error path, drawn by the real code.",
            }.Wearing("Hint"));

            var side = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };

            side.SetResourceReference(Border.BackgroundProperty, "Nav");
            side.SetResourceReference(Border.BorderBrushProperty, "Bd");
            return side;
        }

        private UIElement Bar()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 12, 12, 0),
            };

            row.Children.Add(new TextBlock
            {
                Text = "Pane width",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            }.Wearing("Lbl"));

            var group = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var width in new[] { 470.0, 640.0, 900.0 })
            {
                var pick = width;

                var button = new ToggleButton
                {
                    Content = width.ToString("0", CultureInfo.InvariantCulture),
                    IsChecked = Math.Abs(width - _width) < 0.5,
                    Margin = new Thickness(0, 0, 4, 0),
                };

                button.Wearing("RailBtn");

                button.Checked += (s, e) =>
                {
                    _width = pick;
                    _frame.Width = pick;

                    foreach (var other in group.Children.OfType<ToggleButton>())
                        if (!ReferenceEquals(other, button)) other.IsChecked = false;
                };

                group.Children.Add(button);
            }

            row.Children.Add(group);

            _caption.VerticalAlignment = VerticalAlignment.Center;
            _caption.Margin = new Thickness(16, 0, 0, 0);
            _caption.Wearing("Hint");
            row.Children.Add(_caption);

            return row;
        }

        private void Draw()
        {
            _caption.Text = _sheet.Title + "  ·  " + (Skin.IsDark ? "dark" : "light");

            try
            {
                _stage.Content = _sheet.Build();
            }
            catch (Exception e)
            {
                _stage.Content = Ui.Problem("This page could not be built: " + Ui.Describe(e));
            }
        }
    }
}
