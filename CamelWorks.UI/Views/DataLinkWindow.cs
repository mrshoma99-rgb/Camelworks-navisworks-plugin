using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A spreadsheet and the model, joined on a column, in both directions.
    ///
    /// The round trip already existed: export a sheet, fill it in, bring it back. That works
    /// because CamelWorks put its own identities in the sheet. It does nothing at all for the file
    /// somebody else produced - an asset register keyed on tag, a procurement extract keyed on
    /// order number - which is every file that matters.
    ///
    /// So the join is stated rather than assumed. Which column identifies the element, what on the
    /// element it is matched against, and what each other column does: write it on, fill a gap with
    /// it, or go the other way and put the model's value into the sheet.
    ///
    /// A window because it is a mapping table with a live verdict over it, and because the same
    /// mapping gets adjusted five times before it is right.
    /// </summary>
    public static class DataLinkWindow
    {
        /// <summary>Where saved links live in the project file.</summary>
        public const string Key = DataLink.StoreKey;

        /// <summary>How many elements will be read into one link before it stops and says so.</summary>
        public const int ReadCeiling = 20000;

        private const string Selection = "What is selected";
        private const string Everything = "Everything loaded";

        private const string ById = "the identity CamelWorks wrote in";
        private const string ByProperty = "a property on the element";
        private const string ByName = "the name in the selection tree";

        private const string DoWrite = "write it on";
        private const string DoFill = "only where the model has none";
        private const string DoRead = "take the model's value";
        private const string DoNothing = "leave it";


        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Linker(session).View;

        private sealed class Linker
        {
            private readonly Session _session;
            private readonly ToolMemory _kept;

            private readonly List<DataLinkRecipe> _saved = new List<DataLinkRecipe>();
            private readonly List<ReportElement> _scope = new List<ReportElement>();
            private readonly List<ElementKey> _keys = new List<ElementKey>();

            private readonly StackPanel _read = Ui.Stack();
            private readonly StackPanel _model = Ui.Stack();
            private readonly StackPanel _join = Ui.Stack();
            private readonly StackPanel _columns = Ui.Stack();
            private readonly StackPanel _verdict = Ui.Stack();
            private readonly StackPanel _written = Ui.Stack();
            private readonly StackPanel _sent = Ui.Stack();
            private readonly StackPanel _note = Ui.Stack();

            private readonly TextBox _file;
            private readonly TextBox _out;
            private readonly TextBox _name;

            private WorkbookTables? _book;
            private SheetTable? _sheet;
            private DataLinkRecipe _link = new DataLinkRecipe();
            private ComboBox? _picker;
            private ComboBox? _sheets;
            private string _scopeChoice;
            private ExistingValues _existing = ExistingValues.Keep;
            private int _cut;

            internal Linker(Session session)
            {
                _session = session;
                _kept = ToolMemory.Of(session.Store, "data.link");

                _scopeChoice = _kept.Text("scope", Everything) ?? Everything;

                _file = Ui.Text(_kept.Text("file"), "the spreadsheet to link",
                                text => _kept.Set("file", text), 260);

                _out = Ui.Text(_kept.Text("out"), "where to write the returned sheet",
                               text => _kept.Set("out", text), 260);

                _name = Ui.Text(_kept.Text("name", "Data link"), null, 200);

                Load();

                View = Build();
            }

            internal UIElement View { get; }

            // -----------------------------------------------------------------------------------
            // Layout
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// Five decisions, in the order each one needs the last.
            ///
            /// This was two scrolling columns holding seven headed sections, all on screen at
            /// once - so "Write" sat a few inches from the box you were still typing a file path
            /// into, and the column mapping was visible before there was a file to map. The
            /// dependency here is real: there is nothing to join on until the spreadsheet has
            /// been read, and nothing to preview until the columns are mapped.
            ///
            /// Each step's surface is built once and handed back on every visit, so going back to
            /// step two does not throw away what step two already read.
            /// </summary>
            private UIElement Build()
            {
                _sheets = Ui.Choice(Array.Empty<string>(), null, Pick, 200);
                _sheets.IsEnabled = false;

                var file = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.PathField("Spreadsheet", _file, PathKind.Open,
                                     "Spreadsheet|*.xlsx;*.xlsm|All files|*.*"),
                        Ui.Field("Worksheet", _sheets),
                        Ui.Runner("Read the file", _read, Open)),
                    _read,
                    Bench.Group("This link", Saved()));

                var model = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Field("Scope",
                            Ui.Choice(new[] { Selection, Everything }, _scopeChoice, pick =>
                            {
                                _scopeChoice = pick;
                                _kept.Set("scope", pick);
                            })),
                        Ui.Runner("Read the model", _model, Gather)),
                    _model);

                var join = Ui.Stack(_join);

                var columns = Ui.Stack(_columns);

                var write = Ui.Stack(
                    Ui.Eyebrow("What it would do"),
                    _verdict,
                    Bench.Group("On to the model",
                        Ui.Field("If a value is already there",
                            Ui.Choice(new[] { "leave it", "replace it" }, "leave it", pick =>
                            {
                                _existing = pick == "replace it"
                                    ? ExistingValues.Overwrite
                                    : ExistingValues.Keep;
                            }))),
                    Ui.Commit("See what would change", "Write", "element", Rehearse, Publish),
                    _written,
                    Bench.Group("Back to the sheet",
                        Ui.PathField("Write it to", _out, PathKind.Save, "Excel workbook|*.xlsx",
                                     "The whole sheet goes out again, every row.")),
                    Ui.Commit("See what would be sent back", "Send back", "cell", Returning, Send),
                    _sent,
                    _note);

                var steps = new[]
                {
                    new Ui.Step("The file", () => file,
                                () => _sheet == null ? "Read a spreadsheet first." : null),
                    new Ui.Step("The model", () => model,
                                () => _scope.Count == 0 ? "Read the model first." : null),
                    new Ui.Step("Matching", () => join,
                                () => string.IsNullOrWhiteSpace(_link.KeyHeading)
                                      ? "Say which column finds the element." : null),
                    new Ui.Step("Columns", () => columns),
                    new Ui.Step("Write", () => write),
                };

                // No finish button of its own: the last step carries Ui.Commit, which is the
                // two-stage "show me, then do it" this write has always used. A third button
                // above it saying the same thing and doing nothing would be worse than none.
                return Ui.Steps(steps, null, null);
            }


            private UIElement Saved()
            {
                var names = _saved.Select(l => l.Name).ToList();

                if (names.Count == 0) names.Add(_link.Name);

                _picker = Ui.Choice(names, _link.Name, Switch, 200);

                return Ui.Stack(
                    Ui.Field("Saved", _picker,
                             "Runs again next revision against a newer drop."),
                    Ui.Field("Name", _name),
                    Ui.Actions(
                        Ui.Quiet("Save this one", Keep),
                        Ui.Quiet("Forget it", Forget)));
            }

            // -----------------------------------------------------------------------------------
            // Reading
            // -----------------------------------------------------------------------------------

            private void Open(Job job)
            {
                var path = _file.Text.Trim();

                if (path.Length == 0)
                {
                    _read.Children.Add(Ui.Problem("Say which file first."));
                    return;
                }

                try
                {
                    _book = XlsxReader.Read(File.ReadAllBytes(path));
                }
                catch (Exception e)
                {
                    _read.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                var book = _book;

                if (book.Problem != null)
                {
                    _read.Children.Add(Ui.Problem(book.Problem));
                    return;
                }

                _link.File = path;

                var picker = _sheets;

                if (picker != null)
                {
                    picker.Items.Clear();

                    foreach (var sheet in book.Sheets) picker.Items.Add(sheet.Name);

                    picker.IsEnabled = book.Sheets.Count > 1;
                }

                var wanted = book.Sheets.FirstOrDefault(
                    s => string.Equals(s.Name, _link.Sheet, StringComparison.OrdinalIgnoreCase))
                    ?? book.Sheets.FirstOrDefault();

                if (wanted == null)
                {
                    _read.Children.Add(Ui.Problem("There are no readable worksheets in that file."));
                    return;
                }

                Pick(wanted.Name);

                _read.Children.Add(Ui.Line(
                    Ui.Count(book.Sheets.Count, "worksheet") + ", "
                    + Ui.Count(Math.Max(0, wanted.Rows.Count - 1), "row") + " under "
                    + Ui.Count(DataLink.Headings(wanted).Count, "heading"), 1, true));
            }

            private void Pick(string name)
            {
                var book = _book;
                if (book == null) return;

                _sheet = book.Sheets.FirstOrDefault(
                    s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

                _link.Sheet = _sheet?.Name;

                var picker = _sheets;
                if (picker != null) picker.SelectedItem = _sheet?.Name;

                Suggest();
            }

            private void Gather(Job job)
            {
                _scope.Clear();
                _keys.Clear();
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

                    if (element == null) continue;

                    _scope.Add(element);
                    _keys.Add(element.Key);
                }

                if (_scope.Count == 0)
                {
                    _model.Children.Add(Ui.Empty(
                        _scopeChoice == Selection ? "Nothing is selected." : "Nothing is loaded.",
                        _scopeChoice == Selection
                            ? "Select elements in the model, or switch the scope to everything loaded."
                            : "Append a model first."));

                    Draw();
                    return;
                }

                _model.Children.Add(Ui.Line(Ui.Count(_scope.Count, "element") + " in scope", 1, true));

                if (_cut > 0)
                    _model.Children.Add(Ui.Note(
                        "That scope holds " + Ui.Count(seen, "element") + ". A link reads the first "
                        + ReadCeiling.ToString("N0", CultureInfo.InvariantCulture)
                        + "; the other " + _cut.ToString("N0", CultureInfo.InvariantCulture)
                        + " were not read. Select the part the sheet is about and read that instead."));

                Suggest();
            }

            /// <summary>
            /// Work the join out from the file and the model, so nothing has to be set up first.
            ///
            /// Only when there is nothing to keep. A person who has adjusted the mapping and then
            /// switches worksheets should not have their work replaced by a guess.
            /// </summary>
            private void Suggest()
            {
                if (_sheet == null || _scope.Count == 0) { Draw(); return; }

                if (_link.Columns.Count == 0)
                {
                    var file = _link.File;
                    var sheet = _link.Sheet;

                    _link = DataLink.Propose(_sheet, _scope);
                    _link.File = file;
                    _link.Sheet = sheet;

                    _name.Text = _link.Name;

                    if (_link.Columns.Count > 0)
                        _read.Children.Add(Ui.Done(
                            "Joined on \"" + _link.KeyHeading + "\" and mapped "
                            + Ui.Count(_link.Columns.Count, "column") + ". Change any of it on the "
                            + "right."));
                }

                Draw();
            }

            // -----------------------------------------------------------------------------------
            // Drawing
            // -----------------------------------------------------------------------------------

            private void Draw()
            {
                DrawJoin();
                DrawColumns();
                DrawVerdict();
            }

            private void DrawJoin()
            {
                _join.Children.Clear();

                var headings = DataLink.Headings(_sheet);

                if (headings.Count == 0)
                {
                    _join.Children.Add(Ui.Small("Read a file and its columns are listed here."));
                    return;
                }

                var how = _link.Match switch
                {
                    LinkMatch.Property => ByProperty,
                    LinkMatch.Name => ByName,
                    _ => ById,
                };

                var fields = DataReport.Fields(_scope)
                    .Select(f => f.Category.Length == 0 ? f.Name : f.Category + " / " + f.Name)
                    .ToList();

                var chosen = _link.KeyProperty.Name.Length == 0
                    ? fields.FirstOrDefault()
                    : (_link.KeyProperty.Category.Length == 0
                        ? _link.KeyProperty.Name
                        : _link.KeyProperty.Category + " / " + _link.KeyProperty.Name);

                var property = Ui.Choice(fields, chosen, pick =>
                {
                    _link.KeyProperty = Split(pick);
                    DrawVerdict();
                }, 240);

                property.IsEnabled = _link.Match == LinkMatch.Property && fields.Count > 0;

                _join.Children.Add(Ui.Stack(
                    Ui.Field("The column that identifies it",
                        Ui.Choice(headings, headings.Contains(_link.KeyHeading, StringComparer.OrdinalIgnoreCase)
                                                ? _link.KeyHeading
                                                : headings.FirstOrDefault(),
                                  pick => { _link.KeyHeading = pick; DrawColumns(); DrawVerdict(); }, 240),
                        "The column whose values turn up in the model."),
                    Ui.Field("Matched against",
                        Ui.Choice(new[] { ById, ByProperty, ByName }, how, pick =>
                        {
                            _link.Match = pick == ByProperty ? LinkMatch.Property
                                        : pick == ByName ? LinkMatch.Name
                                        : LinkMatch.Identity;
                            DrawJoin();
                            DrawVerdict();
                        }, 240)),
                    Ui.Field("Which property", property)));

                _join.Children.Add(Ui.Actions(
                    Ui.Quiet("Work it out", Rework)));
            }

            private void DrawColumns()
            {
                _columns.Children.Clear();

                var headings = DataLink.Headings(_sheet);

                if (headings.Count == 0)
                {
                    _columns.Children.Add(Ui.Small("Read a file and its columns are listed here."));
                    return;
                }

                var mapped = 0;

                foreach (var heading in headings)
                {
                    if (string.Equals(heading, _link.KeyHeading, StringComparison.OrdinalIgnoreCase))
                    {
                        _columns.Children.Add(Ui.Across(
                            Ui.Line(heading, 1, true),
                            Ui.Pill("the key", Tone.Good)));

                        continue;
                    }

                    var column = _link.Columns.FirstOrDefault(
                        c => string.Equals(c.Heading, heading, StringComparison.OrdinalIgnoreCase));

                    if (column == null)
                    {
                        column = new LinkColumn(heading, null, null, LinkAction.Ignore);
                        _link.Add(column);
                    }

                    var here = column;

                    if (here.Action != LinkAction.Ignore) mapped++;

                    // Both fed from the model: the tab a column reads its existing values from,
                    // and the property a column lands on, are names the federation already
                    // carries, and retyping either from memory is how a mapping quietly reads
                    // the wrong tab.
                    var tab = Pickers.Tab(here.Category, text =>
                    {
                        here.Category = string.IsNullOrWhiteSpace(text) ? DataLink.DefaultTab : text.Trim();
                        DrawVerdict();
                    }, 130);

                    var name = Pickers.Property(here.Property, text =>
                    {
                        here.Property = string.IsNullOrWhiteSpace(text) ? here.Heading : text.Trim();
                        DrawVerdict();
                    }, 150);

                    _columns.Children.Add(Ui.Across(
                        Ui.Line(heading, 1, true),
                        Ui.Choice(new[] { DoWrite, DoFill, DoRead, DoNothing }, Word(here.Action),
                                  pick => { here.Action = Doing(pick); DrawVerdict(); }, 200),
                        tab,
                        name));
                }

                _columns.Children.Add(Ui.Line(
                    Ui.Count(mapped, "column") + " of " + Ui.Count(headings.Count - 1, "other column")
                    + " mapped", 0.7));

                _columns.Children.Add(Ui.Small("Writes land on the \"" + NavWriteTransaction.TabName
                                               + "\" tab; the tab named here is where existing "
                                               + "values are read."));
            }

            private void DrawVerdict()
            {
                _verdict.Children.Clear();

                if (_sheet == null || _scope.Count == 0)
                {
                    _verdict.Children.Add(Ui.Small("Read the file and the model, and what the link "
                                                   + "would do appears here."));
                    return;
                }

                var plan = DataLink.Plan(_sheet, _scope, _link);

                if (plan.Refusal != null)
                {
                    _verdict.Children.Add(Ui.Note(plan.Refusal));
                    return;
                }

                _verdict.Children.Add(Ui.Line(plan.Explain(), 1, true));

                _verdict.Children.Add(Ui.Across(
                    Ui.Figure(plan.Matched.ToString("N0", CultureInfo.InvariantCulture), "rows matched",
                              plan.Matched > 0 ? Tone.Good : Tone.Bad),
                    Ui.Figure(plan.Elements.ToString("N0", CultureInfo.InvariantCulture), "elements"),
                    Ui.Figure(plan.Orphans.Count.ToString("N0", CultureInfo.InvariantCulture),
                              "rows with no element", plan.Orphans.Count > 0 ? Tone.Warn : Tone.Plain),
                    Ui.Figure(plan.Untouched.Count.ToString("N0", CultureInfo.InvariantCulture),
                              "elements not in the sheet")));

                foreach (var note in plan.Notes) _verdict.Children.Add(Ui.Note(note));

                if (plan.Orphans.Count > 0)
                {
                    var orphans = Ui.Table(plan.Orphans.Take(50)
                                               .Select(r => new TableRow(r,
                                                   r.Number.ToString(CultureInfo.InvariantCulture), r.Key))
                                               .ToList(),
                                           ("Row", 70), ("Key", 0));

                    // Row numbers and keys, which is what somebody needs in front of them to go and
                    // fix the sheet - and the sheet is usually not open on this machine.
                    _verdict.Children.Add(Ui.Eyebrow("The rows that matched nothing"));
                    _verdict.Children.Add(Ui.Picking(orphans,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(orphans, picked), say, "row"))));
                }
            }

            // -----------------------------------------------------------------------------------
            // On to the model
            // -----------------------------------------------------------------------------------

            private int Rehearse(Job job, Panel into)
            {
                var plan = DataLink.Plan(_sheet, _scope, _link);

                if (plan.Refusal != null)
                {
                    into.Children.Add(Ui.Problem(plan.Refusal));
                    return 0;
                }

                var total = 0;

                foreach (var write in plan.Writes)
                {
                    if (!job.Step(total, "properties worked out")) return 0;

                    var diff = Diff(write);

                    into.Children.Add(Ui.Diff(diff));

                    total += diff.Writes(_existing);
                }

                if (total == 0)
                    into.Children.Add(Ui.Note("Nothing would change. Either the model already agrees "
                        + "with the sheet, or every value is behind \"leave it\"."));

                return total;
            }

            private void Publish(Job job)
            {
                _written.Children.Clear();

                var plan = DataLink.Plan(_sheet, _scope, _link);

                if (plan.Refusal != null)
                {
                    _written.Children.Add(Ui.Problem(plan.Refusal));
                    return;
                }

                var written = 0;

                using (var transaction = new NavWriteTransaction(_session.Model,
                           "CamelWorks: " + _link.Name + " from a spreadsheet"))
                {
                    var seen = 0;

                    foreach (var write in plan.Writes)
                    {
                        // Every, not Changes: Changes is the handful of rows the preview shows.
                        foreach (var change in Diff(write).Every)
                        {
                            if (!job.Step(seen++, "values queued")) return;

                            if (!PropertyDiff.ShouldWrite(change.Kind, _existing)) continue;

                            transaction.SetProperty(change.Key, NavWriteTransaction.TabName,
                                                    write.Property, change.After);
                        }
                    }

                    job.Say("Writing...");
                    written = transaction.Commit();
                    Ui.Refused(_written, transaction);
                }

                _session.Record(ActivityKind.Write,
                    "linked " + _link.Name + " onto " + Ui.Count(written, "element"),
                    Ui.Count(plan.Writes.Count, "property") + " from " + (_link.File ?? "a spreadsheet"));

                _written.Children.Add(Ui.Done("Wrote " + Ui.Count(written, "element") + "."));

                // The warning that used to sit above the button, printed after the run instead:
                // Navisworks keeps property writes off its undo stack, and the record of what
                // changed lives in the project file.
                _written.Children.Add(Ui.Small("Ctrl+Z will not undo this write; what changed is "
                                               + "recorded in the project file."));

                if (plan.Orphans.Count > 0)
                    _written.Children.Add(Ui.Note(Ui.Count(plan.Orphans.Count, "row")
                        + " matched nothing and were left in the sheet."));
            }

            /// <summary>
            /// One property's before and after.
            ///
            /// An element the sheet said nothing about has its own value as its "after", so it
            /// counts as unchanged and is never written to - which is what stops a link that covers
            /// a fifth of the model from emptying the other four fifths.
            /// </summary>
            /// <param name="write">What the plan said about that property.</param>
            private WriteDiff Diff(LinkWrite write) =>
                PropertyDiff.Of(NavWriteTransaction.TabName, write.Property,
                    // Resolved fresh per property rather than listed once and shared. Resolve is
                    // deliberately streamed - listing it holds every element in the scope live at
                    // the same time, which is the thing its own doc comment tells callers not to do.
                    PropertyDiff.Resolve(_session.Model, _keys),
                    item => write.Mentions(item.Key)
                        ? write.For(item.Key)
                        : item.Property(NavWriteTransaction.TabName, write.Property));

            // -----------------------------------------------------------------------------------
            // Back to the sheet
            // -----------------------------------------------------------------------------------

            private int Returning(Job job, Panel into)
            {
                var returned = DataLink.Back(_sheet, _scope, _link);

                if (returned.Refusal != null)
                {
                    into.Children.Add(Ui.Problem(returned.Refusal));
                    return 0;
                }

                into.Children.Add(Ui.Line(returned.Explain(), 1, true));

                into.Children.Add(Ui.Across(
                    Ui.Figure(returned.Filled.ToString("N0", CultureInfo.InvariantCulture),
                              "empty cells filled", returned.Filled > 0 ? Tone.Good : Tone.Plain),
                    Ui.Figure(returned.Changed.ToString("N0", CultureInfo.InvariantCulture),
                              "cells replaced", returned.Changed > 0 ? Tone.Warn : Tone.Plain),
                    Ui.Figure(returned.Same.ToString("N0", CultureInfo.InvariantCulture), "already agreed"),
                    Ui.Figure(returned.Missing.ToString("N0", CultureInfo.InvariantCulture),
                              "the model has nothing for")));

                if (returned.Columns.Count > 0)
                    into.Children.Add(Ui.Small("Sending back: " + string.Join(", ", returned.Columns) + "."));

                return returned.Filled + returned.Changed;
            }

            private void Send(Job job)
            {
                _sent.Children.Clear();

                var returned = DataLink.Back(_sheet, _scope, _link);

                if (returned.Refusal != null)
                {
                    _sent.Children.Add(Ui.Problem(returned.Refusal));
                    return;
                }

                var file = _out.Text.Trim();

                if (file.Length == 0)
                {
                    _sent.Children.Add(Ui.Problem("Say where to write it first. It is written beside "
                        + "the original rather than over it, so the file that came in is still there "
                        + "if the join was wrong."));
                    return;
                }

                if (!file.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) file += ".xlsx";

                try
                {
                    File.WriteAllBytes(file, XlsxWriter.Write(new[] { returned.Sheet }));
                }
                catch (Exception e)
                {
                    _sent.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _kept.Set("out", file);
                _kept.Keep();

                _session.Record(ActivityKind.Export, "Sent " + _link.Name + " back to a spreadsheet",
                    (returned.Filled + returned.Changed).ToString("N0", CultureInfo.InvariantCulture)
                    + " cells to " + file);

                _sent.Children.Add(Ui.Done("Wrote " + file + " - " + returned.Explain()));
            }

            // -----------------------------------------------------------------------------------
            // Saving
            // -----------------------------------------------------------------------------------

            private void Load()
            {
                _saved.Clear();
                _saved.AddRange(DataLink.Saved(_session.Store.Section(ProjectStore.DataSection)[Key]));

                var last = _kept.Text("link");

                var wanted = last == null
                    ? _saved.FirstOrDefault()
                    : _saved.FirstOrDefault(l => string.Equals(l.Name, last, StringComparison.OrdinalIgnoreCase));

                if (wanted == null) return;

                _link = wanted.Copy();

                if (_link.File != null) _file.Text = _link.File;
            }

            private void Switch(string name)
            {
                var wanted = _saved.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                if (wanted == null) return;

                _link = wanted.Copy();
                _name.Text = _link.Name;

                if (_link.File != null) _file.Text = _link.File;

                _kept.Set("link", _link.Name);
                _kept.Keep();

                _note.Children.Clear();

                Draw();
            }

            private void Rework()
            {
                if (_sheet == null || _scope.Count == 0)
                {
                    _note.Children.Add(Ui.Note("Read the file and the model first - the join is "
                        + "worked out by seeing which column's values turn up in the model."));
                    return;
                }

                _link.Columns.Clear();

                Suggest();
            }

            private void Keep()
            {
                _note.Children.Clear();

                if (_link.Columns.Count(c => c.Action != LinkAction.Ignore) == 0)
                {
                    _note.Children.Add(Ui.Problem("Nothing is mapped yet, so there would be nothing "
                                                  + "to run next time."));
                    return;
                }

                var name = _name.Text.Trim();

                _link.Name = name.Length == 0 ? "Data link" : name;

                var written = DataLink.Keep(_saved, _link);

                _session.Store.Section(ProjectStore.DataSection).Set(Key, written);

                _saved.Clear();
                _saved.AddRange(DataLink.Saved(written));

                _kept.Set("link", _link.Name);
                _kept.Keep();

                _session.Record(ActivityKind.Setup, "Saved the link \"" + _link.Name + "\"",
                                _link.File ?? "no file");

                _note.Children.Add(Ui.Done("Saved \"" + _link.Name + "\". Point it at a newer drop "
                                           + "of the same file and it runs again."));

                Repick();
            }

            private void Forget()
            {
                _note.Children.Clear();

                var name = _link.Name;

                var left = _saved
                    .Where(l => !string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (left.Count == _saved.Count)
                {
                    _note.Children.Add(Ui.Note("\"" + name + "\" was never saved, so there is "
                                               + "nothing to forget."));
                    return;
                }

                _session.Store.Section(ProjectStore.DataSection)
                        .Set(Key, JsonValue.Array(left.Select(l => l.ToJson())));

                _saved.Clear();
                _saved.AddRange(left);

                _note.Children.Add(Ui.Done("Forgot \"" + name + "\". It is still open here."));

                Repick();
            }

            private void Repick()
            {
                var picker = _picker;
                if (picker == null) return;

                picker.Items.Clear();

                foreach (var link in _saved) picker.Items.Add(link.Name);

                if (_saved.Count == 0) picker.Items.Add(_link.Name);

                picker.SelectedItem = _link.Name;
            }

            // -----------------------------------------------------------------------------------
            // Words
            // -----------------------------------------------------------------------------------

            private static string Word(LinkAction action) => action switch
            {
                LinkAction.Write => DoWrite,
                LinkAction.Fill => DoFill,
                LinkAction.Read => DoRead,
                _ => DoNothing,
            };

            private static LinkAction Doing(string word) => word switch
            {
                DoWrite => LinkAction.Write,
                DoFill => LinkAction.Fill,
                DoRead => LinkAction.Read,
                _ => LinkAction.Ignore,
            };

            private static PropertyRef Split(string? text)
            {
                var whole = (text ?? string.Empty).Trim();
                var slash = whole.IndexOf('/');

                return slash < 0
                    ? new PropertyRef(string.Empty, whole)
                    : new PropertyRef(whole.Substring(0, slash).Trim(), whole.Substring(slash + 1).Trim());
            }
        }
    }
}
