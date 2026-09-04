using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Where the matrix sits in the workbook it arrived in.
    ///
    /// The reader underneath this has always been able to take a matrix from anywhere on a sheet -
    /// <see cref="SheetMapping"/> is ten one-based coordinates, and every one of them can be moved.
    /// Nothing ever let anybody move them. The tool opened the first tab of the workbook, read the
    /// ten numbers that happened to be the defaults, and either worked or said it found nothing,
    /// which on somebody else's spreadsheet is the same as not having the feature.
    ///
    /// So: the tab is chosen, the ten numbers are typed against a picture of the sheet with its own
    /// row and column numbers down the edges, and what the reader would make of it is on the screen
    /// before anything is opened. Nobody has to guess whether the matrix starts at column 7, because
    /// a wrong guess says "no categories were found" while it is still free to change.
    /// </summary>
    internal static class SheetOrigin
    {
        /// <summary>
        /// Ask where the matrix is, and read it.
        ///
        /// The mapping is edited in place, so the caller can hold onto it and open the next
        /// workbook - usually the same layout from the same practice - already pointing at the
        /// right cells.
        /// </summary>
        /// <param name="path">The workbook the user has already chosen, or null to ask here.</param>
        /// <param name="mapping">Where the matrix sits. Edited as the user types.</param>
        /// <returns>What was read when the matrix was opened, or null when it was not.</returns>
        internal static SheetMatrixResult? Ask(string? path, SheetMapping mapping)
        {
            var map = mapping ?? new SheetMapping();

            var window = new Window
            {
                Title = "CamelWorks - Where the matrix sits",
                Width = 900,
                Height = 680,
                MinWidth = 620,
                MinHeight = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };

            var tally = Ui.Stack();
            var take = Ui.Primary("Open the matrix", () => { });
            SheetMatrixResult? read = null;
            SheetMatrixResult? taken = null;

            // Fourteen by fourteen rather than the twelve by ten the schedule uses. The numbers
            // being read off this one are the top-left corner of a grid, not of a list, and the
            // default body column is already the seventh: a preview that stops at ten columns
            // shows three columns of the thing being mapped.
            var source = new SheetSource(null, 14, 14);

            void Refresh()
            {
                tally.Children.Clear();
                read = null;

                var sheet = source.Chosen;

                if (sheet == null)
                {
                    tally.Children.Add(Ui.Note("Open a workbook and choose the tab the matrix is on."));
                    take.IsEnabled = false;
                    return;
                }

                map.SheetName = sheet.Name;
                read = SheetMatrix.Read(sheet, map);

                tally.Children.Add(read.IsUsable ? Ui.Done(read.Explain()) : Ui.Problem(read.Explain()));

                foreach (var warning in read.Warnings.Take(3)) tally.Children.Add(Ui.Note(warning));

                if (read.Warnings.Count > 3)
                    tally.Children.Add(Ui.Small(
                        "and " + (read.Warnings.Count - 3).ToString(CultureInfo.InvariantCulture)
                        + " more, listed on the matrix once it is open."));

                take.IsEnabled = read.IsUsable;
            }

            source.WhenOpened(Refresh);

            take.Click += (s, e) =>
            {
                taken = read;
                window.Close();
            };

            var body = Ui.Stack(
                Bench.Group(string.Empty, source.Picker()),
                Bench.Group("Down the side", Ui.Stack(
                    Ui.Pair(Number("Name column", map.NameColumn, v => map.NameColumn = v, Refresh),
                            Number("First row", map.FirstRow, v => map.FirstRow = v, Refresh)),
                    Ui.Pair(Number("Number column", map.NumberColumn, v => map.NumberColumn = v, Refresh,
                                   "Zero when the sheet has none."),
                            Number("Code column", map.CodeColumn, v => map.CodeColumn = v, Refresh,
                                   "Zero when the sheet has none.")),
                    Number("Saved set column", map.SetColumn, v => map.SetColumn = v, Refresh,
                           "The set that backs each category. Without one, no test on it can run."))),
                Bench.Group("Across the top", Ui.Stack(
                    Ui.Pair(Number("Numbers row", map.HeaderNumberRow, v => map.HeaderNumberRow = v, Refresh,
                                   "Zero when the headings are set names only."),
                            Number("Set names row", map.HeaderSetRow, v => map.HeaderSetRow = v, Refresh,
                                   "Zero when the headings are numbers only.")),
                    Number("First grid column", map.FirstBodyColumn, v => map.FirstBodyColumn = v, Refresh,
                           "Columns are matched to categories by what heads them, never by position, "
                           + "so a spacer column between the labels and the grid costs nothing."))),
                Bench.Group("The sheet as it stands", source.Preview()));

            var root = new DockPanel { Margin = new Thickness(14, 12, 14, 12) };

            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };

            foot.Children.Add(Ui.Actions(take, Ui.Quiet("Cancel", () => window.Close())));
            DockPanel.SetDock(foot, Dock.Right);

            var feet = new DockPanel();
            feet.Children.Add(foot);
            feet.Children.Add(Ui.Loose(tally));
            DockPanel.SetDock(feet, Dock.Bottom);

            root.Children.Add(feet);
            root.Children.Add(new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            });

            var ground = new Border { Child = root };
            ground.Tint(Border.BackgroundProperty, "Bg");

            window.Content = ground;

            source.Load(path);
            Refresh();

            Skin.Apply(window);
            Shell.HostWindow.Own(window);

            window.ShowDialog();

            return taken;
        }

        /// <summary>
        /// One coordinate, one-based, as Excel shows it.
        ///
        /// Anything that is not a whole number at or above zero is simply not taken, rather than
        /// resetting the field: somebody halfway through replacing 7 with 12 has typed nothing at
        /// all for one keystroke, and a box that snapped back to 0 there would fight them.
        /// </summary>
        /// <param name="label">What the coordinate is.</param>
        /// <param name="value">What it is now.</param>
        /// <param name="set">Where a new value goes.</param>
        /// <param name="onChanged">Called after a value is taken.</param>
        /// <param name="hint">One line about it, or null.</param>
        private static Panel Number(string label, int value, Action<int> set, Action onChanged,
                                    string? hint = null) =>
            Ui.Field(label, Ui.Text(value.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    || parsed < 0) return;

                set(parsed);
                onChanged();
            }, 70), hint);
    }
}
