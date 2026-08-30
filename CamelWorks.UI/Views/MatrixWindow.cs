using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The coordination matrix: build one, read one out of the model's own tests, or read one out
    /// of the spreadsheet it lives in — then make the clash tests it asks for.
    ///
    /// Three ways in, because that is how matrices actually arrive: drawn fresh at a kick-off
    /// (categories picked from the sets and models the federation already has, never typed), or
    /// already living in the document as tests somebody set up by hand, or sent over as the
    /// client's own workbook with its own symbols and a key in the corner. Whichever way it came,
    /// it is the same grid, the same legend, and the same "make the tests" at the end — and it
    /// goes back out as an Excel file for whoever asks.
    /// </summary>
    public static class MatrixWindow
    {
        private const string ByHand = "By hand";
        private const string FromModel = "The model's tests";
        private const string FromSheet = "A spreadsheet";

        // The matrix survives tab switches, like every other bench in this app: an afternoon's
        // grid lost to a glance at the Rules tab is an afternoon nobody offers this tool again.
        private static MatrixGrid _matrix = new MatrixGrid();
        private static MatrixLegend _legend = new MatrixLegend();
        private static string _mode = ByHand;

        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Editor(session).View;

        /// <summary>One editing session over the matrix.</summary>
        private sealed class Editor
        {
            private readonly Session _session;

            private readonly SheetMapping _mapping = new SheetMapping();
            private readonly StackPanel _grid = Ui.Stack();
            private readonly StackPanel _catList = Ui.Stack();
            private readonly StackPanel _legendRows = Ui.Stack();
            private readonly StackPanel _warnings = Ui.Stack();
            private readonly StackPanel _fromModel = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly StackPanel _said = Ui.Stack();
            private readonly SheetSource _sheet = new SheetSource();

            private readonly TextBox _pattern;
            private readonly TextBox _folder;
            private readonly ComboBox _addSet;
            private readonly ComboBox _addModel;

            private const string OneSet = "A saved set";
            private const string WholeModel = "A whole model";
            private string _addKind = OneSet;

            /// <summary>Whether a sheet read has been attempted, as opposed to whether one worked.</summary>
            private bool _read;

            private MatrixBuild? _plan;

            internal Editor(Session session)
            {
                _session = session;
                _pattern = Ui.Text("{a} vs {b}", null, 170);
                _folder = Ui.Text(string.Empty, null, 170);
                _addSet = Pickers.Set(string.Empty, null, 190);
                _addModel = Ui.Choice(
                    session.Model.Models.Select(m => m.DisplayName).ToList(),
                    session.Model.Models.Select(m => m.DisplayName).FirstOrDefault() ?? string.Empty,
                    null, 190);

                View = Build();
            }

            /// <summary>The window's content.</summary>
            internal UIElement View { get; }

            /// <summary>
            /// Three decisions, in the order each one needs the last: where the matrix comes
            /// from, what it says, and the tests it makes. Each step's surface is built once and
            /// handed back on every visit, so stepping back does not throw work away.
            /// </summary>
            private UIElement Build()
            {
                var host = new ContentControl();

                var byHand = Ui.Stack(Adder());
                var model = Ui.Stack(
                    Bench.Ready("Reading rebuilds the matrix from the clash tests this document "
                                + "already has: each test's two sides become categories, and each "
                                + "distinct kind-and-tolerance becomes a legend symbol.",
                                "Tests whose sides are hand-picked selections cannot be read back "
                                + "as categories; they are listed, not guessed at."),
                    Ui.Actions(Ui.Runner("Read the tests", _fromModel, primary: false,
                                         work: ReadModel)),
                    _fromModel);
                var sheet = Ui.Stack(Source(), Ui.Rule(), Mapping(), Ui.Rule(), Preview());

                void Show(string mode)
                {
                    _mode = mode;
                    host.Content = null;
                    host.Content = mode == FromSheet ? sheet : mode == FromModel ? model : byHand;
                }

                var source = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Segmented(new[] { ByHand, FromModel, FromSheet }, _mode, Show)),
                    host);

                Show(_mode);

                var matrix = Ui.Stack(
                    Ui.Eyebrow("The categories"), _catList,
                    Ui.Rule(),
                    _grid,
                    Ui.Rule(),
                    Legend(),
                    Ui.Actions(Ui.Quiet("Excel...", ExportExcel)),
                    _said);

                var tests = Make();

                ShowGrid();
                ShowLegend();
                ShowCategories();

                var steps = new[]
                {
                    new Ui.Step("Where it comes from", () => source,
                                () => _matrix.Categories.Count > 0 ? null
                                      : _mode == ByHand
                                          ? "Add at least one category first."
                                          : _mode == FromModel
                                              ? "Read the model's tests first."
                                              : _read
                                                  ? "That read found no categories. Check the row "
                                                    + "and column numbers against the preview - "
                                                    + "they are the numbers Excel shows down the "
                                                    + "side and along the top."
                                                  : "Read the matrix out of the sheet first."),
                    new Ui.Step("The matrix", () => matrix,
                                () => _matrix.Filled == 0
                                      ? "Nothing in the grid to make tests from." : null),
                    new Ui.Step("The tests", () => tests),
                };

                return Ui.Steps(steps, null, null);
            }

            // ---------------------------------------------------------------------------------
            // By hand: categories picked from what the model has
            // ---------------------------------------------------------------------------------

            private UIElement Adder()
            {
                var kindHost = new ContentControl();

                void Kind(string pick)
                {
                    _addKind = pick;
                    kindHost.Content = null;
                    kindHost.Content = pick == WholeModel
                        ? Ui.Field("Model", _addModel)
                        : Ui.Field("Set", _addSet, "search and selection sets, from the model");
                }

                Kind(_addKind);

                return Bench.Group("Add a category",
                    Ui.Field("Backed by", Ui.Choice(new[] { OneSet, WholeModel }, _addKind, Kind, 150)),
                    kindHost,
                    Ui.Actions(Ui.Quiet("Add", AddCategory)),
                    Ui.Small("A category is a row and a column at once - the same list runs down "
                             + "the side and across the top. Fill the grid's cells on the next "
                             + "step; each symbol's meaning is set in the legend there."));
            }

            private void AddCategory()
            {
                _said.Children.Clear();

                MatrixCategory category;

                if (_addKind == WholeModel)
                {
                    var name = (_addModel.Text ?? string.Empty).Trim();

                    var scope = _session.Model.Models
                        .Where(m => string.Equals(m.DisplayName, name, StringComparison.Ordinal))
                        .Select(m => m.Scope)
                        .FirstOrDefault();

                    if (scope == null)
                    {
                        Said(Ui.Problem("Pick one of the loaded models."));
                        return;
                    }

                    category = new MatrixCategory(name, scopes: new[] { scope });
                }
                else
                {
                    var name = (_addSet.Text ?? string.Empty).Trim();

                    if (name.Length == 0)
                    {
                        Said(Ui.Problem("Pick or type the set's name first."));
                        return;
                    }

                    category = new MatrixCategory(name, setName: name);
                }

                if (_matrix.Categories.Any(c =>
                        string.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    Said(Ui.Problem("\"" + category.Name + "\" is already a category."));
                    return;
                }

                _matrix.Add(category);
                _plan = null;

                ShowGrid();
                ShowCategories();
                Said(Ui.Done("Added \"" + category.Name + "\"."));
            }

            private void ShowCategories()
            {
                _catList.Children.Clear();

                if (_matrix.Categories.Count == 0)
                {
                    _catList.Children.Add(Ui.Small("No categories yet. They arrive from the "
                        + "first step - by hand, from the model's tests, or from the sheet."));
                    return;
                }

                for (var i = 0; i < _matrix.Categories.Count; i++)
                {
                    var at = i;
                    var category = _matrix.Categories[at];

                    _catList.Children.Add(Ui.Across(
                        Ui.Line(category.ToString(), 0.85),
                        Ui.Line(category.Scopes.Count > 0
                            ? "a whole model"
                            : category.IsUnbacked
                                ? "backed by nothing"
                                : "set \"" + category.SetName + "\"", 0.6),
                        Ui.Icon(Glyphs.Close, "Take it out, cells and all", () =>
                        {
                            _matrix.RemoveAt(at);
                            _legend = _legend.Covering(_matrix);
                            _plan = null;
                            ShowGrid();
                            ShowLegend();
                            ShowCategories();
                        })));
                }
            }

            // ---------------------------------------------------------------------------------
            // From the model: the matrix the tests already describe
            // ---------------------------------------------------------------------------------

            private void ReadModel(Job job)
            {
                if (!_session.Clash.IsAvailable)
                {
                    _fromModel.Children.Add(Ui.Problem(_session.ClashProblem
                        ?? "This Navisworks edition has no clash engine to read tests from."));
                    return;
                }

                var grid = new MatrixGrid();
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var legend = new List<MatrixSymbol>();
                var unread = new List<string>();
                var seen = 0;
                var made = 0;

                int Category(string name, string? setName, IReadOnlyList<string> scopes)
                {
                    if (index.TryGetValue(name, out var at)) return at;

                    at = grid.Add(new MatrixCategory(name, setName, scopes: scopes));
                    index[name] = at;
                    return at;
                }

                foreach (var test in _session.Clash.Tests())
                {
                    if (!job.Step(++seen, "tests read")) return;

                    var sides = _session.Clash.Sides(test.Id);

                    if (sides == null || !sides.KnowsA || !sides.KnowsB)
                    {
                        unread.Add(test.Name);
                        continue;
                    }

                    var a = sides.SetA != null
                        ? Category(sides.SetA, sides.SetA, Array.Empty<string>())
                        : Category(ScopeName(sides.ScopesA), null, sides.ScopesA);

                    var b = sides.SetB != null
                        ? Category(sides.SetB, sides.SetB, Array.Empty<string>())
                        : Category(ScopeName(sides.ScopesB), null, sides.ScopesB);

                    // One symbol per distinct kind-and-tolerance, lettered in the order they
                    // are met - exactly how a person would draw the key.
                    var combo = sides.Kind + "|"
                                + sides.ToleranceMetres.ToString("0.####", CultureInfo.InvariantCulture);

                    if (!symbols.TryGetValue(combo, out var symbol))
                    {
                        symbol = made < 26
                            ? ((char)('A' + made)).ToString()
                            : "S" + (made + 1).ToString(CultureInfo.InvariantCulture);

                        made++;
                        symbols[combo] = symbol;

                        legend.Add(new MatrixSymbol(symbol,
                            Enum.TryParse<MatrixTest>(sides.Kind, true, out var kind)
                                ? kind
                                : MatrixTest.Hard,
                            sides.ToleranceMetres));
                    }

                    grid.Set(a, b, symbol);
                }

                _matrix = grid;
                _legend = new MatrixLegend(legend);
                _plan = null;

                ShowGrid();
                ShowLegend();
                ShowCategories();

                _fromModel.Children.Add(Ui.Line(Ui.Count(grid.Categories.Count, "category") + ", "
                    + Ui.Count(grid.Filled, "filled cell") + " and "
                    + Ui.Count(legend.Count, "legend symbol") + " read out of "
                    + Ui.Count(seen, "test") + ".", 1, true));

                if (unread.Count > 0)
                    _fromModel.Children.Add(Ui.Note(Ui.Count(unread.Count, "test") + " could not "
                        + "be read back as matrix cells, because a side is a hand-picked "
                        + "selection rather than a set or a whole model: "
                        + string.Join(", ", unread.Take(5))
                        + (unread.Count > 5 ? ", ..." : ".")));
            }

            private string ScopeName(IReadOnlyList<string> scopes)
            {
                var names = _session.Model.Models
                    .Where(m => scopes.Contains(m.Scope, StringComparer.Ordinal))
                    .Select(m => m.DisplayName)
                    .ToList();

                return names.Count == 0 ? "(a model no longer loaded)" : string.Join(" + ", names);
            }

            // ---------------------------------------------------------------------------------
            // From the sheet: the client's own workbook
            // ---------------------------------------------------------------------------------

            private UIElement Source() =>
                Ui.Stack(
                    Ui.Eyebrow("The spreadsheet"),
                    _sheet.Picker());

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
                    Ui.Small("Zero means the sheet does not have that one."));

                var read = Ui.Runner("Read the matrix", _warnings, Read);

                // _warnings is this Runner's output panel, so it has to be on this step: when the
                // read produces nothing, the sentence saying why must be beside the numbers that
                // need changing.
                return Ui.Stack(rows, Ui.Actions(read), _warnings);
            }

            private Panel Number(string label, int value, Action<int> set) =>
                Ui.Field(label, Ui.Text(value.ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        && parsed >= 0) set(parsed);
                }, 70));

            private void Read(Job job)
            {
                job.Say("Reading the sheet...");

                var read = SheetMatrix.Read(_sheet.Chosen, _mapping);

                _read = true;
                _matrix = read.Grid;
                _legend = _legend.Covering(read.Grid);
                _plan = null;

                foreach (var warning in read.Warnings) _warnings.Children.Add(Ui.Note(warning));

                _warnings.Children.Insert(0, Ui.Line(read.Explain(), 1, true));

                // The mapping sanity check: a matrix that read cleanly but names sets this model
                // does not have is a mapping pointing at the wrong column, and the moment to say
                // so is now, not when every test is refused.
                Verify();

                if (_matrix.Categories.Count == 0)
                    _warnings.Children.Add(Ui.Problem(
                        "No categories came out of that. The name column is set to "
                        + _mapping.NameColumn.ToString(CultureInfo.InvariantCulture)
                        + " and the list is set to start at row "
                        + _mapping.FirstRow.ToString(CultureInfo.InvariantCulture)
                        + " - check both against the preview below, using the numbers Excel shows "
                        + "down the side and along the top."));

                if (_matrix.Categories.Count > 0 && !_legend.IsEmpty)
                    _warnings.Children.Add(Ui.Done("The legend arrived with the grid: every "
                        + "symbol in it is listed on the next step, defaulting to a hard clash "
                        + "until its row says otherwise."));

                ShowGrid();
                ShowLegend();
                ShowCategories();
                _output.Children.Clear();
            }

            /// <summary>Whether the sheet's set names exist in this model, said as one line.</summary>
            private void Verify()
            {
                if (_matrix.Categories.Count == 0) return;

                var have = new HashSet<string>(
                    _session.Model.Sets().Select(s => s.DisplayName),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var model in _session.Model.Models) have.Add(model.DisplayName);

                var strangers = _matrix.Categories
                    .Where(c => !c.IsUnbacked && c.Scopes.Count == 0 && !have.Contains(c.SetName))
                    .Select(c => c.SetName)
                    .ToList();

                if (strangers.Count == 0)
                {
                    _warnings.Children.Add(Ui.Done("Every backed category names a set or model "
                        + "this document actually has."));
                    return;
                }

                _warnings.Children.Add(Ui.Problem(Ui.Count(strangers.Count, "category") + " name "
                    + "sets this model does not have - usually a mapping pointing at the wrong "
                    + "column, or sets not built yet: "
                    + string.Join(", ", strangers.Take(5))
                    + (strangers.Count > 5 ? ", ..." : ".")));
            }

            // ---------------------------------------------------------------------------------
            // The matrix and its legend
            // ---------------------------------------------------------------------------------

            private void ShowGrid()
            {
                _grid.Children.Clear();

                if (_matrix.Categories.Count == 0)
                {
                    _grid.Children.Add(Ui.Empty("Nothing to draw yet.",
                        "Categories arrive from the first step - by hand, from the model's "
                        + "tests, or from a sheet."));
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
                    label.ToolTip = _matrix.Categories[r].Scopes.Count > 0
                        ? "A whole model"
                        : _matrix.Categories[r].SetName.Length > 0
                            ? "Backed by \"" + _matrix.Categories[r].SetName + "\""
                            : "Backed by nothing, so no test on it can run.";
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
                _grid.Children.Add(Ui.Small("Top half only; the diagonal is a category against "
                                            + "itself. Any text is a symbol - the legend below "
                                            + "says what each one makes."));
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

                _legendRows.Children.Add(Ui.Small("An undescribed symbol reads as a hard clash."));
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

            /// <summary>The grid and its key, as the workbook whoever asked expects back.</summary>
            private void ExportExcel()
            {
                _said.Children.Clear();

                if (_matrix.Categories.Count == 0)
                {
                    Said(Ui.Problem("There is no matrix to write yet."));
                    return;
                }

                var grid = new XlsxSheet("Matrix");

                var header = new List<string?> { string.Empty };
                header.AddRange(_matrix.Categories.Select(c => (string?)c.ToString()));
                grid.Rows.Add(header);

                for (var r = 0; r < _matrix.Categories.Count; r++)
                {
                    var cells = new List<string?> { _matrix.Categories[r].ToString() };

                    // Both halves written, mirrored, because that is how the square is read on
                    // paper - the top-half-only rule is an editing rule, not a printing one.
                    for (var c = 0; c < _matrix.Categories.Count; c++)
                        cells.Add(_matrix.At(r, c) ?? string.Empty);

                    grid.Rows.Add(cells);
                }

                var key = new XlsxSheet("Legend");
                key.Rows.Add(new List<string?> { "Symbol", "Test", "Tolerance (m)", "Enabled" });

                foreach (var symbol in _legend.Symbols)
                    key.Rows.Add(new List<string?>
                    {
                        symbol.Symbol,
                        symbol.Test.ToString(),
                        symbol.Tolerance.ToString("0.###", CultureInfo.InvariantCulture),
                        symbol.Enabled ? "yes" : "no",
                    });

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export the matrix",
                    Filter = "Excel|*.xlsx",
                    FileName = "clash-matrix.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                };

                if (dialog.ShowDialog() != true) return;

                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName,
                                                 XlsxWriter.Write(new[] { grid, key }));
                }
                catch (Exception e)
                {
                    Said(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _session.Record(ActivityKind.Export, "wrote the clash matrix",
                                Ui.Count(_matrix.Categories.Count, "category") + " to " + dialog.FileName);

                Said(Ui.Done("Wrote " + dialog.FileName + " - the grid on one sheet, the legend "
                             + "on the next. It reads straight back in on the first step."));
            }

            private void Said(UIElement note)
            {
                _said.Children.Clear();
                _said.Children.Add(note);
            }

            // ---------------------------------------------------------------------------------
            // The tests
            // ---------------------------------------------------------------------------------

            private UIElement Make()
            {
                var commit = Ui.Commit("Preview", "Create", "test",
                    plan: (job, into) =>
                    {
                        _plan = MatrixPlanner.Plan(_matrix, _legend, Existing(), _pattern.Text);

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

                            var proposed = Ui.Table(rows,
                                ("Test", 300), ("Kind", 140), ("Tolerance", 100));

                            into.Children.Add(Ui.Picking(proposed,
                                new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(proposed, picked), say, "test"))));
                        }

                        return _plan.Tests.Count;
                    },
                    apply: job => Create(job));

                return Ui.Stack(
                    Bench.Group("How they are made",
                        Ui.Field("Name pattern", _pattern, "{a} and {b} are the sides, {n} numbers"),
                        Ui.Field("Put them in", _folder, "a clash folder; blank is the root")),
                    commit,
                    _output);
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
                        + "The matrix and its legend are still yours to export."));
                    return;
                }

                var folder = _folder.Text.Trim();
                var made = 0;
                var refused = new List<string>();

                foreach (var test in plan.Tests)
                {
                    if (!job.Step(made, plan.Tests.Count, "tests created")) break;

                    if (clash.Create(test.Spec(folder.Length == 0 ? null : folder)) != null) made++;
                    else refused.Add(test.Name + ": " + (clash.LastProblem ?? "the host did not say why."));
                }

                // The report: what was made, of what kind, and everything that was not.
                _output.Children.Add(Ui.Line("Created " + Ui.Count(made, "test")
                    + (folder.Length == 0 ? "" : " in \"" + folder + "\"") + ".", 1, true));

                foreach (var band in plan.Tests.Take(made)
                             .GroupBy(t => t.Test)
                             .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
                    _output.Children.Add(Ui.Line(Ui.Count(band.Count(), band.Key.ToString()
                        + " test"), 0.75));

                if (plan.Already > 0)
                    _output.Children.Add(Ui.Line(Ui.Count(plan.Already, "test")
                        + " already in the document, left alone.", 0.75));

                foreach (var why in refused.Take(6)) _output.Children.Add(Ui.Problem(why));

                if (refused.Count > 6)
                    _output.Children.Add(Ui.Note("and " + Ui.Count(refused.Count - 6, "more refusal")
                        + "."));

                if (made > 0)
                    _output.Children.Add(Ui.Actions(
                        Ui.Quiet("Run them", () =>
                        {
                            ClashManageViews.Forget();
                            Ui.Told(Shell.AppWindow.Show("clash", "manage"));
                        }),
                        Ui.Quiet("Manage them", () =>
                        {
                            ClashManageViews.Forget();
                            Ui.Told(Shell.AppWindow.Show("clash", "manage"));
                        })));

                _session.Record(ActivityKind.Regroup,
                    "created " + Ui.Count(made, "clash test") + " from the matrix");
            }
        }
    }
}
