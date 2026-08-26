using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A report on whatever the model actually carries.
    ///
    /// Everything else CamelWorks writes out reports on something CamelWorks made - a clash, a
    /// change, a takeoff. This reports on the model: pick the properties, pick the shape, get a
    /// sheet. It is the tool that a door schedule, an asset register and a handover matrix all turn
    /// out to be, and none of them survives a fixed set of columns.
    ///
    /// A window because it is three lists side by side - what the model carries, what the report is
    /// made of, and what that produces - and because a person building one moves between all three
    /// a dozen times. That is not a pane; it is a workbench.
    ///
    /// Nothing here needs setting up first. Read the model and it proposes a report from what it
    /// found: the properties most elements carry, minus the ones that say the same thing everywhere
    /// and the ones that say something different on every element. Saving that as a template makes
    /// it repeatable next revision - it is never the price of the first one.
    /// </summary>
    public static class ReportBuilderWindow
    {
        /// <summary>Where saved reports live in the project file.</summary>
        public const string Key = DataReport.StoreKey;

        /// <summary>How many elements will be read into one report before it stops and says so.</summary>
        public const int ReadCeiling = 20000;

        /// <summary>How many rows the preview shows.</summary>
        public const int PreviewRows = 25;

        private const string Selection = "What is selected";
        private const string Everything = "Everything loaded";
        private const string Nothing = "(no grouping)";
        private const string ModelOrder = "(model order)";
        private const string Book = "Excel workbook (.xlsx)";
        private const string Plain = "Comma-separated (.csv)";


        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Builder(session).View;

        private sealed class Builder
        {
            private readonly Session _session;
            private readonly ToolMemory _kept;

            private readonly List<ReportTemplate> _saved = new List<ReportTemplate>();
            private readonly List<ReportElement> _scope = new List<ReportElement>();

            private readonly StackPanel _read = Ui.Stack();
            private readonly StackPanel _catalogue = Ui.Stack();
            private readonly StackPanel _columns = Ui.Stack();
            private readonly StackPanel _shape = Ui.Stack();
            private readonly StackPanel _preview = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();

            private readonly TextBox _file;
            private readonly TextBox _name;

            private IReadOnlyList<FieldSummary> _fields = Array.Empty<FieldSummary>();
            private ReportTemplate _template = new ReportTemplate();
            private ListView? _offered;
            private ComboBox? _picker;
            private string _scopeChoice;
            private string _format;
            private int _cut;

            internal Builder(Session session)
            {
                _session = session;
                _kept = ToolMemory.Of(session.Store, "data.report");

                _scopeChoice = _kept.Text("scope", Everything) ?? Everything;
                _format = _kept.Text("format", Book) ?? Book;

                _file = Ui.Text(_kept.Text("file"), "where to write it",
                                text => _kept.Set("file", text), 260);

                _name = Ui.Text(_kept.Text("name", "Model data"), null, 200);

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Four decisions, in the order each one needs the last.
            ///
            /// This was two scrolling columns holding eight headed sections, which put "Write" a
            /// few inches from a scope nothing had read, and - worse - put the catalogue of what
            /// the model carries in one column and the columns you build out of it in the other,
            /// so choosing a column meant looking away from the list you were choosing it from.
            /// They are one step now.
            ///
            /// The dependency is real at every hop: nothing to choose columns from until the model
            /// has been read, nothing to shape until there are columns, nothing to write until
            /// there is a shape.
            ///
            /// Each step's surface is built once and handed back on every visit, so going back to
            /// add a column does not re-read the model.
            /// </summary>
            private UIElement Build()
            {
                var model = Ui.Stack(
                    Ui.Form(Ui.Field("Scope",
                        Ui.Choice(new[] { Selection, Everything }, _scopeChoice, pick =>
                        {
                            _scopeChoice = pick;
                            _kept.Set("scope", pick);
                        }))),
                    Ui.Runner("Read the model", _read, Gather),
                    _read);

                var columns = Ui.Stack(
                    Ui.Eyebrow("What the model carries"),
                    Ui.Small("Every property found, and how far it reaches. Pick the ones the "
                             + "report should have and add them as columns."),
                    _catalogue,
                    Ui.Rule(),
                    Ui.Eyebrow("The columns"),
                    _columns,
                    Ui.Rule(),
                    Ui.Eyebrow("This report"),
                    Saved());

                var shape = Ui.Stack(
                    _shape,
                    Ui.Rule(),
                    Ui.Eyebrow("What that produces"),
                    _preview);

                var write = Ui.Stack(
                    Ui.Form(Ui.Field("Format",
                        Ui.Choice(new[] { Book, Plain }, _format, pick =>
                        {
                            _format = pick;
                            _kept.Set("format", pick);
                            Draw();
                        }))),
                    Ui.PathField("File", _file, PathKind.Save,
                                 "Excel workbook|*.xlsx|Comma-separated|*.csv",
                                 "The sheet carries the element identities, so it can be filled in "
                                 + "and brought back through Data ▸ Bring a sheet back in."),
                    Ui.Commit("See what would be written", "Write", "row", Rehearse, Publish),
                    _output,
                    _note);

                Draw();

                var steps = new[]
                {
                    new Ui.Step("The model", () => model,
                                () => _scope.Count == 0 ? "Read the model first." : null),
                    new Ui.Step("The columns", () => columns,
                                () => _template.Columns.Count == 0
                                      ? "Add at least one column." : null),
                    new Ui.Step("The shape", () => shape),
                    new Ui.Step("Write it", () => write),
                };

                // No finish button of its own: the last step carries Ui.Commit, which is the
                // two-stage "show me, then do it" a write has always used here.
                return Ui.Scroll(Ui.Steps(steps, null, null));
            }

            private UIElement Saved()
            {
                var names = _saved.Select(t => t.Name).ToList();

                if (names.Count == 0) names.Add(_template.Name);

                _picker = Ui.Choice(names, _template.Name, Switch, 200);

                return Ui.Stack(
                    Ui.Small("A saved report runs again next revision on whatever is loaded then. "
                             + "Nothing has to be saved for the report to work now."),
                    Ui.Form(
                        Ui.Field("Saved", _picker),
                        Ui.Field("Name", _name)),
                    Ui.Actions(
                        Ui.Quiet("Save this one", Keep),
                        Ui.Quiet("Start again", Fresh),
                        Ui.Quiet("Forget it", Forget)));
            }

            // -----------------------------------------------------------------------------------
            // Reading the model
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Read the elements in scope once, flat.
            ///
            /// Once, because a report is built and rebuilt a dozen times while a person decides what
            /// is in it, and re-walking a federated model each time is what makes a tool feel like
            /// it is fighting you.
            /// </summary>
            private void Gather(Job job)
            {
                _scope.Clear();
                _cut = 0;

                var traversal = _scopeChoice == Selection
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.WholeDocument;

                var seen = 0;

                foreach (var item in _session.Model.Traverse(traversal))
                {
                    seen++;

                    if (!job.Step(_scope.Count, "elements read")) return;

                    if (_scope.Count >= ReadCeiling) { _cut++; continue; }

                    var element = ReportElement.Of(item);

                    if (element != null) _scope.Add(element);
                }

                _fields = DataReport.Fields(_scope);

                if (_scope.Count == 0)
                {
                    _read.Children.Add(Ui.Empty(
                        _scopeChoice == Selection ? "Nothing is selected." : "Nothing is loaded.",
                        _scopeChoice == Selection
                            ? "Select elements in the model, or switch the scope to everything loaded."
                            : "Append a model first."));

                    Draw();
                    return;
                }

                _read.Children.Add(Ui.Line(
                    Ui.Count(_scope.Count, "element") + ", " + Ui.Count(_fields.Count, "property"),
                    1, true));

                if (_cut > 0)
                    _read.Children.Add(Ui.Note(
                        "That scope holds " + Ui.Count(seen, "element") + ". A report reads the "
                        + "first " + ReadCeiling.ToString("N0", CultureInfo.InvariantCulture)
                        + " so it stays in memory; the other "
                        + _cut.ToString("N0", CultureInfo.InvariantCulture) + " were not read. "
                        + "Select the part you want, or make a set of it, and read that instead."));

                if (_template.Columns.Count == 0)
                {
                    _template = DataReport.Propose(_scope);
                    _name.Text = _template.Name;

                    _read.Children.Add(Ui.Done(
                        "Nothing was set up, so CamelWorks read the model and proposed "
                        + Ui.Count(_template.Columns.Count, "column")
                        + ". Change any of it below."));
                }

                Draw();
            }

            // -----------------------------------------------------------------------------------
            // Drawing
            // -----------------------------------------------------------------------------------

            private void Draw()
            {
                DrawCatalogue();
                DrawColumns();
                DrawShape();
                DrawPreview();
            }

            private void DrawCatalogue()
            {
                _catalogue.Children.Clear();

                if (_fields.Count == 0)
                {
                    _catalogue.Children.Add(Ui.Small("Read the model and every property it carries "
                                                     + "is listed here."));
                    return;
                }

                var rows = _fields.Select(field =>
                {
                    // Describe already says "the same one everywhere" and "one each, so it reads
                    // as an identifier" - the two shapes worth passing over - so the row does not
                    // need a colour to say it a second time.
                    var row = new TableRow(field,
                        field.Name,
                        field.Category.Length == 0 ? "—" : field.Category,
                        field.Describe(_scope.Count));

                    return row;
                }).ToList();

                var table = Ui.Table(rows, ("Property", 150), ("Tab", 110), ("What it looks like", 0)).Multi();

                _offered = table;

                _catalogue.Children.Add(table);
                _catalogue.Children.Add(Ui.Actions(
                    Ui.Button("Add the chosen ones", AddChosen),
                    Ui.Quiet("Propose a report from all of it", Suggest)));
            }

            private void DrawColumns()
            {
                _columns.Children.Clear();

                if (_template.Columns.Count == 0)
                {
                    _columns.Children.Add(Ui.Empty(
                        "This report has no columns yet.",
                        "Read the model and CamelWorks will propose a set, or pick properties from "
                        + "the list on the left.",
                        _scope.Count > 0 ? "Propose a report" : null,
                        _scope.Count > 0 ? (Action)Suggest : null));

                    return;
                }

                for (var i = 0; i < _template.Columns.Count; i++)
                {
                    var at = i;
                    var column = _template.Columns[at];

                    var heading = Ui.Text(column.Heading, text =>
                    {
                        column.Heading = string.IsNullOrWhiteSpace(text) ? column.Heading : text.Trim();
                        DrawShape();
                        DrawPreview();
                    }, 170);

                    var roll = Ui.Choice(Rolls.Select(r => r.Caption).ToList(), Caption(column.Roll),
                                         pick =>
                                         {
                                             column.Roll = Rolled(pick);
                                             DrawPreview();
                                         }, 190);

                    roll.IsEnabled = _template.GroupBy != null;

                    var editable = Ui.Check("May be filled in", column.Editable,
                                            on => column.Editable = on);

                    editable.IsEnabled = column.Field == ReportField.Property;

                    editable.ToolTip = column.Field == ReportField.Property
                        ? "Marked on the sheet as a column a reviewer may change and send back."
                        : "Only a property column can be written back - there is nowhere to put an "
                          + "edited name or an edited tree path.";

                    _columns.Children.Add(Ui.Across(
                        heading,
                        Ui.Line(Reads(column), 0.7),
                        roll,
                        editable,
                        Ui.Icon(Glyphs.Up, "Move it up", () => Move(at, -1)),
                        Ui.Icon(Glyphs.Down, "Move it down", () => Move(at, 1)),
                        Ui.Icon(Glyphs.Close, "Take it out", () => Drop(at))));
                }

                _columns.Children.Add(Ui.Actions(
                    Ui.Quiet("Add the selection tree", () => AddBuilt(ReportField.Path)),
                    Ui.Quiet("Add a tree level", () => AddBuilt(ReportField.PathLevel)),
                    Ui.Quiet("Add the model name", () => AddBuilt(ReportField.Model))));
            }

            private void DrawShape()
            {
                _shape.Children.Clear();

                var headings = _template.Columns.Select(c => c.Heading).ToList();

                var groups = new List<string> { Nothing };
                groups.AddRange(headings);

                var sorts = new List<string> { ModelOrder };
                sorts.AddRange(headings);

                var suggestion = _template.GroupBy == null && _scope.Count > 0
                    ? DataReport.SuggestGroup(_scope, _template)
                    : null;

                _shape.Children.Add(Ui.Form(
                    Ui.Field("Fold rows on",
                        Ui.Choice(groups, _template.GroupBy ?? Nothing, pick =>
                        {
                            _template.GroupBy = pick == Nothing ? null : pick;
                            DrawColumns();
                            DrawPreview();
                        }, 200),
                        "One row per value instead of one row per element. Each other column then "
                        + "says what it does with the elements underneath it."),
                    Ui.Field("Order by",
                        Ui.Choice(sorts, _template.SortBy ?? ModelOrder, pick =>
                        {
                            _template.SortBy = pick == ModelOrder ? null : pick;
                            DrawPreview();
                        }, 200)),
                    Ui.Field("Largest first",
                        Ui.Toggle(string.Empty, _template.Descending, on =>
                        {
                            _template.Descending = on;
                            DrawPreview();
                        })),
                    Ui.Field("Carry the identities",
                        Ui.Toggle(string.Empty, _template.WithIdentity, on =>
                        {
                            _template.WithIdentity = on;
                            DrawPreview();
                        }),
                        "Puts the CamelWorks id on the front so the sheet can be filled in and "
                        + "brought back. A folded report cannot carry them."),
                    Ui.Field("Say when a value is missing",
                        Ui.Toggle(string.Empty, _template.BlanksShown, on =>
                        {
                            _template.BlanksShown = on;
                            DrawPreview();
                        }),
                        "An empty cell and a cell that says \"" + DataReport.Blank + "\" mean "
                        + "different things to whoever reads the sheet.")));

                if (suggestion != null)
                    _shape.Children.Add(Ui.Info("\"" + suggestion + "\" would fold this into a "
                        + "summary worth reading."));
            }

            private void DrawPreview()
            {
                _preview.Children.Clear();

                if (_scope.Count == 0)
                {
                    _preview.Children.Add(Ui.Small("Read the model and the report appears here as "
                                                   + "you build it."));
                    return;
                }

                var table = DataReport.Build(_scope, _template, _session.Profile.Units);

                if (table.Refusal != null)
                {
                    _preview.Children.Add(Ui.Note(table.Refusal));
                    return;
                }

                _preview.Children.Add(Ui.Line(table.Explain(), 1, true));

                foreach (var note in table.Notes) _preview.Children.Add(Ui.Note(note));

                var shown = table.Rows.Take(PreviewRows).ToList();

                var widths = table.Headings
                    .Select((h, i) => (Header: h, Width: i == table.Headings.Count - 1 ? 0.0 : 150.0))
                    .ToArray();

                var sample = Ui.Table(
                    shown.Select(r => new TableRow(r, r.Cells.ToArray())).ToList(), widths);

                _preview.Children.Add(Ui.Picking(sample,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(sample, picked), say, "row"))));

                _preview.Children.Add(Ui.Tally(shown.Count, table.Rows.Count, "row"));
            }

            // -----------------------------------------------------------------------------------
            // Editing
            // -----------------------------------------------------------------------------------

            private void AddChosen()
            {
                var table = _offered;
                if (table == null) return;

                var added = 0;

                foreach (var picked in table.PickedMany())
                {
                    if (!(picked is FieldSummary field)) continue;

                    if (_template.Columns.Any(c => c.Field == ReportField.Property
                                                   && c.Source == field.Field)) continue;

                    var column = ReportColumn.Of(field.Field);
                    column.Editable = true;

                    if (_template.Add(column) == null) break;

                    added++;
                }

                _note.Children.Clear();

                if (added == 0)
                    _note.Children.Add(Ui.Note("Nothing new to add - pick properties in the list on "
                                               + "the left first."));

                Draw();
            }

            private void AddBuilt(ReportField field)
            {
                var level = 1;

                if (field == ReportField.PathLevel)
                    level = _template.Columns.Count(c => c.Field == ReportField.PathLevel) + 1;

                _template.Add(ReportColumn.Built(field, null, level));

                Draw();
            }

            private void Move(int at, int by)
            {
                var to = at + by;

                if (to < 0 || to >= _template.Columns.Count) return;

                var column = _template.Columns[at];

                _template.Columns.RemoveAt(at);
                _template.Columns.Insert(to, column);

                Draw();
            }

            private void Drop(int at)
            {
                if (at < 0 || at >= _template.Columns.Count) return;

                var gone = _template.Columns[at].Heading;

                _template.Columns.RemoveAt(at);

                if (string.Equals(_template.GroupBy, gone, StringComparison.OrdinalIgnoreCase))
                    _template.GroupBy = null;

                if (string.Equals(_template.SortBy, gone, StringComparison.OrdinalIgnoreCase))
                    _template.SortBy = null;

                Draw();
            }

            private void Suggest()
            {
                if (_scope.Count == 0) return;

                var name = _template.Name;

                _template = DataReport.Propose(_scope);
                _template.Name = name;

                Draw();
            }

            private void Fresh()
            {
                _template = new ReportTemplate(_name.Text);

                _note.Children.Clear();

                Draw();
            }

            // -----------------------------------------------------------------------------------
            // Saving
            // -----------------------------------------------------------------------------------

            private void Load()
            {
                _saved.Clear();
                _saved.AddRange(DataReport.Saved(_session.Store.Section(ProjectStore.DataSection)[Key]));

                var last = _kept.Text("template");

                var wanted = last == null
                    ? _saved.FirstOrDefault()
                    : _saved.FirstOrDefault(t => string.Equals(t.Name, last, StringComparison.OrdinalIgnoreCase));

                if (wanted != null) _template = wanted.Copy();
            }

            private void Switch(string name)
            {
                var wanted = _saved.FirstOrDefault(
                    t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

                if (wanted == null) return;

                _template = wanted.Copy();
                _name.Text = _template.Name;

                _kept.Set("template", _template.Name);
                _kept.Keep();

                _note.Children.Clear();

                Draw();
            }

            private void Keep()
            {
                _note.Children.Clear();

                if (_template.Columns.Count == 0)
                {
                    _note.Children.Add(Ui.Problem("There is nothing to save yet - a report with no "
                                                  + "columns would come back empty."));
                    return;
                }

                var name = _name.Text.Trim();

                _template.Name = name.Length == 0 ? "Model data" : name;

                var written = DataReport.Keep(_saved, _template);

                _session.Store.Section(ProjectStore.DataSection).Set(Key, written);

                _saved.Clear();
                _saved.AddRange(DataReport.Saved(written));

                _kept.Set("template", _template.Name);
                _kept.Keep();

                _session.Record(ActivityKind.Setup, "Saved the report \"" + _template.Name + "\"",
                                Ui.Count(_template.Columns.Count, "column"));

                _note.Children.Add(Ui.Done("Saved \"" + _template.Name + "\". It will run again on "
                                           + "whatever is loaded next time."));

                Repick();
            }

            private void Forget()
            {
                _note.Children.Clear();

                var name = _template.Name;

                var left = _saved
                    .Where(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (left.Count == _saved.Count)
                {
                    _note.Children.Add(Ui.Note("\"" + name + "\" was never saved, so there is "
                                               + "nothing to forget."));
                    return;
                }

                _session.Store.Section(ProjectStore.DataSection)
                        .Set(Key, JsonValue.Array(left.Select(t => t.ToJson())));

                _saved.Clear();
                _saved.AddRange(left);

                _note.Children.Add(Ui.Done("Forgot \"" + name + "\". It is still open here until "
                                           + "you start again."));

                Repick();
            }

            private void Repick()
            {
                var picker = _picker;
                if (picker == null) return;

                picker.Items.Clear();

                foreach (var template in _saved) picker.Items.Add(template.Name);

                if (_saved.Count == 0) picker.Items.Add(_template.Name);

                picker.SelectedItem = _template.Name;
            }

            // -----------------------------------------------------------------------------------
            // Writing
            // -----------------------------------------------------------------------------------

            private int Rehearse(Job job, Panel into)
            {
                var table = DataReport.Build(_scope, _template, _session.Profile.Units);

                if (table.Refusal != null)
                {
                    into.Children.Add(Ui.Problem(table.Refusal));
                    return 0;
                }

                into.Children.Add(Ui.Split("Rows", table.Rows.Count.ToString("N0", CultureInfo.InvariantCulture)));
                into.Children.Add(Ui.Split("Columns", table.Headings.Count.ToString("N0", CultureInfo.InvariantCulture)));
                into.Children.Add(Ui.Split("Elements behind them",
                    table.Elements.ToString("N0", CultureInfo.InvariantCulture)));

                var writable = _template.Columns.Where(c => c.Editable).Select(c => c.Heading).ToList();

                if (table.WithIdentity && writable.Count > 0)
                    into.Children.Add(Ui.Info("It can be sent back: "
                        + string.Join(", ", writable) + " "
                        + (writable.Count == 1 ? "is a column" : "are columns")
                        + " a reviewer may fill in, and the identities on the front say which "
                        + "element each row belongs to."));
                else if (table.WithIdentity)
                    into.Children.Add(Ui.Note("Nothing in it is marked as fillable, so it will read "
                        + "as a record rather than a form. Tick \"May be filled in\" on the columns "
                        + "a reviewer should change."));

                foreach (var note in table.Notes) into.Children.Add(Ui.Note(note));

                if (_format == Plain && table.WithIdentity)
                    into.Children.Add(Ui.Note("Comma-separated files carry the identities as text. "
                        + "Excel and Access both open them; a spreadsheet that reformats the column "
                        + "will break the way back, so keep the workbook if it is going to a "
                        + "reviewer."));

                return table.Rows.Count;
            }

            private void Publish(Job job)
            {
                _output.Children.Clear();

                var table = DataReport.Build(_scope, _template, _session.Profile.Units);

                if (table.Refusal != null)
                {
                    _output.Children.Add(Ui.Problem(table.Refusal));
                    return;
                }

                var file = _file.Text.Trim();

                if (file.Length == 0)
                {
                    _output.Children.Add(Ui.Problem("Say where to write it first."));
                    return;
                }

                file = Extended(file);

                try
                {
                    if (_format == Plain)
                        File.WriteAllText(file, DataReport.Csv(table));
                    else
                        File.WriteAllBytes(file, XlsxWriter.Write(DataReport.Workbook(table, _template)));
                }
                catch (Exception e)
                {
                    _output.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _kept.Set("file", file);
                _kept.Keep();

                _session.Record(ActivityKind.Export, "Wrote the report \"" + _template.Name + "\"",
                                Ui.Count(table.Rows.Count, "row") + " to " + file);

                _output.Children.Add(Ui.Done("Wrote " + file + " - "
                    + Ui.Count(table.Rows.Count, "row") + "."));

                if (table.WithIdentity)
                    _output.Children.Add(Ui.Small("Fill it in and bring it back through "
                        + "Data ▸ Bring a sheet back in. The identity column is how each row "
                        + "finds its element again, so it has to survive the round trip."));
            }

            private string Extended(string file)
            {
                var wanted = _format == Plain ? ".csv" : ".xlsx";

                return file.EndsWith(wanted, StringComparison.OrdinalIgnoreCase) ? file : file + wanted;
            }

            // -----------------------------------------------------------------------------------
            // Words
            // -----------------------------------------------------------------------------------

            private static readonly (ReportRoll Roll, string Caption)[] Rolls =
            {
                (ReportRoll.Same, "the value they share"),
                (ReportRoll.Count, "how many elements"),
                (ReportRoll.Distinct, "how many values"),
                (ReportRoll.List, "list the values"),
                (ReportRoll.Sum, "add them up"),
                (ReportRoll.Least, "the smallest"),
                (ReportRoll.Most, "the largest"),
                (ReportRoll.Mean, "the average"),
            };

            private static string Caption(ReportRoll roll) =>
                Rolls.First(r => r.Roll == roll).Caption;

            private static ReportRoll Rolled(string caption) =>
                Rolls.FirstOrDefault(r => string.Equals(r.Caption, caption, StringComparison.Ordinal)).Roll;

            private static string Reads(ReportColumn column) => column.Field switch
            {
                ReportField.Name => "the name",
                ReportField.Category => "the category",
                ReportField.Type => "the type",
                ReportField.Model => "the model",
                ReportField.Path => "the whole tree path",
                ReportField.PathLevel => "tree level "
                                         + column.Level.ToString(CultureInfo.InvariantCulture),
                ReportField.Identity => "the identity",
                _ => column.Source.Category.Length == 0
                        ? column.Source.Name
                        : column.Source.Category + " / " + column.Source.Name,
            };
        }
    }
}
