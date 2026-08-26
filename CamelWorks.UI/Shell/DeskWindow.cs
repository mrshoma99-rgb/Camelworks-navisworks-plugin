using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CamelWorks.UI.Views;

namespace CamelWorks.UI.Shell
{
    /// <summary>
    /// A desk: one window for one of the coordinator's five jobs.
    ///
    /// Down the left edge, a permanent rail naming every tool of the job, grouped under its stage
    /// in small caps — nothing folded, nothing tabbed, nothing hidden behind anything else. One
    /// tool is on the bench at a time; the rail says which, and one click changes it. The
    /// beside-the-model tools are on the rail too, and their rows open the dock pane, because an
    /// hour's work next to the 3D view does not belong in a window that covers it.
    ///
    /// Opening a desk again raises the one already open. A desk remembers which tool was on its
    /// bench, so the ribbon button lands where the work stopped.
    /// </summary>
    public sealed class DeskWindow : Window
    {
        private static readonly Dictionary<string, DeskWindow> Open =
            new Dictionary<string, DeskWindow>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> LastTool =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Desk _desk;
        private readonly StackPanel _rail = new StackPanel();
        private readonly ContentControl _bench = new ContentControl();
        private readonly ScrollViewer _railScroll;
        private string? _toolId;
        private bool _hooked;

        private DeskWindow(Desk desk)
        {
            _desk = desk;

            Title = "CamelWorks — " + desk.Title;
            Width = 1180;
            Height = 760;
            MinWidth = 820;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            Skin.Apply(this);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _railScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _rail,
                Padding = new Thickness(10, 10, 10, 10),
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.Children.Add(Header());

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rail = new Border
            {
                Child = _railScroll,
                BorderThickness = new Thickness(0, 0, 1, 0),
            };

            rail.Tint(Border.BackgroundProperty, "Pane");
            rail.Tint(Border.BorderBrushProperty, "Bd");
            body.Children.Add(rail);

            Grid.SetColumn(_bench, 1);
            body.Children.Add(_bench);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var ground = new Border { Child = LeaseStrip.Over(root) };
            ground.Tint(Border.BackgroundProperty, "Bg");
            Content = ground;

            // A job started anywhere must keep the rail closed while it runs: the Navisworks API
            // is main-thread-only, so the work pumps this window's dispatcher too, and switching
            // tools mid-run would tear down the surface the job is writing into.
            Loaded += (s, e) => Hook(true);
            Unloaded += (s, e) => Hook(false);
            Closed += (s, e) => { Hook(false); Open.Remove(_desk.Id); };
        }

        /// <summary>
        /// Open a desk, or raise the one already open, with a tool on the bench.
        /// </summary>
        /// <param name="deskId">Which desk.</param>
        /// <param name="toolId">Which tool, or null for wherever the desk's work stopped.</param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show(string? deskId, string? toolId = null)
        {
            var desk = Desks.Find(deskId);

            if (desk == null) return "There is no desk called \"" + (deskId ?? "nothing") + "\".";

            if (!Open.TryGetValue(desk.Id, out var window) || window == null)
            {
                window = new DeskWindow(desk);
                Open[desk.Id] = window;
                window.Show();
            }
            else
            {
                window.Activate();
            }

            window.Bench(toolId
                         ?? (LastTool.TryGetValue(desk.Id, out var last) ? last : null)
                         ?? desk.Stages[0].ToolIds[0]);

            return null;
        }

        /// <summary>
        /// The desk's name and its job, with the desk's colour under them.
        ///
        /// The colour is the family the old workspaces wore; it survives because it is the one
        /// piece of chrome that lets two open desks be told apart from across a room.
        /// </summary>
        private UIElement Header()
        {
            var titles = new StackPanel { Margin = new Thickness(16, 12, 16, 11) };

            var name = new TextBlock
            {
                Text = _desk.Title,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
            };

            name.Tint(TextBlock.ForegroundProperty, "Text");
            titles.Children.Add(name);

            var job = Ui.Sub(_desk.Job);
            job.Margin = new Thickness(0, 1, 0, 0);
            titles.Children.Add(job);

            var accent = new Border { Height = 2 };
            accent.Tint(Border.BackgroundProperty, "Fam." + _desk.Family);

            var header = new Grid();
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bar = new Border
            {
                Child = titles,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };

            bar.Tint(Border.BackgroundProperty, "Nav");
            bar.Tint(Border.BorderBrushProperty, "Bd");

            header.Children.Add(bar);
            Grid.SetRow(accent, 1);
            header.Children.Add(accent);

            return header;
        }

        /// <summary>Put a tool on the bench and mark its rail row.</summary>
        /// <param name="toolId">Which tool.</param>
        private void Bench(string toolId)
        {
            var tool = Tools.Find(toolId);
            if (tool == null) return;

            // Beside-the-model tools and the acts never take the bench: their rows route
            // elsewhere, and the bench keeps whatever was on it.
            if (tool.Shape == ToolShape.Panel)
            {
                Route(tool);
                return;
            }

            _toolId = tool.Id;
            LastTool[_desk.Id] = tool.Id;

            BuildRail();

            UIElement surface;

            try
            {
                surface = Surfaces.Build(tool.Id);
            }
            catch (Exception e)
            {
                // A surface that throws while being built would otherwise take the desk with it.
                surface = Ui.Scroll(Ui.Stack(
                    Ui.Problem("This tool could not be opened: " + Ui.Describe(e))));
            }

            _bench.Content = BenchFrame(tool, surface);
        }

        /// <summary>The compact strip over the bench: the tool's name and what it is trying to do.</summary>
        private static UIElement BenchFrame(Tool tool, UIElement surface)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

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

            name.Tint(TextBlock.ForegroundProperty, "Text");
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

            var strip = new Border
            {
                Child = titles,
                BorderThickness = new Thickness(0, 0, 0, 1),
            };

            strip.Tint(Border.BackgroundProperty, "Pane");
            strip.Tint(Border.BorderBrushProperty, "Bd");

            grid.Children.Add(strip);
            Grid.SetRow(surface, 1);
            grid.Children.Add(surface);

            return grid;
        }

        /// <summary>
        /// Rebuild the rail: every stage, every tool, the current one marked.
        /// </summary>
        private void BuildRail()
        {
            _rail.Children.Clear();

            var first = true;

            foreach (var stage in _desk.Stages)
            {
                var label = Ui.Eyebrow(stage.Title);
                label.Margin = new Thickness(8, first ? 2 : 16, 0, 5);
                _rail.Children.Add(label);
                first = false;

                foreach (var toolId in stage.ToolIds)
                {
                    var tool = Tools.Find(toolId);
                    if (tool == null) continue;

                    _rail.Children.Add(Row(tool));
                }
            }
        }

        /// <summary>
        /// One rail row. The current tool carries the desk's colour down its left edge; a
        /// beside-the-model tool says where it opens instead of pretending it will open here.
        /// </summary>
        private UIElement Row(Tool tool)
        {
            var current = string.Equals(tool.Id, _toolId, StringComparison.OrdinalIgnoreCase);

            var text = new StackPanel();

            var name = new TextBlock
            {
                Text = tool.Title,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            name.Tint(TextBlock.ForegroundProperty, "Text");
            text.Children.Add(name);

            var aside = Aside(tool);

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

            if (current) marker.Tint(Border.BackgroundProperty, "Fam." + _desk.Family);

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
                ToolTip = tool.Purpose,
            };

            button.Wearing(current ? "RailRowOn" : "RailRow");
            button.Click += (s, e) => Route(tool);

            return button;
        }

        /// <summary>What a row says under its name, or null for the ordinary case.</summary>
        private static string? Aside(Tool tool) => tool.Shape switch
        {
            ToolShape.Panel => "opens beside the model",
            ToolShape.Act => "runs now",
            _ => null,
        };

        /// <summary>Send a rail click where the tool actually lives.</summary>
        private void Route(Tool tool)
        {
            switch (tool.Shape)
            {
                case ToolShape.Panel:
                    Ui.Told(PaneRouter.Open(tool));
                    break;

                case ToolShape.Act:
                    Ui.Told(Actions.Run(ActionCommandOf(tool.Id)));
                    break;

                default:
                    if (string.Equals(tool.Id, "review", StringComparison.OrdinalIgnoreCase))
                    {
                        Ui.Told(ReviewWindow.Show());
                        break;
                    }

                    Bench(tool.Id);
                    break;
            }
        }

        /// <summary>The ribbon command an act runs as — the acts predate the desks and answer to those ids.</summary>
        private static string ActionCommandOf(string toolId) =>
            string.Equals(toolId, "baseline", StringComparison.OrdinalIgnoreCase)
                ? "ID_CW_Baseline"
                : "ID_CW_SectionBox";

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

        private void OnRunning(object? sender, int count) =>
            _railScroll.IsEnabled = count == 0;

        /// <summary>
        /// Rebuild after a theme change, for the parts built with a brush resolved at the time.
        /// </summary>
        private void OnSkinChanged(object? sender, EventArgs e)
        {
            if (_toolId != null) Bench(_toolId);
        }
    }
}
