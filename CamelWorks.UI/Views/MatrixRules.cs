using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The standing instructions that fill a matrix, in one window.
    ///
    /// <b>A matrix is agreed in sentences and was only ever filled in cells.</b> "All services
    /// against all architecture, clearance at twenty-five" is one line of a kick-off minute and
    /// it governs sixty cells. Folders and a block fill wrote those sixty - once, on the
    /// afternoon somebody did it. The discipline added the following week arrived blank, nothing
    /// said it should not be, and the grid quietly stopped saying what the room agreed.
    ///
    /// Kept as rules, the sentence goes on being true: a line added later arrives already
    /// checked the way its folder is checked, and a rule that is edited or dropped takes its own
    /// cells with it rather than leaving them behind as decisions nobody made.
    ///
    /// <b>The list is read from the bottom.</b> People write the general instruction first and
    /// the exception after it, so the later line is the more specific one and it wins. The order
    /// is therefore something to change, which is what the arrows are for.
    /// </summary>
    internal static class MatrixRules
    {
        /// <summary>
        /// Show the rules and let them be changed.
        /// </summary>
        /// <param name="board">The board, whose rules are edited in place.</param>
        /// <param name="symbols">The legend's marks, which are what a rule can lay down.</param>
        /// <returns>True when something changed, so the caller can apply and redraw.</returns>
        internal static bool Ask(MatrixBoard board, IReadOnlyList<string> symbols)
        {
            if (board == null) return false;

            var touched = false;

            var window = new Window
            {
                Title = "CamelWorks - Fill rules",
                Width = 560,
                Height = 520,
                MinWidth = 460,
                MinHeight = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var list = Ui.Stack();

            // <b>Anything is a folder like any other, at the top of both lists.</b> "All services
            // against everything" is as ordinary a sentence as a folder-to-folder pair, and
            // writing it as one rule per folder is the same maintenance problem one layer up.
            var folders = new List<string> { Anything };

            folders.AddRange(board.Folders(rows: true));

            foreach (var one in board.Folders(rows: false))
                if (!folders.Contains(one, StringComparer.OrdinalIgnoreCase)) folders.Add(one);

            var marks = new List<string>(symbols ?? Array.Empty<string>());

            if (!marks.Contains(MatrixBoard.NotRequired)) marks.Add(MatrixBoard.NotRequired);

            void Redraw()
            {
                list.Children.Clear();

                if (board.Rules.Count == 0)
                {
                    var none = Ui.Small(
                        "No rules yet. A rule is a sentence about two folders - “all "
                        + "services against all architecture, B” - and it goes on being true "
                        + "as the matrix grows.");

                    none.TextWrapping = TextWrapping.Wrap;
                    none.Margin = new Thickness(2, 6, 2, 6);

                    list.Children.Add(none);
                    return;
                }

                for (var at = 0; at < board.Rules.Count; at++)
                {
                    var here = at;
                    var rule = board.Rules[at];

                    var line = new DockPanel
                    {
                        LastChildFill = true,
                        Margin = new Thickness(0, 0, 0, 6),
                    };

                    var acts = Ui.Across(
                        Ui.Quiet("↑", () =>
                        {
                            board.MoveRule(here, here - 1);
                            touched = true;
                            Redraw();
                        }),
                        Ui.Quiet("↓", () =>
                        {
                            board.MoveRule(here, here + 1);
                            touched = true;
                            Redraw();
                        }),
                        Ui.Quiet("Remove", () =>
                        {
                            board.RemoveRule(here);
                            touched = true;
                            Redraw();
                        }));

                    DockPanel.SetDock(acts, Dock.Right);
                    line.Children.Add(acts);

                    var said = new TextBlock
                    {
                        Text = rule.Worded(),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                    }.Wearing("Body");

                    // The one that wins is worth saying out loud, because "the last one wins" is
                    // a fact about a list that a list does not show.
                    if (here == board.Rules.Count - 1 && board.Rules.Count > 1)
                        said.FontWeight = FontWeights.SemiBold;

                    line.Children.Add(said);

                    list.Children.Add(line);
                }
            }

            Redraw();

            var sideA = Ui.Choice(folders, Anything, null, 150);
            var sideB = Ui.Choice(folders, Anything, null, 150);
            var mark = Ui.Choice(marks, marks.Count > 0 ? marks[0] : null, null, 90);

            var adding = Ui.Across(
                sideA,
                new TextBlock
                {
                    Text = "against",
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Hint"),
                sideB,
                new TextBlock
                {
                    Text = "→",
                    Margin = new Thickness(8, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }.Wearing("Hint"),
                mark,
                Ui.Quiet("Add", () =>
                {
                    if (!(mark.SelectedItem is string symbol) || symbol.Length == 0) return;

                    board.AddRule(new MatrixRule(Folder(sideA.SelectedItem as string),
                                                 Folder(sideB.SelectedItem as string), symbol));

                    touched = true;
                    Redraw();
                }));

            var body = Ui.Stack();

            body.Children.Add(Ui.Eyebrow("The rules, in the order they are read"));

            var order = Ui.Small("The last one that covers a pair is the one that wins, so put "
                                 + "the exceptions below the general rule.");

            order.TextWrapping = TextWrapping.Wrap;
            order.Margin = new Thickness(0, 2, 0, 8);

            body.Children.Add(order);
            body.Children.Add(list);

            var root = new DockPanel { Margin = new Thickness(16, 14, 16, 14) };

            var foot = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Child = Ui.Actions(Ui.Primary("Done", () => window.Close())),
            };

            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            var writing = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(10, 9, 10, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = Ui.Stack(Ui.Eyebrow("Add a rule"), adding),
            };

            writing.Tint(Border.BackgroundProperty, "Card2");
            writing.Tint(Border.BorderBrushProperty, "Bd");

            DockPanel.SetDock(writing, Dock.Bottom);
            root.Children.Add(writing);

            root.Children.Add(new ScrollViewer
            {
                Content = Ui.Loose(body),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });

            var ground = new Border { Child = root };

            ground.Tint(Border.BackgroundProperty, "Bg");
            window.Content = ground;

            Skin.Apply(window);
            HostWindow.Own(window);

            window.ShowDialog();

            return touched;
        }

        /// <summary>What the wildcard is called where a folder name would be.</summary>
        private const string Anything = "Anything";

        private static string Folder(string? picked) =>
            picked == null || string.Equals(picked, Anything, StringComparison.Ordinal)
                ? string.Empty
                : picked;
    }
}
