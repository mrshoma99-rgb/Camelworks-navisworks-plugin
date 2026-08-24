using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The coordination matrix: read one out of the spreadsheet it lives in, say what its symbols
    /// mean, and make the clash tests.
    ///
    /// <b>Why this is a window.</b> Everything else in this product is a dock pane four hundred and
    /// seventy pixels wide, and that is the right shape for reading — a board, a verdict, a list of
    /// findings. It is the wrong shape for authoring. A matrix is a square with the same headings
    /// down the side and across the top, a symbol in each cell and a key beside it; squeezed into a
    /// pane it stops being a matrix and becomes two dropdowns, which is exactly what it had become.
    /// The tools that read live in the pane. The tools that author get a window.
    /// </summary>
    public static class MatrixWindow
    {
        private static Window? _open;

        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            if (_open != null)
            {
                _open.Activate();
                return null;
            }

            var window = new Window
            {
                Title = "CamelWorks - clash matrix",
                Width = 1080,
                Height = 720,
                MinWidth = 780,
                MinHeight = 540,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Content = new Editor(session).View,
            };

            window.Closed += (_, __) => _open = null;

            _open = window;
            window.Show();

            return null;
        }

        /// <summary>One editing session over one matrix.</summary>
        private sealed class Editor
        {
            private readonly Session _session;

            private readonly SheetMapping _mapping = new SheetMapping();
            private readonly StackPanel _grid = Ui.Stack();
            private readonly StackPanel _legendRows = Ui.Stack();
            private readonly StackPanel _warnings = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly SheetSource _sheet = new SheetSource();

            private MatrixGrid _matrix = new MatrixGrid();
            private MatrixLegend _legend = new MatrixLegend();
            private MatrixBuild? _plan;

            internal Editor(Session session)
            {
                _session = session;
                View = Build();
            }

            /// <summary>The window's content.</summary>
            internal UIElement View { get; }

            private UIElement Build()
            {
                var columns = new Grid();
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
                columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var left = Ui.Scroll(Ui.Stack(Source(), Ui.Rule(), Mapping(), Ui.Rule(), Preview()));
                Grid.SetColumn(left, 0);
                columns.Children.Add(left);

                var right = Ui.Scroll(Ui.Stack(Board(), Ui.Rule(), Legend(), Ui.Rule(), Make()));
                Grid.SetColumn(right, 1);
                columns.Children.Add(right);

                return columns;
            }

            // ---------------------------------------------------------------------------------
            // Left: where the matrix is, and what the sheet looks like
            // ---------------------------------------------------------------------------------

            private UIElement Source() =>
                Ui.Stack(
                    Ui.Eyebrow("The spreadsheet"),
                    _sheet.Picker(),
                    Ui.Small("Nobody's matrix is laid out like anybody else's, so nothing is assumed "
                             + "about where things are. The preview below is the sheet as it stands; "
                             + "read the row and column numbers off it and fill them in."));

            private UIElement Preview() =>
                Ui.Stack(Ui.Eyebrow("The sheet as it stands"), _sheet.Preview());

            private UIElement Mapping()
            {
                var rows = Ui.Stack(
                    Ui.Eyebrow("Where things are"),
                    Ui.Pair(Number("Name column", _mapping.NameColumn, v => _mapping.NameColumn = v),
                            Number("Set column", _mapping.SetColumn, v => _mapping.SetColumn = v)),
                    Ui.Pair(Number("Number column", _mapping.NumberColumn, v => _mapping.NumberColumn = v),
                            Number("Code column", _mapping.CodeColumn, v => _mapping.CodeColumn = v)),
                    Ui.Pair(Number("First category row", _mapping.FirstRow, v => _mapping.FirstRow = v),
                            Number("First grid column", _mapping.FirstBodyColumn, v => _mapping.FirstBodyColumn = v)),
                    Ui.Pair(Number("Numbers across the top", _mapping.HeaderNumberRow, v => _mapping.HeaderNumberRow = v),
                            Number("Sets across the top", _mapping.HeaderSetRow, v => _mapping.HeaderSetRow = v)),
                    Ui.Small("A zero means the sheet does not have that one. The columns across the "
                             + "top are matched back to the categories down the side by number, then "
                             + "by set name - never by position, so a spacer column costs nothing."));

                var read = Ui.Runner("Read the matrix", _warnings, Read);

                return Ui.Stack(rows, Ui.Actions(read));
            }

            private Panel Number(string label, int value, Action<int> set) =>
                Ui.Field(label, Ui.Text(value.ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        && parsed >= 0) set(parsed);
                }, 70));

            // ---------------------------------------------------------------------------------
            // Right: the matrix, its legend, and the tests
            // ---------------------------------------------------------------------------------

            private void Read(Job job)
            {
                job.Say("Reading the sheet...");

                var read = SheetMatrix.Read(_sheet.Chosen, _mapping);

                _matrix = read.Grid;
                _legend = _legend.Covering(read.Grid);
                _plan = null;

                foreach (var warning in read.Warnings) _warnings.Children.Add(Ui.Note(warning));

                _warnings.Children.Insert(0, Ui.Line(read.Explain(), 1, true));

                ShowGrid();
                ShowLegend();
                _output.Children.Clear();
            }

            private UIElement Board() => Ui.Stack(Ui.Eyebrow("The matrix"), _grid, _warnings);

            private void ShowGrid()
            {
                _grid.Children.Clear();

                if (_matrix.Categories.Count == 0)
                {
                    _grid.Children.Add(Ui.Empty("Nothing read yet.",
                        "Open a workbook, say where the matrix sits in it, and read it."));
                    return;
                }

                var table = new Grid();
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

                foreach (var _ in _matrix.Categories)
                    table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                for (var c = 0; c < _matrix.Categories.Count; c++)
                {
                    var head = Ui.Small(Short(_matrix.Categories[c]));
                    head.HorizontalAlignment = HorizontalAlignment.Center;
                    head.ToolTip = _matrix.Categories[c].Name;
                    Grid.SetRow(head, 0);
                    Grid.SetColumn(head, c + 1);
                    table.Children.Add(head);
                }

                for (var r = 0; r < _matrix.Categories.Count; r++)
                {
                    table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var label = Ui.Line(_matrix.Categories[r].ToString(), 0.85);
                    label.TextTrimming = TextTrimming.CharacterEllipsis;
                    label.ToolTip = _matrix.Categories[r].SetName.Length > 0
                        ? "Backed by \"" + _matrix.Categories[r].SetName + "\""
                        : "No saved set backs this, so no test on it can run.";
                    Grid.SetRow(label, r + 1);
                    Grid.SetColumn(label, 0);
                    table.Children.Add(label);

                    for (var c = 0; c < _matrix.Categories.Count; c++)
                    {
                        // Only the upper triangle is editable. The lower half is the same cell seen
                        // from the other side, and offering two boxes for one value is how a matrix
                        // comes to disagree with itself.
                        if (c < r) continue;

                        var row = r;
                        var column = c;

                        var box = Ui.Text(_matrix.At(r, c) ?? string.Empty,
                            text =>
                            {
                                _matrix.Set(row, column, text);
                                _legend = _legend.Covering(_matrix);
                                _plan = null;
                                ShowLegend();
                                _output.Children.Clear();
                            }, 36);

                        box.TextAlignment = TextAlignment.Center;
                        box.Margin = new Thickness(1);

                        Grid.SetRow(box, r + 1);
                        Grid.SetColumn(box, c + 1);
                        table.Children.Add(box);
                    }
                }

                _grid.Children.Add(table);
                _grid.Children.Add(Ui.Small("Only the top half is editable: the bottom half is the "
                                            + "same pair seen from the other side. The diagonal is a "
                                            + "category against itself, which finds the duct running "
                                            + "through its own hanger."));
            }

            private static string Short(MatrixCategory category) =>
                category.Number.Length > 0 ? category.Number
                    : category.Code.Length > 0 ? category.Code
                    : category.Name.Length > 3 ? category.Name.Substring(0, 3) : category.Name;

            private UIElement Legend() => Ui.Stack(Ui.Eyebrow("What the symbols mean"), _legendRows);

            private void ShowLegend()
            {
                _legendRows.Children.Clear();

                var symbols = _legend.Symbols
                    .Where(s => _matrix.SymbolsUsed.Contains(s.Symbol, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (symbols.Count == 0)
                {
                    _legendRows.Children.Add(Ui.Small("No symbols in the grid yet."));
                    return;
                }

                foreach (var symbol in symbols)
                {
                    var current = symbol;

                    var kind = Ui.Choice(Enum.GetNames(typeof(MatrixTest)), current.Test.ToString(),
                        picked => Replace(current.Symbol, picked, null, null), 160);

                    var tolerance = Ui.Text(
                        current.Tolerance.ToString("0.###", CultureInfo.InvariantCulture),
                        text => Replace(current.Symbol, null, text, null), 70);

                    var on = Ui.Check("Make it", current.Enabled,
                                      value => Replace(current.Symbol, null, null, value));

                    var row = Ui.Across(Ui.Field("Symbol", Frozen(current.Symbol)),
                                        Ui.Field("Test", kind),
                                        Ui.Field("Tolerance (m)", tolerance),
                                        on);

                    _legendRows.Children.Add(row);
                }

                _legendRows.Children.Add(Ui.Small("A symbol nobody has described arrives as an "
                                                  + "ordinary hard clash, so a sheet reads before its "
                                                  + "key has been typed in. Correct the ones that are "
                                                  + "not hard clashes."));
            }

            private static TextBlock Frozen(string text)
            {
                var block = Ui.Line(text, 1, true);
                block.VerticalAlignment = VerticalAlignment.Center;
                return block;
            }

            private void Replace(string symbol, string? test, string? tolerance, bool? enabled)
            {
                var was = _legend.Meaning(symbol);
                if (was == null) return;

                var kind = was.Test;

                if (test != null && Enum.TryParse<MatrixTest>(test, out var parsed)) kind = parsed;

                var metres = was.Tolerance;

                if (tolerance != null
                    && double.TryParse(tolerance, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed)
                    && typed >= 0) metres = typed;

                var kept = _legend.Symbols.Where(s => !string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList();
                kept.Add(new MatrixSymbol(symbol, kind, metres, enabled ?? was.Enabled));

                _legend = new MatrixLegend(kept);
                _plan = null;
                _output.Children.Clear();
            }

            private UIElement Make()
            {
                var commit = Ui.Commit("Show me the tests it would make", "Create", "test",
                    plan: (job, into) =>
                    {
                        _plan = MatrixPlanner.Plan(_matrix, _legend, Existing());

                        into.Children.Add(Ui.Line(_plan.Explain(), 1, true));

                        if (_plan.Skipped.Count > 0)
                            foreach (var why in _plan.Skipped.Take(8))
                                into.Children.Add(Ui.Note(why));

                        if (_plan.Tests.Count > 0)
                        {
                            var rows = _plan.Tests.Take(200)
                                .Select(t => new TableRow(t, t.Name, t.Test.ToString(),
                                    t.Tolerance.ToString("0.###", CultureInfo.InvariantCulture) + " m"))
                                .ToList();

                            into.Children.Add(Ui.Table(rows,
                                ("Test", 300), ("Kind", 140), ("Tolerance", 100)));
                        }

                        return _plan.Tests.Count;
                    },
                    apply: job => Create(job));

                return Ui.Stack(Ui.Eyebrow("Make the tests"), commit, _output);
            }

            private IReadOnlyCollection<string> Existing() =>
                _session.Clash.IsAvailable
                    ? _session.Clash.Tests().Select(t => t.Name).ToList()
                    : (IReadOnlyCollection<string>)Array.Empty<string>();

            private void Create(Job job)
            {
                var plan = _plan;
                if (plan == null) return;

                var clash = _session.Clash;

                if (!clash.IsAvailable || !clash.CanEdit)
                {
                    _output.Children.Add(Ui.Problem(
                        "This build of Navisworks will not let a plug-in create clash tests. "
                        + "The matrix and its legend are still yours to save."));
                    return;
                }

                var made = 0;

                foreach (var test in plan.Tests)
                {
                    if (!job.Step(made, plan.Tests.Count, "tests created")) break;
                    if (clash.Create(test.Spec()) != null) made++;
                }

                _output.Children.Add(Ui.Line("Created " + Ui.Count(made, "test") + ".", 1, true));

                if (made < plan.Tests.Count)
                    _output.Children.Add(Ui.Note(Ui.Count(plan.Tests.Count - made, "test")
                        + " could not be created. " + (clash.LastProblem ?? "The host did not say why.")));

                _session.Record(ActivityKind.Regroup,
                    "created " + Ui.Count(made, "clash test") + " from the matrix");
            }

        }
    }
}
