using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The frame every tool that is not a panel opens in.
    ///
    /// One host rather than twenty-four bespoke windows, because the frame is the part that should
    /// be identical: the tool's name, the sentence saying what it is trying to do, its surface,
    /// and its one main action in the same place every time. What differs between tools is the
    /// middle, and the middle is all a tool should have to write.
    ///
    /// <b>Why windows at all.</b> These used to be tabs in a four-hundred-and-seventy-pixel dock
    /// pane, reached through a workspace switcher and a tab strip - so twenty-one ribbon buttons
    /// all opened the same panel, three of them on a tab another button already opened. A tool
    /// gets the width its work needs, and the ribbon button that opens it opens *it*.
    /// </summary>
    public static class ToolWindow
    {
        /// <summary>How wide a tool opens, unless it asks for more.</summary>
        public const int DefaultWidth = 900;

        /// <summary>How tall.</summary>
        public const int DefaultHeight = 700;

        private static readonly Dictionary<string, Window> Open =
            new Dictionary<string, Window>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Show a tool in its own window, or bring the one already open to the front.
        /// </summary>
        /// <param name="tool">Which tool.</param>
        /// <param name="body">Its surface.</param>
        /// <param name="width">How wide, or zero for the default.</param>
        /// <param name="height">How tall, or zero for the default.</param>
        /// <param name="arrange">
        /// A last word on the window, once it is on screen. Nearly nothing needs this - the point
        /// of one frame is that tools do not each invent their own. Review mode does: it is a
        /// floating palette that stays above the host, sizes itself to what it is showing and
        /// answers the keyboard, and it has to be told so after the window exists.
        /// </param>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show(Tool? tool, Func<UIElement> body, int width = 0, int height = 0,
                                   Action<Window>? arrange = null)
        {
            if (tool == null) return "There is no such tool.";
            if (body == null) return "That tool has no screen.";

            // Opening a second copy of a tool that writes to the project would let two of them
            // disagree about what is staged, so the one already open is raised instead.
            if (Open.TryGetValue(tool.Id, out var already) && already != null)
            {
                already.Activate();
                return null;
            }

            UIElement surface;

            try
            {
                surface = body();
            }
            catch (Exception e)
            {
                return "This tool could not be opened: " + Ui.Describe(e);
            }

            surface = LeaseStrip.Over(surface);

            var window = new Window
            {
                Title = "CamelWorks - " + tool.Title,
                Width = width > 0 ? width : DefaultWidth,
                Height = height > 0 ? height : DefaultHeight,
                MinWidth = 560,
                MinHeight = 420,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Content = Frame(tool, surface),
            };

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.Closed += (s, e) => Open.Remove(tool.Id);

            Open[tool.Id] = window;
            window.Show();

            arrange?.Invoke(window);

            return null;
        }

        /// <summary>
        /// The title, the purpose and the surface, in that order.
        ///
        /// The purpose line is not decoration. Every tool has always carried a sentence saying
        /// what it is for and it has always been a tooltip - which is to say it was written for
        /// somebody who already knew enough to hover over the right thing.
        ///
        /// Public so the design harness can put a reconstruction inside the real frame rather than
        /// beside a drawing of one. The frame is most of what a person sees when a tool opens, and
        /// a picture that leaves it out is a picture of something else.
        /// </summary>
        /// <param name="tool">Whose window it is.</param>
        /// <param name="surface">What goes under the header.</param>
        /// <returns>The window's content.</returns>
        public static UIElement Frame(Tool? tool, UIElement surface)
        {
            if (tool == null) return surface;

            var grid = new Grid();

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var heading = new StackPanel();

            heading.Children.Add(Ui.Heading(tool.Title));
            heading.Children.Add(Ui.Sub(tool.Purpose));

            var header = new Border
            {
                Padding = new Thickness(16, 13, 16, 13),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = heading,
            };

            header.Tint(Border.BackgroundProperty, "Nav");
            header.Tint(Border.BorderBrushProperty, "Bd");

            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var body = new Border { Padding = new Thickness(16, 12, 16, 16), Child = surface };

            Grid.SetRow(body, 1);
            grid.Children.Add(body);

            var ground = new Border { Child = grid };

            ground.Tint(Border.BackgroundProperty, "Bg");

            return ground;
        }
    }
}
