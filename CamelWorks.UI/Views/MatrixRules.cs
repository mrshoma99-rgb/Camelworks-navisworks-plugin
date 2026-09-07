using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// A matrix is agreed in sentences and was previously only filled in cells. "All services
    /// against all architecture, clearance at twenty-five" is one line of a kick-off minute and it
    /// governs sixty cells. Folders and a block fill wrote those sixty once; a discipline added the
    /// following week arrived blank, nothing said it should not be, and the grid stopped matching
    /// what was agreed.
    ///
    /// Kept as rules, the instruction stays in force: a line added later arrives checked the way its
    /// folder is checked, and a rule that is edited or dropped takes its own cells with it rather
    /// than leaving them as decisions nobody made.
    ///
    /// The list is read from the bottom. The general instruction is written first and the exception
    /// after it, so the later line is the more specific one and wins. The order is therefore
    /// editable, which is what the arrows are for.
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

            // "Anything" is a folder like any other, at the top of both lists. "All services
            // against everything" is as ordinary as a folder-to-folder pair, and writing it as one
            // rule per folder is the same maintenance problem one layer up.
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

                var rows = new List<TableRow>();

                for (var at = 0; at < board.Rules.Count; at++)
                {
                    var rule = board.Rules[at];

                    rows.Add(new TableRow(at,
                        (at + 1).ToString(CultureInfo.CurrentCulture), rule.Worded())
                    {
                        // The one that wins is worth saying out loud, because "the last one wins"
                        // is a fact about a list that a list does not show.
                        Tone = at == board.Rules.Count - 1 && board.Rules.Count > 1
                            ? Tone.Good : Tone.Plain,
                    });
                }

                var table = Ui.Table(rows, ("Read", 52), ("Rule", 0));

                table.MaxHeight = 220;

                // Move up, move down and remove, over whatever is ticked, rather than three small
                // buttons on every row. Several rules move at once, and the tick beside each one
                // says what the next press covers.
                var verbs = new Ui.OnPicked[]
                {
                    new Ui.OnPicked("Move up {n}", (picked, said) => Shift(picked, -1, said)),
                    new Ui.OnPicked("Move down {n}", (picked, said) => Shift(picked, 1, said)),
                    new Ui.OnPicked("Remove these {n}", (picked, said) => Drop(picked, said),
                                    danger: true),
                };

                list.Children.Add(Ui.Stack(table, Ui.Picking(table, verbs)));
            }

            void Shift(IReadOnlyList<object> picked, int by, Panel said)
            {
                var at = picked.OfType<int>().OrderBy(one => one * by).ToList();

                if (at.Count == 0)
                {
                    said.Children.Add(Ui.Note("Tick a rule to move."));
                    return;
                }

                foreach (var one in at) board.MoveRule(one, one + by);

                touched = true;
                Redraw();
            }

            void Drop(IReadOnlyList<object> picked, Panel said)
            {
                // Highest index first, so removing one does not renumber the next.
                var at = picked.OfType<int>().OrderByDescending(one => one).ToList();

                if (at.Count == 0)
                {
                    said.Children.Add(Ui.Note("Tick a rule to remove."));
                    return;
                }

                foreach (var one in at) board.RemoveRule(one);

                touched = true;
                Redraw();
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

            body.Children.Add(new TextBlock { Text = "The rules, in the order they are read" }
                .Wearing("Caps"));

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
                // Done closes the window; the rules are written as they are added and removed.
                Child = Ui.Actions(null, Ui.Button("Done", () => window.Close())),
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            var writing = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(10, 9, 10, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = Ui.Stack(new TextBlock { Text = "Add a rule" }.Wearing("Caps"), adding),
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
