using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Data;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A workbook, a sheet inside it, and a look at the corner of that sheet.
    ///
    /// Two tools now read a matrix out of a spreadsheet somebody already has - the clash matrix and
    /// the set schedule - and both need exactly this before they can begin: open the file, choose
    /// the tab, and see enough of the top-left to read row and column numbers off it into a mapping.
    ///
    /// One implementation, because the audit that started this work found four copies of the same
    /// diff rendering that had already drifted into three slightly different tables. The second copy
    /// is where that starts, so there is no second copy.
    /// </summary>
    internal sealed class SheetSource
    {
        private readonly TextBox _file;
        private readonly ComboBox _sheets = Ui.Choice(Array.Empty<string>(), null, null, 200);
        private readonly StackPanel _preview = Ui.Stack();
        private readonly StackPanel _trouble = Ui.Stack();

        private readonly int _rows;
        private readonly int _columns;

        private WorkbookTables? _book;
        private Action? _opened;

        /// <summary>Create one.</summary>
        /// <param name="onOpened">Called when a workbook has been read and a sheet chosen.</param>
        /// <param name="rows">How many rows of the corner to show.</param>
        /// <param name="columns">How many columns of the corner to show.</param>
        internal SheetSource(Action? onOpened = null, int rows = 12, int columns = 10)
        {
            _file = Ui.Text(string.Empty, "The workbook it is in", _ => { }, 320);
            _rows = Math.Max(1, rows);
            _columns = Math.Max(1, columns);
            _opened = onOpened;

            _sheets.SelectionChanged += (s, e) => { ShowPreview(); _opened?.Invoke(); };
        }

        /// <summary>
        /// Also call this when a workbook has been read and a sheet chosen.
        ///
        /// A screen whose handler needs the picker - one that reads the chosen sheet the moment it
        /// changes - cannot pass that handler to the constructor, because the handler does not
        /// exist until the picker does.
        /// </summary>
        /// <param name="onOpened">What to call. Added to whatever is already there.</param>
        internal void WhenOpened(Action? onOpened)
        {
            if (onOpened == null) return;

            var already = _opened;
            _opened = already == null ? onOpened : (Action)(() => { already(); onOpened(); });
        }

        /// <summary>The sheet the user has chosen, or null when none is open.</summary>
        internal SheetTable? Chosen =>
            _book == null ? null
                : _sheets.SelectedItem is string name ? _book.Sheet(name)
                : _book.First;

        /// <summary>
        /// Open a workbook the caller already has the path to.
        ///
        /// A tool that asked for the file before it opened this - the matrix does, because the
        /// press that starts it is "open a spreadsheet" - would otherwise show an empty path field
        /// beside a file the user has already chosen, and ask them to choose it again.
        /// </summary>
        /// <param name="path">The workbook's path. Anything empty is ignored.</param>
        internal void Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _file.Text = path!.Trim();
            Open();
        }

        /// <summary>The picker: the path, the button, and the sheet.</summary>
        internal UIElement Picker()
        {
            var open = Ui.Icon(Glyphs.Folder, "Open it", Open);

            return Ui.Stack(
                Ui.PathField("Workbook", _file, PathKind.Open,
                             "Excel workbook|*.xlsx;*.xlsm|All files|*.*"),
                Ui.Actions(open),
                Ui.Field("Sheet", _sheets),
                _trouble);
        }

        /// <summary>The corner of the sheet, with row and column numbers along the edges.</summary>
        internal UIElement Preview() => _preview;

        private void Open()
        {
            _trouble.Children.Clear();
            _preview.Children.Clear();
            _sheets.Items.Clear();
            _book = null;

            var path = _file.Text.Trim();

            if (path.Length == 0 || !File.Exists(path))
            {
                _trouble.Children.Add(Ui.Problem("There is no file at that path."));
                return;
            }

            try
            {
                _book = XlsxReader.Read(File.ReadAllBytes(path));
            }
            catch (IOException error)
            {
                _trouble.Children.Add(Ui.Problem("Could not open it: " + error.Message));
                return;
            }
            catch (UnauthorizedAccessException error)
            {
                _trouble.Children.Add(Ui.Problem("Could not open it: " + error.Message));
                return;
            }

            if (!_book.IsUsable)
            {
                _trouble.Children.Add(Ui.Problem(_book.Problem ?? "Nothing could be read from that file."));
                _book = null;
                return;
            }

            // Items, not ItemsSource: Ui.Choice fills Items, and WPF refuses ItemsSource on a box
            // that has any.
            foreach (var sheet in _book.Sheets) _sheets.Items.Add(sheet.Name);

            _sheets.SelectedIndex = 0;

            ShowPreview();
            _opened?.Invoke();
        }

        /// <summary>
        /// The sheet as it stands, numbered.
        ///
        /// The numbers along the edges are the whole point. Filling in a mapping means reading
        /// coordinates off the sheet, and a tool that shows the cells but not their positions has
        /// sent the user back to Excel to do the one thing it asked them for.
        /// </summary>
        private void ShowPreview()
        {
            var rows = _rows;
            var columns = _columns;

            _preview.Children.Clear();

            var sheet = Chosen;
            if (sheet == null) return;

            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            for (var c = 0; c < columns; c++)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });

            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var c = 0; c < columns; c++)
            {
                var head = Ui.Small((c + 1).ToString(CultureInfo.InvariantCulture));
                head.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetRow(head, 0);
                Grid.SetColumn(head, c + 1);
                table.Children.Add(head);
            }

            for (var r = 0; r < rows; r++)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var head = Ui.Small((r + 1).ToString(CultureInfo.InvariantCulture));
                head.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetRow(head, r + 1);
                Grid.SetColumn(head, 0);
                table.Children.Add(head);

                for (var c = 0; c < columns; c++)
                {
                    var text = Cell(sheet, r + 1, c + 1);
                    if (text.Length == 0) continue;

                    var cell = Ui.Small(text.Length > 12 ? text.Substring(0, 11) + "…" : text);
                    cell.ToolTip = text;
                    Grid.SetRow(cell, r + 1);
                    Grid.SetColumn(cell, c + 1);
                    table.Children.Add(cell);
                }
            }

            _preview.Children.Add(table);
        }

        private static string Cell(SheetTable sheet, int row, int column)
        {
            if (row <= 0 || column <= 0 || row > sheet.Rows.Count) return string.Empty;

            var cells = sheet.Rows[row - 1];
            var text = column > cells.Count ? null : cells[column - 1];

            return text ?? string.Empty;
        }
    }
}
