using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The Data workspace: read what the models carry, write what they should, and get numbers out
    /// of both.
    /// </summary>
    public static class DataViews
    {
        private const string WholeModel = "Everything loaded";
        private const string Selection = "Current selection";

        // -----------------------------------------------------------------------------------
        // Data manager
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Write one property across everything in scope.
        ///
        /// The same four writers the Data Manager offers - a value you type, a formula, a code
        /// table, a spreadsheet - plus find and replace, without the property browser above them.
        /// They were only reachable by opening the Data Manager, choosing a scope, pressing Read
        /// properties and scrolling past a table of everything the model already carries; somebody
        /// who knows exactly which property they are setting had to be shown all the others first.
        ///
        /// Scope still leads and reading still gates the writers, because a write needs the
        /// elements it is writing to and the values it would be overwriting. What goes is the
        /// browse table, which is the Data Manager's job and not this one's.
        /// </summary>
        internal static UIElement WriteProperties()
        {
            var scope = Selection;
            string? readScope = null;
            var results = Ui.Stack();
            var editor = Ui.Stack();

            var picker = Ui.Choice(new[] { Selection, WholeModel }, scope, pick =>
            {
                scope = pick;

                // The writers keep the elements they were opened on; flipping this box does not
                // move them. Left unsaid, a coordinator reads a selection, widens the scope, and
                // writes "everything" onto forty elements without anything having disagreed.
                if (readScope != null && !string.Equals(readScope, pick, StringComparison.Ordinal))
                {
                    var was = readScope;
                    readScope = null;

                    results.Children.Add(Ui.Note("The scope box now says \"" + pick + "\", but the "
                        + "writers below still hold what was read from \"" + was + "\". Read again "
                        + "to point them at the new scope."));
                }
            });

            var read = Ui.Runner("Read what is there now", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var keys = new List<ElementKey>();
                var readFrom = scope;

                var traversal = readFrom == Selection
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.WholeDocument;

                foreach (var item in session.Model.Traverse(traversal))
                {
                    if (!job.Step(keys.Count, "elements read")) return;
                    keys.Add(item.Key);
                }

                if (keys.Count == 0)
                {
                    results.Children.Add(Ui.Empty(
                        readFrom == Selection ? "Nothing is selected." : "Nothing is loaded.",
                        readFrom == Selection
                            ? "Select elements in the model, or switch the scope to everything loaded."
                            : "Append a model first."));
                    return;
                }

                readScope = readFrom;

                results.Children.Add(Ui.Line(Ui.Count(keys.Count, "element") + " read from "
                    + (readFrom == Selection ? "the current selection" : "everything loaded")
                    + ". The writers below work on exactly those.", 1, true));

                editor.Children.Clear();
                editor.Children.Add(Writes(session, keys));
                editor.Children.Add(Correct(session, keys));
            });

            results.Children.Add(Bench.Ready(
                "Reading counts the elements in scope and opens the writers on exactly those.",
                "A write needs the values it would be overwriting, so reading comes first."));

            return Bench.Surface(
                Ui.Stack(Bench.Group(string.Empty, Ui.Field("Write to", picker))),
                read,
                Ui.Stack(results, editor));
        }

        /// <summary>
        /// Browse and edit properties in one place.
        ///
        /// The scope defaults to the current selection rather than the whole federation, because
        /// the whole federation is the answer nobody wants first and the one that takes longest to
        /// produce.
        /// </summary>
        public static UIElement Manager()
        {
            var scope = Selection;
            string? readScope = null;
            var results = Ui.Stack();
            var editor = Ui.Stack();

            var picker = Ui.Choice(new[] { Selection, WholeModel }, scope, pick =>
            {
                scope = pick;

                // Same rule as the Write tab: the table and the writers hold what was read, not
                // what the box says now, and the one moment to say so is the flip itself.
                if (readScope != null && !string.Equals(readScope, pick, StringComparison.Ordinal))
                {
                    var was = readScope;
                    readScope = null;

                    results.Children.Add(Ui.Note("The scope box now says \"" + pick + "\", but the "
                        + "table and the writers below still hold what was read from \"" + was
                        + "\". Read again to point them at the new scope."));
                }
            });

            var read = Ui.Runner("Read properties", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var keys = new List<ElementKey>();
                var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                var blanks = new Dictionary<string, int>(StringComparer.Ordinal);
                var readFrom = scope;

                var traversal = readFrom == Selection ? TraversalScope.CurrentSelection : TraversalScope.WholeDocument;

                foreach (var item in session.Model.Traverse(traversal))
                {
                    if (!job.Step(keys.Count, "elements read")) return;
                    keys.Add(item.Key);

                    foreach (var property in item.Properties())
                    {
                        var name = property.Category + " / " + property.Name;

                        if (string.IsNullOrEmpty(property.Value))
                        {
                            blanks[name] = blanks.TryGetValue(name, out var n) ? n + 1 : 1;
                            continue;
                        }

                        if (!values.TryGetValue(name, out var set))
                            values[name] = set = new HashSet<string>(StringComparer.Ordinal);

                        // Only the first few distinct values are ever shown, and a property with
                        // one value per element would otherwise hold the whole federation in memory.
                        if (set.Count < 6) set.Add(property.Value!);
                    }
                }

                if (keys.Count == 0)
                {
                    results.Children.Add(Ui.Empty(
                        readFrom == Selection ? "Nothing is selected." : "Nothing is loaded.",
                        readFrom == Selection
                            ? "Select elements in the model, or switch the scope to everything loaded."
                            : "Append a model first."));
                    return;
                }

                readScope = readFrom;

                var rows = values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p =>
                    new TableRow(p.Key,
                        p.Key,
                        p.Value.Count == 1 ? p.Value.First() : "(" + p.Value.Count + (p.Value.Count > 5 ? "+" : string.Empty) + " values)",
                        blanks.TryGetValue(p.Key, out var blank) && blank > 0
                            ? blank.ToString(CultureInfo.InvariantCulture) + " blank"
                            : string.Empty)).ToList();

                results.Children.Add(Ui.Line(Ui.Count(keys.Count, "element") + " read from "
                    + (readFrom == Selection ? "the current selection" : "everything loaded") + ", "
                    + Ui.Count(rows.Count, "property"), 1, true));

                var found = Ui.Table(rows, ("Property", 260), ("Value", 200), ("", 0));

                // What a coordinator does with this list is send it to whoever owns the model:
                // "these eleven are missing on half the elements". Picking the eleven is the point.
                results.Children.Add(Ui.Picking(found,
                    new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(found, picked), say),
                                    primary: true)));

                editor.Children.Clear();
                editor.Children.Add(Writes(session, keys));
                editor.Children.Add(Correct(session, keys));
                editor.Children.Add(new ColumnScreen(session, keys).Build());
            });

            results.Children.Add(Bench.Ready(
                "Reading lists every property the scope carries, with its values and its blanks.",
                "The writers open under the table, on exactly the elements that were read."));

            // Reading is the hinge: the scope in setup, what it found on the stage, and the
            // things you can then do to what it found below that.
            return Bench.Surface(
                Ui.Stack(Bench.Group(string.Empty, Ui.Field("Scope", picker))),
                read,
                Ui.Stack(
                    results,
                    editor,
                    Ui.Rule(),
                    Report(results),
                    Link(results),
                    Piece(results),
                    OnSite(results)));
        }

        /// <summary>
        /// The three places the model meets something that is not the model.
        ///
        /// Here because all three read the current selection, which is what this screen is about:
        /// where the weight of what is selected sits, the points somebody sets it out from, and
        /// the paperwork the model already says belongs to it.
        /// </summary>
        /// <param name="results">Where to say so if the window will not open.</param>
        private static UIElement OnSite(Panel results)
        {
            var open = Ui.Button("Take it to site...", () =>
            {
                var problem = ToolViews.Open("weight");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            return Ui.Stack(
                Heading("Off the screen and onto the ground"),
                Ui.Actions(open));
        }

        /// <summary>
        /// A piece of the federation, chosen by a rule rather than by clicking.
        ///
        /// Here rather than on its own tab because the rule is written in the same vocabulary this
        /// screen reads: a level, a zone, a property value. A person who has just seen what the
        /// model carries is one step from wanting the part of it that carries one particular thing.
        /// </summary>
        /// <param name="results">Where to say so if the window will not open.</param>
        private static UIElement Piece(Panel results)
        {
            var open = Ui.Button("Extract...", () =>
            {
                var problem = ToolViews.Open("extract");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            return Ui.Stack(
                Heading("Part of it, not all of it"),
                Ui.Actions(open));
        }

        /// <summary>
        /// Somebody else's spreadsheet, joined to the model in both directions.
        ///
        /// The round trip above works because CamelWorks put its own identities in the sheet. This
        /// is for the file it did not write - an asset register keyed on tag, a procurement extract
        /// keyed on order number - where the join has to be stated, and where the register owns
        /// half the columns and the model owns the other half.
        /// </summary>
        /// <param name="results">Where to say so if the window will not open.</param>
        private static UIElement Link(Panel results)
        {
            var open = Ui.Button("Link a sheet...", () =>
            {
                var problem = ToolViews.Open("datalink");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            return Ui.Stack(
                Heading("Somebody else's file"),
                Ui.Actions(open));
        }

        /// <summary>
        /// The way out of this screen and into a sheet.
        ///
        /// A person who has just read the properties and can see what the model carries is one step
        /// from wanting all of it in a file - as a door schedule, an asset register, a handover
        /// matrix. That is a different shape of job to browsing, so it is a window; but it belongs
        /// here, at the bottom of the screen where the question comes up.
        /// </summary>
        /// <param name="results">Where to say so if the window will not open.</param>
        private static UIElement Report(Panel results)
        {
            var open = Ui.Button("Report...", () =>
            {
                var problem = ToolViews.Open("report");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            return Ui.Stack(
                Heading("All of it, as a sheet"),
                Ui.Actions(open));
        }

        /// <summary>
        /// A small-caps heading over a block of stage content — the label a card's chrome used to
        /// pretend to carry, with the breathing room a bare eyebrow does not bring of its own.
        /// </summary>
        private static UIElement Heading(string text)
        {
            var eyebrow = Ui.Eyebrow(text);
            eyebrow.Margin = new Thickness(0, 14, 0, 6);
            return eyebrow;
        }

        /// <summary>
        /// What a screen says where its write button was, once the preview stopped describing what
        /// would happen. Said the same way on every screen that previews before it writes.
        /// </summary>
        private const string Outdated =
            "That changed what would be written, so the preview above is no longer what would "
            + "happen. Show it again before writing.";

        /// <summary>
        /// Assign a code, and refuse anything the table does not list.
        ///
        /// The point is the refusal. A lookup that silently passes an unlisted value is a lookup
        /// that does nothing — the whole reason a project keeps a code list is that "L3-MECH-01"
        /// and "L03 MECH 1" are the same thing to a person and two different things to every
        /// downstream tool, and the only moment either can be caught is the moment somebody writes
        /// one.
        ///
        /// So it reads a property, puts each value through the table, and writes only what comes
        /// back — in the table's own spelling, so a column ends up with one form of each code
        /// rather than four. What it refused is counted and named.
        /// </summary>
        private sealed class CodeScreen
        {
            private readonly Session _session;
            private readonly IReadOnlyList<ElementKey> _keys;

            private readonly ComboBox _source;
            private readonly TextBox _target;
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _applyRow = Ui.Stack();

            private ExistingValues _existing = ExistingValues.Keep;
            private CodeTable? _table;
            private WriteDiff? _diff;
            private bool _stale;

            internal CodeScreen(Session session, IReadOnlyList<ElementKey> keys)
            {
                // Editing either of these makes the preview a preview of something else, so both
                // of them drop it. Apply reads the target name live, which is how a preview of
                // "Code" could write into "Uniclass" without anybody being shown that.
                //
                // The source is a picker fed from the model, because the property a code table
                // reads is exactly the kind of name somebody otherwise retypes from an element.
                _source = Pickers.Property("Element / Classification", _ => Stale(), 240);
                _target = Ui.Text("Code", _ => Stale(), 160);

                _session = session;
                _keys = keys;
            }

            internal UIElement Build()
            {
                var tables = CodeTables.FromJson(
                    _session.Store.Section(ProjectStore.ProfileSection)["codeTables"]);

                if (tables.IsEmpty)
                    return Ui.Empty("No code tables on this project.",
                        "Project ▸ Profile takes one pasted straight out of the spreadsheet your "
                        + "client sent. Every other way of writing a value works without one.");

                _table = tables.Find(tables.Names[0]);

                var picker = Ui.Choice(tables.Names, tables.Names[0], pick =>
                {
                    _table = tables.Find(pick);
                    _target.Text = pick;
                });

                _target.Text = tables.Names[0];

                var preview = Ui.Runner("Show me what would change", _status, Preview);

                return Ui.Stack(
                    Ui.Form(
                        Ui.Field("Table", picker),
                        Ui.Field("Read from", _source,
                                 "picked from the model; \"Category / Name\" narrows it to one tab"),
                        Ui.Field("Write as", _target)),
                    preview,
                    Ui.Small("A value the table does not list is left alone and counted, never "
                             + "written and never guessed at."),
                    _status,
                    _applyRow);
            }

            /// <summary>
            /// Whether an element that already holds a value gets written over.
            ///
            /// Set from the one picker on the panel rather than owned here. Three write tools each
            /// with their own copy of this choice is three chances to have set the safe one and be
            /// looking at the other, over the single irreversible thing this product does.
            /// </summary>
            internal ExistingValues Existing
            {
                get => _existing;

                set
                {
                    _existing = value;
                    if (_diff != null) ShowApply();
                }
            }


            /// <summary>Read the source, check it against the table, and show the coverage and the diff.</summary>
            private void Preview(Job job)
            {
                _applyRow.Children.Clear();
                _diff = null;

                var table = _table;
                if (table == null) return;

                var target = _target.Text.Trim();

                if (target.Length == 0)
                {
                    _status.Children.Add(Ui.Problem("Give the property a name to write as."));
                    return;
                }

                var (category, name) = Split(_source.Text);

                if (name.Length == 0)
                {
                    _status.Children.Add(Ui.Problem(
                        "Say which property to read, as \"Category / Name\" - for example "
                        + "\"Element / Classification\"."));

                    return;
                }

                job.Say("Reading " + name + "...");

                var values = new List<string?>();

                foreach (var entry in PropertyDiff.Resolve(_session.Model, _keys))
                    values.Add(entry.Value?.Property(category, name));

                var coverage = table.Check(values);

                _status.Children.Add(Ui.Line(coverage.Explain(), 1, true));

                _status.Children.Add(Ui.Across(
                    Ui.Figure(Measure.Count(coverage.Coded), "in the table",
                              coverage.Coded > 0 ? Tone.Good : Tone.Plain),
                    Ui.Figure(Measure.Count(coverage.Uncoded), "carry nothing"),
                    Ui.Figure(Measure.Count(coverage.Invalid), "not in the table",
                              coverage.Invalid > 0 ? Tone.Bad : Tone.Good)));

                if (coverage.InvalidValues.Count > 0)
                    _status.Children.Add(Ui.Problem("Not in " + table.Name + ": "
                        + string.Join(", ", coverage.InvalidValues.Take(10))
                        + ". Those elements are left alone. Either the table is missing a row or the "
                        + "model is carrying a value nobody agreed to."));

                var diff = PropertyDiff.Of(NavWriteTransaction.TabName, target,
                    PropertyDiff.Resolve(_session.Model, _keys),
                    item => table.Canonical(item.Property(category, name))
                            ?? item.Property(NavWriteTransaction.TabName, target));

                _diff = diff;
                _stale = false;

                _status.Children.Add(Ui.Diff(diff));

                ShowApply();
            }

            /// <summary>"Category / Name" into its two halves. A bare name means any category.</summary>
            private static (string Category, string Name) Split(string? text)
            {
                var whole = (text ?? string.Empty).Trim();
                var slash = whole.IndexOf('/');

                return slash < 0
                    ? (string.Empty, whole)
                    : (whole.Substring(0, slash).Trim(), whole.Substring(slash + 1).Trim());
            }

            private void ShowApply()
            {
                _applyRow.Children.Clear();

                var diff = _diff;

                if (diff == null)
                {
                    if (_stale) _applyRow.Children.Add(Ui.Note(Outdated));
                    return;
                }

                var count = diff.Every.Count(c => PropertyDiff.ShouldWrite(c.Kind, _existing));

                if (count == 0)
                {
                    _applyRow.Children.Add(Ui.Note("Nothing would be written with that policy."));
                    return;
                }

                _applyRow.Children.Add(Ui.Button("Write " + Ui.Count(count, "element"), () => Apply(diff)));
            }

            /// <summary>
            /// Throw the preview away, because what it was a preview of has changed.
            ///
            /// Apply reads the target property name live, so a preview left standing over an
            /// edited input is a button that writes somewhere nobody was shown. Taking the button
            /// away is the fix: the only way to a write is a preview of that write.
            /// </summary>
            private void Stale()
            {
                if (_diff == null) return;

                _diff = null;
                _stale = true;

                _applyRow.Children.Clear();
                _applyRow.Children.Add(Ui.Note(Outdated));
            }

            private void Apply(WriteDiff diff)
            {
                _applyRow.Children.Clear();

                var target = _target.Text.Trim();
                int written;

                using (var write = new NavWriteTransaction(_session.Model, "CamelWorks: code " + target))
                {
                    foreach (var change in diff.Every)
                    {
                        if (!PropertyDiff.ShouldWrite(change.Kind, _existing)) continue;

                        write.SetProperty(change.Key, NavWriteTransaction.TabName, target, change.After);
                    }

                    written = write.Commit();
                }

                _status.Children.Add(Ui.Line("Wrote " + Ui.Count(written, "element")
                    + ", every one of them a value " + (_table?.Name ?? "the table") + " lists.", 1, true));

                _session.Record(ActivityKind.Write,
                    "assigned " + target + " from a code table onto " + Ui.Count(written, "element"));

                _diff = null;
            }
        }

        /// <summary>
        /// Out to a spreadsheet and back again, keyed.
        ///
        /// Sending properties out is a courtesy; taking them back is the feature. The whole reason
        /// a project exports a schedule is that somebody is going to fill in a column, and a tool
        /// that cannot read it back has moved the retyping rather than removed it.
        ///
        /// The sheet carries a written identity column so a returned row is matched on what it is
        /// rather than on what it is called — every keyed import in this industry matches on a mark
        /// or a description, and every one of them breaks the first time two doors are called
        /// "D-12". That column is the one column the import may never write back.
        ///
        /// Coming back in goes through exactly the same preview as any other write: a diff, element
        /// by element, before anything touches the model.
        /// </summary>
        private sealed class ExcelScreen
        {
            private readonly Session _session;
            private readonly IReadOnlyList<ElementKey> _keys;

            private readonly TextBox _out = Ui.Text(string.Empty, null, 340);
            private readonly TextBox _in = Ui.Text(string.Empty, null, 340);
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _applyRow = Ui.Stack();

            private ExistingValues _existing = ExistingValues.Keep;
            private SheetImportReport? _read;
            private string? _column;
            private string _property = string.Empty;
            private WriteDiff? _diff;

            internal ExcelScreen(Session session, IReadOnlyList<ElementKey> keys)
            {
                _session = session;
                _keys = keys;
            }

            /// <summary>
            /// Two legs of one trip, both on the glass.
            ///
            /// <b>Not a wizard, deliberately.</b> Sending a sheet out and reading it back are not
            /// two steps of one task - they are the same task on two different days, done by two
            /// different people, and a Next button between them would make the person who came
            /// back on Thursday click past the half they are not doing. Both legs are simply
            /// visible; the report underneath is shared, because it is the answer to whichever
            /// was pressed.
            /// </summary>
            internal UIElement Build() => Ui.Stack(
                OutField(),
                OutRunner(),
                Ui.Small("Every property these elements carry, one column each, plus a \""
                         + SheetImport.KeyHeading + "\" column that says which element a row "
                         + "is. Whoever fills it in does not need Navisworks."),
                ReadLeg(),
                _status,
                _applyRow);

            /// <summary>The out leg's one input: where the workbook goes.</summary>
            internal UIElement OutField() =>
                Ui.PathField("Write to", _out, PathKind.Save, "Excel|*.xlsx|All files|*.*",
                             "blank writes it to your documents");

            /// <summary>
            /// The button that writes the sheet — the trip's first leg, and the tool's one action.
            /// </summary>
            /// <param name="primary">False when it stands beside another primary act.</param>
            internal UIElement OutRunner(bool primary = true) =>
                Ui.Runner("Write a sheet", _status, Out, primary);

            /// <summary>
            /// The return leg: the file that came back, the read that previews it, and the rule
            /// about the identity column — a consequence no field label can carry.
            /// </summary>
            internal UIElement ReadLeg() => Ui.Stack(
                Ui.PathField("Read back", _in, PathKind.Open, "Excel|*.xlsx|All files|*.*"),
                Ui.Runner("Read a sheet back", _status, In, primary: false),
                Ui.Small("The \"" + SheetImport.KeyHeading + "\" column is how a returned row is "
                         + "matched to an element, it is never written back, and deleting it "
                         + "makes the sheet unreadable. Rows that match nothing are counted and "
                         + "named rather than skipped."));

            /// <summary>Where both legs report, shared because it answers whichever was pressed.</summary>
            internal Panel Status => _status;

            /// <summary>Where the write button lands once a read-back has shown its diff.</summary>
            internal Panel ApplyRow => _applyRow;

            /// <summary>
            /// Point the return leg at a file, for the screen that knows which sheet went out
            /// last. Only when the box is empty: a path somebody typed is not ours to replace.
            /// </summary>
            /// <param name="path">The workbook that went out.</param>
            internal void SuggestReturn(string path)
            {
                if (_in.Text.Trim().Length == 0) _in.Text = path;
            }

            /// <summary>
            /// Whether an element that already holds a value gets written over.
            ///
            /// Set from the one picker on the panel rather than owned here. Three write tools each
            /// with their own copy of this choice is three chances to have set the safe one and be
            /// looking at the other, over the single irreversible thing this product does.
            /// </summary>
            internal ExistingValues Existing
            {
                get => _existing;

                set
                {
                    _existing = value;
                    if (_diff != null) ShowApply();
                }
            }


            /// <summary>Every property these elements carry, one column each, plus the identity.</summary>
            private void Out(Job job)
            {
                _applyRow.Children.Clear();

                var path = _out.Text.Trim();

                if (path.Length == 0)
                {
                    path = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Deliverable.Safe(_session.Profile.ProjectName + " properties") + ".xlsx");

                    _out.Text = path;
                }

                job.Say("Reading properties...");

                var columns = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rows = new List<(ElementKey Key, string Name, Dictionary<string, string?> Values)>();

                var read = 0;

                foreach (var entry in PropertyDiff.Resolve(_session.Model, _keys))
                {
                    if (!job.Step(read++, "elements read")) return;
                    if (entry.Value == null) continue;

                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                    foreach (var property in entry.Value.Properties())
                    {
                        var name = property.Category + " / " + property.Name;

                        if (seen.Add(name)) columns.Add(name);

                        values[name] = property.Value;
                    }

                    rows.Add((entry.Key, entry.Value.DisplayName, values));
                }

                if (rows.Count == 0)
                {
                    _status.Children.Add(Ui.Empty("Nothing to write.",
                        "None of the elements that were read could be found in the model any more. "
                        + "Read the properties again."));
                    return;
                }

                // Alphabetical after the two fixed columns, so a sheet somebody opens next week has
                // the columns where they were last week.
                columns.Sort(StringComparer.OrdinalIgnoreCase);

                var sheet = new XlsxSheet("Properties");

                var header = new List<string?> { SheetImport.KeyHeading, "Element" };
                header.AddRange(columns);
                sheet.Row(header.ToArray());

                foreach (var row in rows)
                {
                    var cells = new List<string?> { row.Key.ToString(), row.Name };

                    foreach (var column in columns)
                        cells.Add(row.Values.TryGetValue(column, out var value) ? value : null);

                    sheet.Row(cells.ToArray());
                }

                job.Say("Writing...");

                try
                {
                    System.IO.File.WriteAllBytes(path, XlsxWriter.Write(new[] { sheet }));
                }
                catch (Exception error)
                {
                    _status.Children.Add(Ui.Problem("Could not write it: " + error.Message));
                    return;
                }

                _in.Text = path;

                _status.Children.Add(Ui.Line("Wrote " + Ui.Count(rows.Count, "element") + " and "
                    + Ui.Count(columns.Count, "property") + " to " + path, 1, true));

                _session.Record(ActivityKind.Export, "wrote properties to a spreadsheet", path);
            }

            /// <summary>Read a returned sheet, match it, and show what would change.</summary>
            private void In(Job job)
            {
                _applyRow.Children.Clear();
                _diff = null;

                var path = _in.Text.Trim();

                if (path.Length == 0)
                {
                    _status.Children.Add(Ui.Problem("Say which file to read back."));
                    return;
                }

                byte[] bytes;

                try
                {
                    bytes = System.IO.File.ReadAllBytes(path);
                }
                catch (Exception error)
                {
                    _status.Children.Add(Ui.Problem("Could not read it: " + error.Message));
                    return;
                }

                job.Say("Reading the sheet...");

                var book = XlsxReader.Read(bytes);

                if (!book.IsUsable)
                {
                    _status.Children.Add(Ui.Problem(book.Problem ?? "Nothing could be read from that file."));
                    return;
                }

                var known = new HashSet<string>(_keys.Select(k => k.ToString()), StringComparer.Ordinal);

                var report = SheetImport.Of(book.Sheet("Properties") ?? book.First, known);
                _read = report;

                _status.Children.Add(Ui.Line(report.Explain(), 1, true));

                if (report.Problem != null)
                {
                    _status.Children.Add(Ui.Problem(report.Problem));
                    return;
                }

                Unplaceable(report);

                if (!report.IsUsable) return;

                var writable = report.Writable.Where(w => !string.Equals(w, "Element",
                    StringComparison.OrdinalIgnoreCase)).ToList();

                if (writable.Count == 0)
                {
                    _status.Children.Add(Ui.Empty("Nothing to bring back.",
                        "That sheet has an identity column and nothing else worth writing."));
                    return;
                }

                _column = writable[0];
                _property = Suggested(_column);

                // A picker, so the returned column can land on a property spelled the way the
                // model already spells it rather than the way the sheet's author did.
                var name = Pickers.Property(_property, null, 200);
                var picker = Ui.Choice(writable, _column, pick =>
                {
                    _column = pick;
                    name.Text = Suggested(pick);
                });

                var preview = Ui.Runner("Show me what it would write", _status,
                    inner => Preview(inner, name.Text));

                _status.Children.Add(Ui.Field("Bring back the column", picker));
                _status.Children.Add(Ui.Field("Write it as", name));
                _status.Children.Add(Ui.Across(preview));
            }

            /// <summary>What a sheet column should be called on the element, before anybody edits it.</summary>
            private static string Suggested(string? column)
            {
                if (string.IsNullOrWhiteSpace(column)) return "Value";

                var slash = column!.LastIndexOf('/');

                return slash < 0 ? column.Trim() : column.Substring(slash + 1).Trim();
            }

            /// <summary>Say what could not be placed, in full, before offering to write anything.</summary>
            private void Unplaceable(SheetImportReport report)
            {
                if (report.Unmatched.Count > 0)
                    _status.Children.Add(Ui.Problem(
                        Ui.Count(report.Unmatched.Count, "row") + " in that sheet match no element in "
                        + "scope - rows " + string.Join(", ", report.Unmatched.Take(8).Select(r => r.Number))
                        + (report.Unmatched.Count > 8 ? " and more" : string.Empty)
                        + ". They were added after the export, belong to another package, or name "
                        + "elements this model no longer has. Nothing will be written for them."));

                if (report.Duplicates.Count > 0)
                    _status.Children.Add(Ui.Problem(
                        Ui.Count(report.Duplicates.Count, "row") + " repeat an identity an earlier row "
                        + "already claimed - rows "
                        + string.Join(", ", report.Duplicates.Take(8).Select(r => r.Number))
                        + ". The first row wins and these are ignored."));

                if (report.Blank > 0)
                    _status.Children.Add(Ui.Note(
                        Ui.Count(report.Blank, "row") + " carry values but no identity, so there is no "
                        + "way to tell which element they are about."));
            }

            /// <summary>The diff, from the chosen column, through the same path as every other write.</summary>
            private void Preview(Job job, string property)
            {
                _applyRow.Children.Clear();
                _diff = null;

                var report = _read;
                if (report == null) return;

                var name = property.Trim();

                if (name.Length == 0)
                {
                    _status.Children.Add(Ui.Problem("Give the property a name first."));
                    return;
                }

                _property = name;

                var values = report.Column(report.ColumnOf(_column));

                job.Say("Reading what is there now...");

                var diff = PropertyDiff.Of(NavWriteTransaction.TabName, name,
                    PropertyDiff.Resolve(_session.Model, _keys),
                    item => values.TryGetValue(item.Key.ToString(), out var value)
                        ? value
                        : item.Property(NavWriteTransaction.TabName, name));

                _diff = diff;

                _status.Children.Add(Ui.Diff(diff));

                ShowApply();
            }

            private void ShowApply()
            {
                _applyRow.Children.Clear();

                var diff = _diff;
                if (diff == null) return;

                var count = diff.Every.Count(c => PropertyDiff.ShouldWrite(c.Kind, _existing));

                if (count == 0)
                {
                    _applyRow.Children.Add(Ui.Note("Nothing would be written with that policy."));
                    return;
                }

                _applyRow.Children.Add(Ui.Button("Write " + Ui.Count(count, "element"), () => Apply(diff)));
            }

            private void Apply(WriteDiff diff)
            {
                _applyRow.Children.Clear();

                int written;

                using (var write = new NavWriteTransaction(_session.Model,
                           "CamelWorks: " + _property + " from a spreadsheet"))
                {
                    // Queued from the diff rather than from the scope, so an element already
                    // holding the value is not written to at all - and an element the sheet said
                    // nothing about is not touched, because its "after" is its "before".
                    //
                    // Every, not Changes: Changes is the first twenty-five rows, which is what the
                    // screen shows. Writing from it wrote twenty-five elements and reported that it
                    // had, which is the worst way for this to be wrong.
                    foreach (var change in diff.Every)
                    {
                        if (!PropertyDiff.ShouldWrite(change.Kind, _existing)) continue;

                        write.SetProperty(change.Key, NavWriteTransaction.TabName, _property, change.After);
                    }

                    written = write.Commit();
                }

                _status.Children.Add(Ui.Line("Wrote " + Ui.Count(written, "element") + ".", 1, true));

                _session.Record(ActivityKind.Write,
                    "brought " + _property + " back from a spreadsheet onto "
                    + Ui.Count(written, "element"));

                // The diff describes a model that no longer exists. Pressing the button twice would
                // report a second write of nothing and read as if the first had failed.
                _diff = null;
            }
        }

        /// <summary>What the find-and-replace matching picker says for each kind.</summary>
        private const string Anywhere = "anywhere in the value";
        private const string WholeValue = "the whole value";
        private const string AtStart = "at the start";
        private const string AtEnd = "at the end";
        private const string LikePattern = "a pattern with * and ?";

        /// <summary>"Category / Name" into its two halves. A bare name means any category.</summary>
        /// <param name="text">What was typed.</param>
        private static (string Category, string Name) NameOf(string? text)
        {
            var whole = (text ?? string.Empty).Trim();
            var slash = whole.IndexOf('/');

            return slash < 0
                ? (string.Empty, whole)
                : (whole.Substring(0, slash).Trim(), whole.Substring(slash + 1).Trim());
        }

        private const string KeepThem = "leave it alone";

        private const string ReplaceThem = "replace it";

        /// <summary>
        /// Correct a value everywhere it appears.
        ///
        /// Every federation carries a property somebody spelled two ways. Every filter, set rule and
        /// schedule join then quietly covers half of what it should, and the discovery is a count
        /// wrong by a number nobody can explain. Fixing it in the authoring tool means six models
        /// and six people; fixing it here means one press, and the reason nobody does it today is
        /// that there is nothing to press.
        /// </summary>
        private static UIElement Correct(Session session, IReadOnlyList<ElementKey> keys)
        {
            var rule = new Replacement { Kind = MatchKind.Part };

            // A picker rather than a bare box: the property being corrected is by definition one
            // the model already carries, so its spelling is in the dropdown.
            var property = Pickers.Property(string.Empty, text =>
            {
                var (category, name) = NameOf(text);
                rule.Category = category;
                rule.Name = name;
            }, 220);

            var find = Ui.Text(string.Empty, text => rule.Find = text, 160);
            var with = Ui.Text(string.Empty, text => rule.ReplaceWith = text, 160);

            var how = Ui.Choice(new[] { Anywhere, WholeValue, AtStart, AtEnd, LikePattern }, Anywhere,
                pick => rule.Kind = pick == WholeValue ? MatchKind.Whole
                                  : pick == AtStart ? MatchKind.Start
                                  : pick == AtEnd ? MatchKind.End
                                  : pick == LikePattern ? MatchKind.Pattern
                                  : MatchKind.Part, 200);

            var cased = Ui.Check("Case has to match", false, v => rule.CaseSensitive = v);

            List<(ElementKey Key, string After)>? staged = null;

            var apply = Ui.Commit("Show me what it would correct", "Correct", "element",
                plan: (job, into) =>
                {
                    staged = null;

                    var refusal = rule.Refusal;
                    if (refusal != null)
                    {
                        into.Children.Add(Ui.Problem(refusal));
                        return 0;
                    }

                    var pending = new List<(ElementKey Key, string After)>();
                    var values = new List<string?>();
                    var read = 0;

                    foreach (var pair in PropertyDiff.Resolve(session.Model, keys))
                    {
                        if (!job.Step(read++, keys.Count, "values read")) return 0;
                        if (pair.Value == null) continue;

                        var now = pair.Value.Property(rule.Category, rule.Name);
                        values.Add(now);

                        var after = FindReplace.Apply(now, rule);

                        if (after != null && !string.Equals(after, now, StringComparison.Ordinal))
                            pending.Add((pair.Key, after));
                    }

                    var count = FindReplace.Rehearse(values, rule);

                    into.Children.Add(Ui.Line(count.Explain(), 1, true));

                    // Before the press, not after. A rule that compounds is one somebody will run
                    // twice because they cannot remember whether they already did.
                    if (rule.Caution != null) into.Children.Add(Ui.Note(rule.Caution));

                    if (count.Examples.Count > 0)
                    {
                        var rows = count.Examples
                            .Select(e => new TableRow(e, e.From, e.To))
                            .ToList();

                        var examples = Ui.Table(rows, ("There now", 240), ("Would be", 240));

                        // Picking here changes nothing about what gets written - the apply works
                        // off the staged set. It is for taking the before-and-after pairs into the
                        // message that asks somebody whether this rename is right.
                        into.Children.Add(Ui.Picking(examples,
                            new Ui.OnPicked("Copy {n}",
                                (picked, say) => Ui.Copy(Ui.Lines(examples, picked), say, "example"))));
                    }

                    staged = pending;
                    return pending.Count;
                },
                apply: job =>
                {
                    var writing = staged;
                    if (writing == null) return;

                    using (var write = new NavWriteTransaction(session.Model, "CamelWorks: correct a value"))
                    {
                        var done = 0;

                        foreach (var entry in writing)
                        {
                            if (!job.Step(done++, writing.Count, "elements corrected")) return;
                            write.SetProperty(entry.Key, rule.Category, rule.Name, entry.After);
                        }

                        job.Say("Writing...");
                        var written = write.Commit();

                        session.Record(ActivityKind.Write,
                            "corrected " + rule.Name + " on " + Ui.Count(written, "element"));
                    }
                });

            return Ui.Stack(
                Heading("Correct a value"),
                Ui.Form(
                    Ui.Field("Property", property,
                             "picked from the model; a bare name matches whichever tab carries it"),
                    Ui.Pair(Ui.Field("Find", find), Ui.Field("Replace with", with)),
                    Ui.Field("Matching", how)),
                cased,
                apply);
        }

        /// <summary>
        /// The workbook round-trip, as its own tool.
        ///
        /// Its ribbon button opened Data Manager, because this was one of four staging screens
        /// behind a picker there. It is the only one of the four whose whole point is that
        /// somebody without Navisworks does the editing, which is why it earns a window rather
        /// than a slot in a picker beside three ways of typing a value in.
        /// </summary>
        public static UIElement Excel()
        {
            var session = Host.Current;

            if (session == null)
                return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

            var keys = session.Model.SelectedKeys();
            var screen = new ExcelScreen(session, keys);

            screen.Status.Children.Add(Bench.Ready(
                "Writing a sheet puts every property "
                + (keys.Count == 0 ? "the model carries" : "of the " + Ui.Count(keys.Count, "selected element"))
                + " into a workbook anyone can edit without Navisworks.",
                "Reading it back previews a diff, element by element, before anything is written."));

            // The two legs of the trip happen days apart, often to different people, and the
            // activity log is the only place that remembers which file went out in between. The
            // person opening this tab on Thursday is here for the return leg, so the file that
            // went out is stated and offered as the file to read back.
            var trip = session.Store.Activity.Entries.FirstOrDefault(a =>
                string.Equals(a.Kind, ActivityKind.Export, StringComparison.Ordinal)
                && a.Summary.StartsWith("wrote properties to a spreadsheet", StringComparison.Ordinal));

            if (trip != null)
            {
                var back = session.Store.Activity.Entries.FirstOrDefault(a =>
                    string.Equals(a.Kind, ActivityKind.Write, StringComparison.Ordinal)
                    && a.Summary.StartsWith("brought ", StringComparison.Ordinal));

                var returned = back != null && back.WhenTicks >= trip.WhenTicks;

                screen.Status.Children.Add(Ui.Line("The last sheet went out "
                    + ActivityLog.Ago(trip.WhenTicks, session.NowTicks)
                    + (trip.Detail == null ? string.Empty : " to " + trip.Detail)
                    + (returned
                        ? ", and edits came back " + ActivityLog.Ago(back!.WhenTicks, session.NowTicks) + "."
                        : ", and nothing has come back in since."), 0.7));

                if (trip.Detail != null) screen.SuggestReturn(trip.Detail);
            }

            return Bench.Surface(
                Ui.Stack(
                    Bench.Scope(keys.Count == 0
                        ? "Nothing is selected, so this works on the whole model."
                        : Ui.Count(keys.Count, "element") + " selected. The sheet carries those."),
                    Bench.Group(string.Empty, screen.OutField())),
                screen.OutRunner(),
                Ui.Stack(
                    Heading("Bring it back"),
                    screen.ReadLeg(),
                    screen.Status,
                    screen.ApplyRow));
        }

        /// <summary>
        /// One write pipeline, every source of a value on the glass at once.
        ///
        /// It used to be four bodies behind a picker, which read as one tool but showed a quarter
        /// of it — and the quarter on screen decided which fields somebody thought existed. Each
        /// source is a group under its own heading now: a value you type, a formula, a code table,
        /// a spreadsheet. All of them write into CamelWorks' own tab, and none of them writes
        /// until a diff has been shown, element by element.
        ///
        /// The one overwrite policy still governs all four. Four copies of "if a value is already
        /// there" is four chances to have set the safe one and be looking at the other, over the
        /// single irreversible thing this product does.
        /// </summary>
        private static UIElement Writes(Session session, IReadOnlyList<ElementKey> keys)
        {
            var typed = new WriteScreen(session, keys);
            var worked = new WriteScreen(session, keys);
            var coded = new CodeScreen(session, keys);
            var sheet = new ExcelScreen(session, keys);

            var told = new Action<ExistingValues>[]
            {
                v => typed.Existing = v,
                v => worked.Existing = v,
                v => coded.Existing = v,
                v => sheet.Existing = v,
            };

            var policy = Ui.Choice(new[] { KeepThem, ReplaceThem }, KeepThem, pick =>
            {
                var existing = pick == ReplaceThem ? ExistingValues.Overwrite : ExistingValues.Keep;
                foreach (var tell in told) tell(existing);
            });

            return Ui.Stack(
                Heading("Write to the elements"),
                Ui.Form(Ui.Field("If a value is already there", policy,
                    "one answer for every writer below")),
                Heading("A value you type"),
                typed.Build(false),
                Heading("A formula over other properties"),
                worked.Build(true),
                Heading("A code table"),
                coded.Build(),
                Heading("A spreadsheet, out and back"),
                sheet.Build(),
                Ui.Small("Everything lands in the \"" + NavWriteTransaction.TabName + "\" tab, so "
                         + "it is never mixed with the authoring tool's data. Ctrl+Z will not undo "
                         + "a write: Navisworks keeps property writes off its undo stack, so "
                         + "CamelWorks records what it changed in the project file instead of "
                         + "pretending otherwise."));
        }

        /// <summary>
        /// Writing a property, in two steps, and never over somebody else's answer by accident.
        ///
        /// Writing into a model is the one destructive thing CamelWorks does, and it is destructive
        /// in a way Ctrl+Z does not reach — Navisworks does not put user-defined property writes on
        /// its undo stack. So the button that writes is not the button you press first. You see the
        /// diff, element by element, and then you decide.
        ///
        /// <b>Existing values are kept unless you say otherwise.</b> The failure this prevents is
        /// mundane and expensive: a coordinator runs a zoning rule on Tuesday, a colleague runs
        /// their own on Wednesday, and the first person's answers are gone with nothing to say they
        /// were ever there. Keeping is the default; replacing is a choice made in front of a count
        /// of exactly what would be replaced.
        /// </summary>
        private sealed class WriteScreen
        {
            private readonly Session _session;
            private readonly IReadOnlyList<ElementKey> _keys;

            /// <summary>What the value picker says for each of the two ways to make one.</summary>
            private const string Fixed = "the same for every element";

            private const string Worked = "worked out from other properties";

            private readonly ComboBox _name;
            private readonly TextBox _value;
            private readonly TextBox _formula;
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _applyRow = Ui.Stack();

            private ExistingValues _existing = ExistingValues.Keep;
            private string _how = Fixed;
            private WriteDiff? _diff;
            private Formula? _parsed;
            private bool _stale;

            internal WriteScreen(Session session, IReadOnlyList<ElementKey> keys)
            {
                // All three drop the preview when they change. This screen is the worst case of
                // the three that preview before writing: Apply reads the value box live, so
                // previewing "Zone A" over forty elements and then typing "Zone B" left a button
                // reading "Write 40 elements" that wrote Zone B - a value nobody was ever shown,
                // under a count worked out for a different one. The formula path was stale the
                // other way round, applying the parsed formula from the last preview rather than
                // the text on screen. Two different wrong answers in one screen.
                // The name is a picker: half the time the property being written already exists
                // on part of the model, and the naming standard's spelling is in the dropdown
                // rather than in whoever's memory is filling it in.
                _name = Pickers.Property("Zone", _ => Stale(), 150);
                _value = Ui.Text(string.Empty, _ => Stale(), 200);
                _formula = Ui.Text(string.Empty, _ => Stale(), 340);

                _session = session;
                _keys = keys;
            }

            /// <summary>Throw the preview away, because what it was a preview of has changed.</summary>
            private void Stale()
            {
                if (_diff == null) return;

                _diff = null;
                _parsed = null;
                _stale = true;

                _applyRow.Children.Clear();
                _applyRow.Children.Add(Ui.Note(Outdated));
            }

            /// <summary>The writer for one kind of value.</summary>
            /// <param name="worked">Whether the value is a formula rather than one typed in.</param>
            internal UIElement Build(bool worked)
            {
                _how = worked ? Worked : Fixed;

                // One input, never both. A screen showing a Value box and a Formula box at once is
                // asking which of them it is going to use, and answering in a third field.
                var input = worked
                    ? Ui.Field("Formula", _formula, "reads other properties: {Element/Mark}")
                    : Ui.Field("Value", _value);

                // The syntax goes below the well rather than inside it: it is reference material
                // about a field, not another thing to fill in.
                var help = worked
                    ? Ui.Stack(
                        Ui.Small("Join text with & and do arithmetic with + - * / — two different "
                                 + "operators on purpose, because one that did both would turn a "
                                 + "door reference of \"12\" and a level of \"3\" into 15 instead "
                                 + "of \"12-3\". An element missing something the formula needs is "
                                 + "left alone rather than written a zero."),
                        Ui.Small("regex(value, pattern) pulls out what the brackets caught and "
                                 + "regex(value, pattern, replacement) rewrites; "
                                 + "unit(number, \"mm\", \"m\") moves a number between units; "
                                 + "lookup(value, \"Uniclass\") reads a code table and gives "
                                 + "nothing for a value it does not list; upper, lower and trim "
                                 + "tidy."))
                    : null;

                var preview = Ui.Runner("Show me what would change", _status, job =>
                {
                    _diff = null;
                    _parsed = null;
                    _applyRow.Children.Clear();

                    var name = _name.Text.Trim();

                    if (name.Length == 0)
                    {
                        _status.Children.Add(Ui.Problem("Give the property a name first."));
                        return;
                    }

                    if (_how == Worked && !Read()) return;

                    job.Say("Reading what is there now...");

                    var diff = _parsed == null
                        ? PropertyDiff.Of(NavWriteTransaction.TabName, name,
                            PropertyDiff.Resolve(_session.Model, _keys), _value.Text)
                        : PropertyDiff.Of(NavWriteTransaction.TabName, name,
                            PropertyDiff.Resolve(_session.Model, _keys), ValueOf(_parsed));

                    _diff = diff;
                    _stale = false;

                    if (_parsed != null) Unworkable(diff);

                    _status.Children.Add(Ui.Line(diff.Explain(), 1, true));

                    _status.Children.Add(Ui.Across(
                        Ui.Figure(diff.Added.ToString("N0", CultureInfo.InvariantCulture), "to add",
                                  diff.Added > 0 ? Tone.Good : Tone.Plain),
                        Ui.Figure(diff.Changed.ToString("N0", CultureInfo.InvariantCulture), "already set",
                                  diff.Changed > 0 ? Tone.Warn : Tone.Plain),
                        Ui.Figure(diff.Cleared.ToString("N0", CultureInfo.InvariantCulture), "would empty",
                                  diff.Cleared > 0 ? Tone.Bad : Tone.Plain),
                        Ui.Figure(diff.Unchanged.ToString("N0", CultureInfo.InvariantCulture), "already right")));

                    if (diff.Before.Count > 0)
                        _status.Children.Add(Ui.Line("There now: "
                            + string.Join(", ", diff.Before.Select(v => v.ToString())), 0.7));

                    if (diff.Changes.Count > 0)
                    {
                        var rows = diff.Changes.Select(c => new TableRow(c,
                            c.Label,
                            c.Before ?? "-",
                            c.After ?? "-",
                            Word(c.Kind))
                        {
                            Tone = c.Kind == WriteChange.Added ? Tone.Good : Tone.Warn,
                        }).ToList();

                        var changing = Ui.Table(rows,
                            ("Element", 200), ("Now", 130), ("Would become", 130), ("", 0));

                        _status.Children.Add(Ui.Picking(changing,
                            new Ui.OnPicked("Copy {n}",
                                (picked, say) => Ui.Copy(Ui.Lines(changing, picked), say, "change"))));

                        if (diff.Added + diff.Changed + diff.Cleared > diff.Changes.Count)
                            _status.Children.Add(Ui.Line("First " + diff.Changes.Count
                                + " of them; the counts above are all of them.", 0.6));
                    }

                    if (diff.Missing.Count > 0)
                        _status.Children.Add(Ui.Note(Ui.Count(diff.Missing.Count, "element")
                            + " no longer resolve and will be skipped. They were read before the model "
                            + "changed under them; re-read the scope to see the current answer."));

                    if (diff.IsEmpty)
                    {
                        _status.Children.Add(Ui.Empty("Nothing to write.",
                            "Every element in scope already holds exactly this value. Writing it "
                            + "again would be a few hundred thousand host calls to no effect."));
                        return;
                    }

                    ShowApply();
                });

                return Ui.Stack(
                    Ui.Form(
                        Ui.Field("Name", _name),
                        input),
                    help,
                    preview,
                    _status,
                    _applyRow);
            }

            /// <summary>
            /// Whether an element that already holds a value gets written over.
            ///
            /// Set from the one picker on the panel rather than owned here. Three write tools each
            /// with their own copy of this choice is three chances to have set the safe one and be
            /// looking at the other, over the single irreversible thing this product does.
            /// </summary>
            internal ExistingValues Existing
            {
                get => _existing;

                set
                {
                    _existing = value;
                    if (_diff != null) ShowApply();
                }
            }


            /// <summary>
            /// The button that actually writes, labelled with what it is about to do.
            ///
            /// It says the number rather than saying "Apply", because the number is the thing worth
            /// checking and a button that hides it is asking you to trust a screen you have already
            /// scrolled past.
            /// </summary>
            private void ShowApply()
            {
                _applyRow.Children.Clear();

                var diff = _diff;

                if (diff == null)
                {
                    if (_stale) _applyRow.Children.Add(Ui.Note(Outdated));
                    return;
                }

                var writes = diff.Writes(_existing);

                if (writes == 0)
                {
                    _applyRow.Children.Add(Ui.Note(
                        "Nothing would be written: every element in scope already holds a value, and "
                        + "existing values are being left alone. Change what happens to a value that "
                        + "is already there, above, if replacing them is what you meant."));
                    return;
                }

                var skipped = diff.Added + diff.Changed + diff.Cleared - writes;

                _applyRow.Children.Add(Ui.Button("Write " + Ui.Count(writes, "element"), () => Apply(diff)));

                if (skipped > 0)
                    _applyRow.Children.Add(Ui.Line(Ui.Count(skipped, "element")
                        + " will be left as they are because they already hold something.", 0.7));
            }

            private void Apply(WriteDiff diff)
            {
                var name = diff.Name;
                var written = 0;

                using (var write = new NavWriteTransaction(_session.Model, "CamelWorks: set " + name))
                {
                    // Queued from the diff rather than from the scope, so an element that already
                    // holds the value is not written to at all. On a federation that is the
                    // difference between a re-run costing nothing and costing a minute.
                    var text = _value.Text;
                    var formula = _parsed;

                    Func<IModelItem, string?> value;

                    if (formula != null) value = ValueOf(formula);
                    else value = _ => text;

                    foreach (var entry in PropertyDiff.Resolve(_session.Model, _keys))
                    {
                        var item = entry.Value;
                        if (item == null) continue;

                        var next = value(item);

                        // A formula that could not be worked out for this element writes nothing at
                        // all, rather than emptying whatever is there. Leaving it alone is the same
                        // rule the preview counted it under.
                        if (_parsed != null && next == null) continue;

                        var kind = PropertyDiff.Classify(item.Property(NavWriteTransaction.TabName, name), next);

                        if (!PropertyDiff.ShouldWrite(kind, _existing)) continue;

                        write.SetProperty(entry.Key, NavWriteTransaction.TabName, name, next);
                    }

                    written = write.Commit();
                }

                _applyRow.Children.Add(Ui.Line("Wrote " + Ui.Count(written, "element") + ".", 1, true));

                if (_existing == ExistingValues.Overwrite && diff.Changed > 0)
                    _applyRow.Children.Add(Ui.Note(Ui.Count(diff.Changed, "element")
                        + " held a different value and now hold this one. What they held is in the "
                        + "project journal, which is the only way back."));

                _session.Record(ActivityKind.Write,
                    "set " + name + " on " + Ui.Count(written, "element")
                    + (_existing == ExistingValues.Overwrite ? ", replacing existing values" : ""));

                // The diff describes a model that no longer exists. Making the button press once is
                // not a nicety: pressing it twice would report a second write of nothing and read as
                // if the first had failed.
                _diff = null;
            }

            /// <summary>
            /// Read the formula box, reporting what is wrong with it rather than refusing silently.
            /// </summary>
            private bool Read()
            {
                var formula = Formula.Parse(_formula.Text);

                if (!formula.IsUsable)
                {
                    _status.Children.Add(Ui.Problem(formula.Problem ?? "That is not a formula."));
                    return false;
                }

                _parsed = formula;

                _status.Children.Add(Ui.Line("Reads " + string.Join(", ",
                    formula.References.Select(r => r.ToString())) + ".", 0.6));

                return true;
            }

            /// <summary>The value function for a formula: null for an element it cannot be worked out for.</summary>
            private Func<IModelItem, string?> ValueOf(Formula formula)
            {
                var lookup = Lookup(_session);

                return item => formula.Evaluate((category, name) => category.Length == 0
                    ? AnyTab(item, name)
                    : item.Property(category, name), lookup).Value;
            }

            /// <summary>
            /// The code-table reader a formula's lookup() uses, or null when the project keeps none.
            ///
            /// Read once per preview rather than per element: a federation is a hundred thousand
            /// elements and the tables do not change while a formula runs.
            /// </summary>
            internal static Func<string, string, string?>? Lookup(Session session)
            {
                var tables = CodeTables.FromJson(
                    session.Store.Section(ProjectStore.ProfileSection)["codeTables"]);

                if (tables.IsEmpty) return null;

                return (table, value) =>
                {
                    var found = tables.Find(table)?.Find(value);

                    return found?.Description ?? found?.Code;
                };
            }

            /// <summary>
            /// A property by name, whichever tab it is under.
            ///
            /// What somebody means when they write {Level} rather than {Element/Level}. The first
            /// match wins, and the tab is worth naming when a federation carries the same property
            /// name under two of them — which the note on the card says.
            /// </summary>
            private static string? AnyTab(IModelItem item, string name)
            {
                foreach (var property in item.Properties())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(property.Value))
                        return property.Value;
                }

                return null;
            }

            /// <summary>Say how many elements the formula could not be worked out for.</summary>
            private void Unworkable(WriteDiff diff)
            {
                // Everything the formula produced nothing for lands in Unchanged, because a null
                // value against a property that is not there is no change. Naming that number is
                // the difference between "it did nothing" and "it did nothing to these".
                var blank = diff.Unchanged;
                if (blank <= 0) return;

                _status.Children.Add(Ui.Note(Ui.Count(blank, "element")
                    + " either already hold this value or carry nothing the formula needs. The ones "
                    + "missing a property are left alone rather than written an empty value — an "
                    + "element with no width has no area, and a zero would go into a schedule "
                    + "somebody adds up."));
            }

            private static string Word(WriteChange kind) => kind switch
            {
                WriteChange.Added => "new",
                WriteChange.Changed => "replaces a value",
                WriteChange.Cleared => "empties it",
                _ => "no change",
            };
        }

        /// <summary>
        /// Renaming and removing a column CamelWorks put there.
        ///
        /// The two operations nobody thinks about until the day they need them, and by then the
        /// column is on forty thousand elements and the only alternative is a script. Both are
        /// scoped to whatever was read, both count before they act, and both refuse to touch a tab
        /// CamelWorks does not own — the authoring tool's data is not ours to rename.
        /// </summary>
        private sealed class ColumnScreen
        {
            private readonly Session _session;
            private readonly IReadOnlyList<ElementKey> _keys;

            // The column is a picker - it exists on the model or there is nothing to rename. The
            // new name stays a box, because a new name is by definition not in any dropdown.
            private readonly ComboBox _column = Pickers.Property(string.Empty, null, 150);
            private readonly TextBox _renamed = Ui.Text(string.Empty, null, 150);
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _actions = Ui.Stack();

            internal ColumnScreen(Session session, IReadOnlyList<ElementKey> keys)
            {
                _session = session;
                _keys = keys;
            }

            internal UIElement Build()
            {
                var count = Ui.Runner("Count it", _status, job =>
                {
                    _actions.Children.Clear();

                    var column = _column.Text.Trim();

                    if (column.Length == 0)
                    {
                        _status.Children.Add(Ui.Problem("Name the column first."));
                        return;
                    }

                    job.Say("Reading...");

                    var census = PropertyDiff.Census(NavWriteTransaction.TabName, column,
                        PropertyDiff.Resolve(_session.Model, _keys));

                    if (census.Present == 0)
                    {
                        _status.Children.Add(Ui.Empty("Nothing carries it.",
                            "No element in this scope has a \"" + column + "\" in the "
                            + NavWriteTransaction.TabName + " tab. Check the spelling, or widen the "
                            + "scope and read again."));
                        return;
                    }

                    _status.Children.Add(Ui.Line(Ui.Count(census.Present, "element")
                        + " carry it, out of " + Ui.Count(census.Scope, "element") + " in scope.", 1, true));

                    _status.Children.Add(Ui.Line("Values: "
                        + string.Join(", ", census.Before.Where(v => v.Value != null).Select(v => v.ToString())), 0.7));

                    _actions.Children.Add(Ui.Across(
                        Ui.Button("Rename it", () => Rename(column, census.Present)),

                        // Red, and asked twice. Stripping a column off forty thousand elements is
                        // the harshest act on this tab and it used to be the same grey rectangle
                        // as the rename beside it.
                        Ui.Danger("Remove it from " + Ui.Count(census.Present, "element"), () =>
                        {
                            _actions.Children.Clear();

                            _actions.Children.Add(Ui.Danger(
                                "Yes, remove \"" + column + "\" from "
                                + Ui.Count(census.Present, "element"),
                                () => Remove(column, census.Present)));

                            _actions.Children.Add(Ui.Small("Ctrl+Z will not bring it back."));
                        })));
                });

                return Ui.Stack(
                    Heading("Rename or remove a column"),
                    Ui.Form(Ui.Pair(
                        Ui.Field("Column", _column, "as it is spelled in the "
                                                    + NavWriteTransaction.TabName + " tab"),
                        Ui.Field("New name", _renamed))),
                    count,
                    Ui.Small("Only columns in CamelWorks' own tab: renaming what the authoring "
                             + "tool wrote would make the model disagree with the model it came "
                             + "from."),
                    _status,
                    _actions);
            }

            /// <summary>
            /// Copy to the new name, then remove the old — in that order, in one transaction.
            ///
            /// The order matters if it fails halfway: a crash after the copy leaves both columns,
            /// which somebody can see and sort out. A crash after the removal would leave neither.
            /// </summary>
            private void Rename(string column, int carriers)
            {
                var renamed = _renamed.Text.Trim();

                if (renamed.Length == 0)
                {
                    _actions.Children.Add(Ui.Problem("Type the new name first."));
                    return;
                }

                if (string.Equals(renamed, column, StringComparison.OrdinalIgnoreCase))
                {
                    _actions.Children.Add(Ui.Problem("That is the name it already has."));
                    return;
                }

                var moved = 0;

                using (var write = new NavWriteTransaction(_session.Model,
                           "CamelWorks: rename " + column + " to " + renamed))
                {
                    foreach (var entry in PropertyDiff.Resolve(_session.Model, _keys))
                    {
                        var item = entry.Value;
                        if (item == null) continue;

                        var value = item.Property(NavWriteTransaction.TabName, column);
                        if (string.IsNullOrEmpty(value)) continue;

                        write.SetProperty(entry.Key, NavWriteTransaction.TabName, renamed, value);
                        write.RemoveProperty(entry.Key, NavWriteTransaction.TabName, column);
                    }

                    moved = write.Commit();
                }

                _actions.Children.Add(Ui.Line("Renamed on " + Ui.Count(moved, "element")
                    + (moved == carriers ? "." : " of " + carriers + "."), 1, true));

                _session.Record(ActivityKind.Write,
                    "renamed " + column + " to " + renamed + " on " + Ui.Count(moved, "element"));
            }

            private void Remove(string column, int carriers)
            {
                var removed = 0;

                using (var write = new NavWriteTransaction(_session.Model, "CamelWorks: remove " + column))
                {
                    foreach (var entry in PropertyDiff.Resolve(_session.Model, _keys))
                    {
                        var item = entry.Value;
                        if (item == null) continue;
                        if (string.IsNullOrEmpty(item.Property(NavWriteTransaction.TabName, column))) continue;

                        write.RemoveProperty(entry.Key, NavWriteTransaction.TabName, column);
                    }

                    removed = write.Commit();
                }

                _actions.Children.Add(Ui.Done("Removed from " + Ui.Count(removed, "element")
                    + (removed == carriers ? "." : " of " + carriers + ".")));

                _actions.Children.Add(Ui.Note("What those elements held is in the project journal. "
                    + "Ctrl+Z does not reach a property write, so that record is the way back."));

                _session.Record(ActivityKind.Write,
                    "removed " + column + " from " + Ui.Count(removed, "element"));
            }
        }

        // -----------------------------------------------------------------------------------
        // Levels and zones
        // -----------------------------------------------------------------------------------

        /// <summary>Derive levels from whatever the model has, and optionally stamp them on.</summary>
        public static UIElement Zones()
        {
            var results = Ui.Stack();
            var after = Ui.Stack();

            var run = Ui.Runner("Derive levels", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var scale = session.MetresPerUnit;

                var named = new List<KeyValuePair<string, double>>();
                var elevations = new List<double>();
                var items = new List<(ElementKey Key, double Base, double Top)>();

                // The grid is read before the traverse, because it is one call and it decides
                // whether the rest of the chain matters.
                job.Say("Looking for a coordination grid...");

                var grids = new NavGrids(session.Model);
                var fromGrid = grids.Derive();

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(items.Count, "elements read")) return;
                    if (!item.HasGeometry) continue;

                    var bounds = item.Bounds;
                    items.Add((item.Key, bounds.MinZ * scale, bounds.MaxZ * scale));
                    elevations.Add(bounds.MinZ * scale);

                    var level = item.Property("Element", "Level") ?? item.Property("Item", "Level");

                    if (level != null && named.Count < 4000)
                        named.Add(new KeyValuePair<string, double>(level, bounds.MinZ * scale));
                }

                job.Say("Working out levels...");

                // Grid, then the models' own property, then the histogram. In that order because
                // that is the order of authority: a grid level is one authored fact with the name
                // that is on the drawing, a level property is one authored fact per model, and a
                // histogram is an honest guess.
                var fromModel = LevelSet.FromModel(named);
                var guessed = LevelSet.Derive(elevations);

                // Separates, not Bands.Count. A coordination grid reached a user reporting three
                // levels with every elevation at 0.000 - three bands, one height, and nothing can
                // be grouped by that. A source that cannot separate the model is worse than the
                // histogram, which at least knows it is guessing, so it does not win the chain
                // just for existing. The rejected source is still offered below, because the
                // coordinator may know something about it that this does not.
                var chosen = fromGrid.Separates ? fromGrid
                           : fromModel.Separates ? fromModel
                           : guessed.Separates ? guessed
                           : fromGrid.Bands.Count > 0 ? fromGrid
                           : fromModel.Bands.Count > 0 ? fromModel
                           : guessed;

                var units = session.Profile.Units;

                // Declared as a local function rather than assembled inline, because choosing a
                // different source has to redraw everything under it - including the two stamps,
                // which write whichever set is on screen.
                void Show(LevelSet levels)
                {
                    results.Children.Clear();
                    after.Children.Clear();

                    results.Children.Add(Ui.Line(levels.ToString(), 1, true));
                    results.Children.Add(Ui.Line(levels.Provenance(), 0.75));

                    if (levels.Source == LevelSource.Grid && grids.Name != null)
                        results.Children.Add(Ui.Small("Grid system: " + grids.Name));

                    if (levels.Bands.Count == 0)
                    {
                        results.Children.Add(Ui.Empty("No levels.",
                            "Nothing loaded carries a grid, a level property, or geometry to infer "
                            + "them from."));

                        return;
                    }

                    // Many more property levels than a building has storeys is the signature of
                    // disciplines spelling the same storey differently, and it is worth naming
                    // rather than leaving somebody to notice "L02" and "Level 02" sitting next to
                    // each other in a report.
                    if (!levels.Separates)
                        results.Children.Add(Ui.Problem(Ui.Count(levels.Bands.Count, "level")
                            + ", all at the same height (" + units.Length(levels.Bands[0].Elevation)
                            + "). Nothing can be grouped by these - every element falls in the "
                            + "first one. A grid usually reads this way when its levels were "
                            + "authored for their names and nobody set their heights. Pick another "
                            + "source below, or set the elevations in the authoring tool."));

                    if (levels.Source == LevelSource.Property && levels.Bands.Count > 25)
                        results.Children.Add(Ui.Note(Ui.Count(levels.Bands.Count, "distinct level")
                            + " is more than most buildings have storeys. That usually means two "
                            + "disciplines spell the same storey differently - \"L02\" and "
                            + "\"Level 02\" are two levels here. Worth a look at the table below "
                            + "before anything is reported by level."));

                    // The three sources as one control rather than two footnotes. They were
                    // Ui.Link, which reads as commentary — and a coordinator who disagrees with a
                    // derived answer wants the other answer, not a sentence describing it.
                    var sources = new List<string>();
                    var byName = new Dictionary<string, LevelSet>(StringComparer.Ordinal);

                    foreach (var source in new[] { fromGrid, fromModel, guessed })
                    {
                        if (source.Bands.Count == 0) continue;

                        var name = Named(source) + " (" + source.Bands.Count.ToString(CultureInfo.InvariantCulture) + ")";

                        if (byName.ContainsKey(name)) continue;

                        sources.Add(name);
                        byName[name] = source;
                    }

                    if (sources.Count > 1)
                        results.Children.Add(Ui.Field("Worked out from",
                            Ui.Choice(sources, Named(levels) + " ("
                                      + levels.Bands.Count.ToString(CultureInfo.InvariantCulture) + ")",
                                      pick =>
                                      {
                                          if (byName.TryGetValue(pick, out var swap) && swap.Source != levels.Source)
                                              Show(swap);
                                      }, 260)));

                    var rows = levels.Bands.Select(b => new TableRow(b,
                        b.Name,
                        units.Bare(b.Elevation),
                        b.Support > 0 ? Ui.Count(b.Support, "element") : string.Empty,
                        b.IsDerived ? "inferred" : levels.Source == LevelSource.Grid ? "from the grid"
                                                                                     : "from the model")).ToList();

                    var bands = Ui.Table(rows,
                        ("Level", 140), ("Elevation (" + units.LengthUnit + ")", 110),
                        ("Elements", 110), ("Source", 110));

                    // A level schedule with the elevations beside it is the thing a coordinator is
                    // asked for by everybody, and it was previously read off the screen.
                    results.Children.Add(Ui.Picking(bands,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(bands, picked), say, "level"))));

                    results.Children.Add(Ui.Tally(rows.Count, levels.Bands.Count, "level"));

                    after.Children.Add(Ui.Rule());
                    after.Children.Add(Stamp(session, levels, items));
                    after.Children.Add(GridStamp(session, grids));
                    after.Children.Add(BreakdownInline(session));
                    after.Children.Add(LevelHandoffs(results));
                }

                Show(chosen);
            });

            results.Children.Add(Bench.Ready(
                "Deriving reads the coordination grid, the models' own level property and the "
                + "geometry, and keeps the most trustworthy answer that actually separates the model.",
                "The stamps that write levels, grid squares and the breakdown open under the table."));

            return Bench.Surface(
                Ui.Stack(Bench.Scope("Everything loaded, wherever it has geometry.")),
                run,
                Ui.Stack(results, after));
        }

        /// <summary>
        /// Where a stamped level goes to work.
        ///
        /// The stamps above write Level and Grid into the CamelWorks tab, and the tools that read
        /// that tab by name live in other apps — one selection set per level in Sets Manager,
        /// quantities by level in Quantify. Without these the tab derives, stamps and stops, and
        /// the coordinator asked "how much ductwork per floor" is left to find the next tool by
        /// themselves.
        /// </summary>
        /// <param name="results">Where to say so if a window will not open.</param>
        private static UIElement LevelHandoffs(Panel results)
        {
            var sets = Ui.Button("Sets per level...", () =>
            {
                var problem = ToolViews.Open("setlib");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            var takeoff = Ui.Button("Takeoff by level...", () =>
            {
                var problem = ToolViews.Open("takeoff");
                if (problem != null) results.Children.Add(Ui.Problem(problem));
            });

            return Ui.Stack(
                Ui.Rule(),
                Heading("What the stamp unlocks"),
                Ui.Actions(sets, takeoff),
                Ui.Small("Both read what was just stamped: Generate Library makes one selection "
                         + "set per Level value, and the takeoff's Level axis prefers the "
                         + "CamelWorks tab over the models' own."));
        }

        /// <summary>Where a set of levels came from, as a sentence can use it.</summary>
        private static string Named(LevelSet levels) => levels.Source switch
        {
            LevelSource.Grid => "The coordination grid",
            LevelSource.Property => "The models' own level property",
            _ => "A histogram of element heights",
        };

        /// <summary>
        /// Write the grid reference onto every element.
        ///
        /// The half of the grid nobody gets at: Navisworks knows which grid square an element sits
        /// in and will tell you, one element at a time, in a tooltip. Stamped onto a property it
        /// becomes something clash grouping, set rules and reports can all use - and "Grid C-4" is
        /// how a site engineer finds the thing.
        /// </summary>
        private static UIElement GridStamp(Session session, NavGrids grids)
        {
            // Nothing at all rather than a card explaining its own absence. A federation with no
            // grid has nothing to write, and a box saying so is a permanent reminder of a feature
            // that does not apply to this job.
            if (grids.System == null) return Ui.Stack();

            // Held between the two presses. The plan pass reads the grid; the write pass reads this.
            var placements = new Dictionary<ElementKey, string>();

            var apply = Ui.Commit("Show me the grid squares it would stamp", "Stamp", "element",
                plan: (job, into) =>
                {
                    placements.Clear();

                    var scope = new List<KeyValuePair<ElementKey, IModelItem?>>();
                    var missed = 0;
                    var read = 0;

                    foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                    {
                        if (!job.Step(read++, "elements read")) return 0;
                        if (!item.HasGeometry) continue;

                        var reference = grids.Reference(item);

                        // Left out of scope rather than carried in with a null value. An element
                        // the grid cannot locate is not an element at grid nowhere, and a diff
                        // holding a null would read as a proposal to blank whatever it has.
                        if (reference == null) { missed++; continue; }

                        placements[item.Key] = reference;
                        scope.Add(new KeyValuePair<ElementKey, IModelItem?>(item.Key, item));
                    }

                    var diff = PropertyDiff.Of(NavWriteTransaction.TabName, "Grid", scope,
                                               item => placements[item.Key]);

                    into.Children.Add(Ui.Diff(diff));

                    if (missed > 0)
                        into.Children.Add(Ui.Note(Ui.Count(missed, "element")
                            + " would get no reference, because the grid cannot place them. They "
                            + "keep whatever Grid value they already had."));

                    return diff.Writes(ExistingValues.Overwrite);
                },
                apply: job =>
                {
                    using (var write = new NavWriteTransaction(session.Model, "CamelWorks: assign grid references"))
                    {
                        var done = 0;

                        foreach (var placed in placements)
                        {
                            if (!job.Step(done++, placements.Count, "elements stamped")) return;
                            write.SetProperty(placed.Key, NavWriteTransaction.TabName, "Grid", placed.Value);
                        }

                        job.Say("Writing...");
                        var written = write.Commit();

                        session.Record(ActivityKind.Write,
                            "stamped grid references onto " + Ui.Count(written, "element"));
                    }
                });

            return Ui.Stack(
                apply,
                Ui.Small("Writes Grid into the CamelWorks tab from the \"" + (grids.Name ?? "unnamed")
                         + "\" grid system, so clash grouping, set rules and reports can all say "
                         + "\"C-4\" instead of a coordinate."));
        }

        /// <summary>
        /// The selection tree's own shape, written down as properties.
        ///
        /// Levels and grids answer where an element is. This answers what it is part of - the
        /// system under the zone under the package that whoever authored the model already built
        /// into the tree. Everything downstream joins on that and the model does not carry it as a
        /// property, so nothing can filter, group, schedule or export against it.
        /// </summary>
        internal static UIElement BreakdownStamp(Session session)
        {
            var (fields, apply) = BreakdownParts(session);

            return Bench.Surface(
                Ui.Stack(Bench.Group("How the breakdown is written", fields)),
                null,
                Ui.Stack(apply));
        }

        /// <summary>
        /// The breakdown writer as a block on the Levels and Zones stage, where it sits beside the
        /// other two stamps rather than owning a surface of its own.
        /// </summary>
        private static UIElement BreakdownInline(Session session)
        {
            var (fields, apply) = BreakdownParts(session);

            return Ui.Stack(
                Ui.Rule(),
                Heading("Write the breakdown down"),
                Ui.Form(fields),
                apply);
        }

        /// <summary>
        /// The breakdown writer's working parts — the recipe fields and the two-press commit —
        /// built once and dressed by whichever of the two homes above is asking.
        /// </summary>
        private static (UIElement[] Fields, UIElement Apply) BreakdownParts(Session session)
        {
            var recipe = new BreakdownRecipe();

            var levels = Ui.Text("6", text =>
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    recipe.Levels = n;
            }, 70);

            var separator = Ui.Text(recipe.Separator, text => recipe.Separator = text, 70);
            var prefix = Ui.Text(recipe.Prefix, text => recipe.Prefix = text, 110);

            var root = Ui.Check("Keep the file name as the first level", false, v => recipe.SkipRoot = !v);

            BreakdownPlan? plan = null;

            var apply = Ui.Commit("Show me what it would write", "Write", "element",
                plan: (job, into) =>
                {
                    var paths = new List<KeyValuePair<ElementKey, IReadOnlyList<string>>>();
                    var read = 0;

                    foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                    {
                        if (!job.Step(read++, "elements read")) return 0;
                        if (!item.HasGeometry) continue;

                        paths.Add(new KeyValuePair<ElementKey, IReadOnlyList<string>>(item.Key, item.TreePath));
                    }

                    plan = Breakdown.Of(paths, recipe);

                    into.Children.Add(Ui.Line(plan.Explain(), 1, true));

                    if (plan.IsEmpty) return 0;

                    var sample = plan.Entries
                        .GroupBy(e => e.Composite, StringComparer.Ordinal)
                        .OrderByDescending(g => g.Count())
                        .Take(12)
                        .Select(g => new TableRow(g.Key, g.Key, Ui.Count(g.Count(), "element")))
                        .ToList();

                    var breakdown = Ui.Table(sample, ("Breakdown", 0), ("Holds", 120));

                    into.Children.Add(Ui.Picking(breakdown,
                        new Ui.OnPicked("Copy {n}",
                            (picked, say) => Ui.Copy(Ui.Lines(breakdown, picked), say, "branch"))));

                    if (plan.Deepest < recipe.Levels)
                        into.Children.Add(Ui.Note("The tree only reaches "
                            + plan.Deepest.ToString(CultureInfo.InvariantCulture)
                            + " levels here, so the deeper ones are left unwritten rather than blank."));

                    return plan.Entries.Count;
                },
                apply: job =>
                {
                    var writing = plan;
                    if (writing == null) return;

                    using (var write = new NavWriteTransaction(session.Model, "CamelWorks: write the breakdown"))
                    {
                        var done = 0;

                        foreach (var entry in writing.Entries)
                        {
                            if (!job.Step(done++, writing.Entries.Count, "elements written")) return;

                            for (var level = 0; level < entry.Levels.Count; level++)
                                write.SetProperty(entry.Key, NavWriteTransaction.TabName,
                                                  Breakdown.LevelName(recipe, level + 1), entry.Levels[level]);

                            write.SetProperty(entry.Key, NavWriteTransaction.TabName,
                                              recipe.CompositeName, entry.Composite);
                        }

                        job.Say("Writing...");
                        var written = write.Commit();

                        session.Record(ActivityKind.Write,
                            "wrote the breakdown onto " + Ui.Count(written, "element"));
                    }
                });

            var fields = new UIElement[]
            {
                Ui.Pair(Ui.Field("Property prefix", prefix, "levels come out as \"" + recipe.Prefix + " 1\", \"" + recipe.Prefix + " 2\""),
                        Ui.Field("How many levels", levels)),
                Ui.Field("Joined by", separator, "what separates the levels in the one key a spreadsheet joins on"),
                root,
            };

            return (fields, apply);
        }

        private static UIElement Stamp(Session session, LevelSet levels,
                                       IReadOnlyList<(ElementKey Key, double Base, double Top)> items)
        {
            var placements = new Dictionary<ElementKey, string>();

            var apply = Ui.Commit("Show me the levels it would stamp", "Stamp", "element",
                plan: (job, into) =>
                {
                    placements.Clear();

                    var outside = 0;
                    var read = 0;

                    foreach (var item in items)
                    {
                        if (!job.Step(read++, items.Count, "elements placed")) return 0;

                        var level = levels.LevelOf(item.Base, item.Top);

                        // Same rule as the grid: an element in none of the bands is left out of
                        // scope, not carried in holding a null that would read as "empty this".
                        if (level == null) { outside++; continue; }

                        placements[item.Key] = level;
                    }

                    var scope = PropertyDiff.Resolve(session.Model, placements.Keys).ToList();

                    var diff = PropertyDiff.Of(NavWriteTransaction.TabName, "Level", scope,
                                               item => placements[item.Key]);

                    into.Children.Add(Ui.Diff(diff));

                    if (outside > 0)
                        into.Children.Add(Ui.Note(Ui.Count(outside, "element")
                            + " sits outside every derived band and keeps whatever Level it had."));

                    return diff.Writes(ExistingValues.Overwrite);
                },
                apply: job =>
                {
                    using (var write = new NavWriteTransaction(session.Model, "CamelWorks: assign levels"))
                    {
                        var done = 0;

                        foreach (var placed in placements)
                        {
                            if (!job.Step(done++, placements.Count, "elements stamped")) return;
                            write.SetProperty(placed.Key, NavWriteTransaction.TabName, "Level", placed.Value);
                        }

                        job.Say("Writing...");
                        var written = write.Commit();

                        session.Record(ActivityKind.Write, "stamped levels onto " + Ui.Count(written, "element"));
                    }
                });

            return Ui.Stack(
                apply,
                Ui.Small("Writes Level into the CamelWorks tab, so clash grouping, reports and set "
                         + "rules can all use it — including on models that never had a level "
                         + "property to begin with. A value already there is replaced: the level is "
                         + "derived from where the element sits, so the model's own answer is the "
                         + "one being corrected."));
        }

        // -----------------------------------------------------------------------------------
        // Takeoff
        // -----------------------------------------------------------------------------------

        /// <summary>Sum the numeric properties the model carries, grouped, reporting what it could not read.</summary>
        public static UIElement Takeoff() => new TakeoffScreen().Build();

        /// <summary>
        /// How much of something there is, and where it is.
        ///
        /// One axis answers the first question. Two answer the second, which is what a programme, a
        /// procurement schedule or a phasing plan is actually built from — and producing it by hand
        /// means running the takeoff once per level and typing the results into a spreadsheet.
        ///
        /// Nothing has to be mapped first. The measure comes from whatever the elements already
        /// carry, and what cannot be read is counted and named rather than quietly left out of the
        /// total.
        /// </summary>
        private sealed class TakeoffScreen
        {
            /// <summary>The column axis that means "do not pivot".</summary>
            private const string NoColumns = "Nothing - just the one axis";

            /// <summary>The assumed-unit choice that means "leave bare numbers bare".</summary>
            private const string NoUnit = "Leave it as it is";

            private static readonly string[] Axes =
            {
                "Category", "Type", "Model", "Level", "Discipline", "System",
            };

            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly TextBox _path = Ui.Text(string.Empty, null, 340);
            private readonly TextBox _clean = Ui.Text(string.Empty, null, 220);

            private string _assume = NoUnit;
            private ValueCleaner _cleaner = ValueCleaner.None;
            private Measure _units = new Measure();

            private string _scope = Selection;
            private string _measure = "Volume";
            private string _rows = "Category";
            private string _columns = NoColumns;

            private TakeoffPivotTable? _table;
            private TakeoffResult? _flat;

            internal UIElement Build()
            {
                var columns = new List<string> { NoColumns };
                columns.AddRange(Axes);

                var scope = Ui.Choice(new[] { Selection, WholeModel }, _scope, p => _scope = p);
                var measure = Ui.Choice(new[] { "Volume", "Area", "Length", "Count" }, _measure, p => _measure = p);
                var rows = Ui.Choice(Axes, _rows, p => _rows = p);
                var down = Ui.Choice(columns, _columns, p => _columns = p);

                var assumable = new List<string> { NoUnit };
                assumable.AddRange(ValueCleaner.AssumableUnits);

                var assume = Ui.Choice(assumable, _assume, p => _assume = p);

                var run = Ui.Runner("Sum it", _results, Run);

                _results.Children.Add(Bench.Ready(
                    "Summing reads the measure off every element in scope and groups the totals "
                    + "the way the axes say.",
                    "What cannot be read as a quantity is counted and named, never dropped."));

                var setup = Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Field("Scope", scope),
                        Ui.Field("Measure", measure)),
                    Bench.Group("Grouped how",
                        Ui.Field("Rows", rows),
                        Ui.Field("Columns", down, "Leave it to get a flat list.")));

                // Both settings are off until somebody sets them, and a property that already
                // reads as a quantity needs neither - so they are one symbol away rather than
                // three fields between the scope and the button. The window states itself when
                // it is opened, which is the point at which anybody cares what it does.
                var foot = new WrapPanel();

                foot.Children.Add(run);
                foot.Children.Add(new Border { Width = 14 });

                foot.Children.Add(Ui.Icon(Glyphs.Tune, "If the values need tidying first", () =>
                    Ui.Configure("Reading the values", null,
                        new Ui.Page("If the values need tidying first", () => Ui.Stack(
                            Ui.Field("Take the number matching", _clean),
                            Ui.Across(
                                Ui.Quiet("Just the first number", () => _clean.Text = ValueCleaner.FirstNumber),
                                Ui.Quiet("Clear it", () => _clean.Text = string.Empty)),
                            Ui.Field("When it has no unit, read it as", assume),
                            Ui.Note("Nothing here invents a number. A pattern that matches nothing leaves the "
                                    + "value alone, so the takeoff still counts it unreadable and names it - "
                                    + "turning what it could not clean into a zero would move the failure out "
                                    + "of a line that admits it and into a total that is quietly wrong."))))));

                return Bench.Surface(setup, foot, Ui.Stack(_results, _output));
            }

            private void Run(Job job)
            {
                _table = null;
                _flat = null;
                _output.Children.Clear();

                var session = Host.Current;
                if (session == null) { _results.Children.Add(Ui.Line(Host.NoModel)); return; }

                _units = session.Profile.Units;

                _cleaner = ValueCleaner.Of(_clean.Text,
                                           _assume == NoUnit ? null : _assume,
                                           out var badPattern);

                // Said and then carried on with what survived, rather than refusing to run. A bad
                // pattern with a good assumed unit is still half a setting, and a takeoff that
                // silently ignored the box would be worse than one that says the box was wrong.
                if (badPattern != null) _results.Children.Add(Ui.Problem(badPattern));

                var traversal = _scope == Selection
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.WholeDocument;

                var flat = new List<TakeoffLine>();
                var pivot = new List<PivotLine>();
                var wide = _columns != NoColumns;

                foreach (var item in session.Model.Traverse(traversal))
                {
                    if (!job.Step(flat.Count + pivot.Count, "elements read")) return;

                    var value = _measure == "Count" ? "1" : _cleaner.Clean(MeasureOf(item, _measure));
                    var row = AxisOf(session, item, _rows);

                    if (wide) pivot.Add(new PivotLine(item.DisplayName, row, AxisOf(session, item, _columns), value));
                    else flat.Add(new TakeoffLine(item.DisplayName, row, value));
                }

                if (flat.Count + pivot.Count == 0)
                {
                    _results.Children.Add(Ui.Empty(
                        _scope == Selection ? "Nothing is selected." : "Nothing is loaded.",
                        _scope == Selection
                            ? "Select elements in the model, or switch the scope to everything loaded."
                            : "Append a model first."));
                    return;
                }

                job.Say("Adding up...");

                if (wide) Wide(session, pivot);
                else Narrow(session, flat);

                _output.Children.Add(Ui.PathField("Write to", _path, PathKind.Save,
                                                  "Excel|*.xlsx|CSV|*.csv|All files|*.*"));
                _output.Children.Add(Ui.Across(Ui.Button("Excel...", () => Export(session))));
            }

            /// <summary>The single-axis answer.</summary>
            private void Narrow(Session session, IReadOnlyList<TakeoffLine> lines)
            {
                var result = Core.Data.Takeoff.Sum(lines);
                _flat = result;

                _results.Children.Add(Ui.Line(result.Describe(_units), 1, true));

                if (result.Groups.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing to measure.",
                        "Nothing in scope carries that property."));
                    return;
                }

                var rows = result.Groups.OrderByDescending(g => g.Count).Select(g => new TableRow(g,
                    g.Name,
                    g.Count.ToString("N0", CultureInfo.InvariantCulture),
                    g.Total?.Show(_units) ?? (g.MixedKinds ? "(mixed units)" : "-"),
                    g.Unreadable > 0
                        ? g.Unreadable.ToString(CultureInfo.InvariantCulture) + " unreadable"
                        : string.Empty)
                {
                    Tone = g.IsComplete ? Tone.Plain : Tone.Warn,
                }).ToList();

                // A takeoff is read once and pasted somewhere else. Picking the lines that belong
                // in this bill and taking those beats copying the screen and deleting half of it.
                var totals = Ui.Table(rows,
                    ("Group", 190), ("Count", 80), ("Total (" + UnitWord() + ")", 130), ("", 0));

                _results.Children.Add(Ui.Picking(totals,
                    new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(totals, picked), say),
                                    primary: true)));

                Provenance();
                Caveat(result.IsComplete);

                if (_rows == "Level")
                    LevelGap(result.Groups
                        .FirstOrDefault(g => g.Name == PivotLine.None)?.Count ?? 0);

                session.Record(ActivityKind.Report, "took off " + _measure.ToLowerInvariant()
                                                    + " by " + _rows.ToLowerInvariant() + ": " + result);
            }

            /// <summary>The two-axis answer.</summary>
            private void Wide(Session session, IReadOnlyList<PivotLine> lines)
            {
                var table = TakeoffPivot.Of(lines);
                _table = table;

                _results.Children.Add(Ui.Line(table.Explain(_units), 1, true));

                if (table.IsEmpty)
                {
                    _results.Children.Add(Ui.Empty("Nothing to measure.",
                        "Nothing in scope carries that property."));
                    return;
                }

                var headers = new List<(string Header, double Width)> { (_rows, 170) };

                foreach (var column in table.Columns) headers.Add((column, 110));
                headers.Add(("Total (" + UnitWord() + ")", 110));

                var rows = new List<TableRow>();

                foreach (var row in table.Rows)
                {
                    var cells = new List<string?> { row };

                    foreach (var column in table.Columns) cells.Add(Read(table.Cell(row, column)));

                    cells.Add(Read(table.RowTotal(row)));

                    rows.Add(new TableRow(row, cells.ToArray())
                    {
                        Tone = table.RowTotal(row)?.IsComplete == false ? Tone.Warn : Tone.Plain,
                    });
                }

                // The totals row last and labelled, so a reader scrolling to the bottom of a long
                // table knows the last line is not another category.
                var totals = new List<string?> { "All" };

                foreach (var column in table.Columns) totals.Add(Read(table.ColumnTotal(column)));

                totals.Add(Read(table.Grand));
                rows.Add(new TableRow(null, totals.ToArray()) { Tone = Tone.Good });

                var grid = Ui.Table(rows, headers.ToArray());

                // The same argument as the single-axis totals above it: a cross-tab is read once
                // and pasted somewhere else, and the totals row goes with whatever is picked.
                _results.Children.Add(Ui.Picking(grid,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(grid, picked), say, "line"))));

                Provenance();
                Caveat(table.Grand.IsComplete);

                if (_rows == "Level")
                    LevelGap(table.RowTotal(PivotLine.None)?.Count ?? 0);
                else if (_columns == "Level")
                    LevelGap(table.ColumnTotal(PivotLine.None)?.Count ?? 0);

                if (table.DroppedRows > 0 || table.DroppedColumns > 0)
                    _results.Children.Add(Ui.Note("The largest rows and columns are shown. What is not on "
                        + "screen is still in every total on it, so the numbers add up whether or not "
                        + "the thing they came from has a row of its own."));

                session.Record(ActivityKind.Report, "took off " + _measure.ToLowerInvariant()
                                                    + " by " + _rows.ToLowerInvariant() + " and "
                                                    + _columns.ToLowerInvariant() + ": " + table);
            }

            /// <summary>
            /// The quantities that landed in "(none)" because elements carry no Level.
            ///
            /// Said with the way out rather than left as a row to puzzle over: the Level this
            /// takeoff reads first is the one Levels &amp; Zones stamps from the geometry itself,
            /// so the gap is one tab away from closed — and a bill split by level with a fat
            /// unplaced row is exactly the sheet nobody will sign.
            /// </summary>
            /// <param name="missing">How many elements had no Level to be grouped by.</param>
            private void LevelGap(int missing)
            {
                if (missing == 0) return;

                _results.Children.Add(Ui.Info(Ui.Count(missing, "element") + " in scope carries "
                    + "no Level, so its quantities sit in the \"" + PivotLine.None + "\" line. "
                    + "Levels & Zones works the levels out from the geometry and stamps them on, "
                    + "and this takeoff reads that stamp first.",
                    "Open Levels & Zones", () => Ui.Told(ToolViews.Open("levels"))));
            }

            /// <summary>Which unit the column is in, for the heading rather than every cell.</summary>
            private string UnitWord() => _measure switch
            {
                "Volume" => _units.VolumeUnit,
                "Area" => _units.AreaUnit,
                "Length" => _units.LengthUnit,
                _ => "count",
            };

            /// <summary>
            /// What this run did to the numbers before it added them.
            ///
            /// Shown every time and not only when a cleaner is set. A takeoff whose provenance is
            /// invisible is one somebody has to re-derive by hand before they will put it in a cost
            /// plan, and the whole point of the tool is that they should not have to.
            /// </summary>
            private void Provenance()
            {
                var said = "Totals in " + _units.Describe() + ". ";

                said += _cleaner.DoesNothing
                    ? "Values read exactly as the model wrote them."
                    : "Before adding: " + _cleaner.Describe() + ".";

                _results.Children.Add(Ui.Line(said, 0.7));
            }

            private void Caveat(bool complete)
            {
                if (complete) return;

                _results.Children.Add(Ui.Note("Some values could not be read as quantities, or a cell held "
                    + "more than one kind of unit. They are counted and named rather than dropped, because "
                    + "a total that quietly excludes what it could not parse is worse than no total."));
            }

            /// <summary>How one cell reads: the total, or why there is not one.</summary>
            private string Read(TakeoffGroup? group)
            {
                if (group == null || group.Count == 0) return string.Empty;
                if (group.MixedKinds) return "(mixed units)";

                return group.Total?.Show(_units)
                       ?? group.Count.ToString("N0", CultureInfo.InvariantCulture) + " elements";
            }

            /// <summary>Write the same table the screen is showing, so the two cannot disagree.</summary>
            private void Export(Session session)
            {
                _output.Children.Clear();
                _output.Children.Add(Ui.PathField("Write to", _path, PathKind.Save,
                                                  "Excel|*.xlsx|CSV|*.csv|All files|*.*"));

                var file = _path.Text.Trim();

                if (file.Length == 0)
                {
                    // Beside the project file first, because that is the folder the rest of the
                    // team can see; a takeoff in one person's Documents is a takeoff re-run by
                    // the next person who needs it.
                    var project = session.Store.Path;

                    var folder = string.IsNullOrWhiteSpace(project)
                        ? null
                        : System.IO.Path.GetDirectoryName(project);

                    if (string.IsNullOrEmpty(folder))
                        folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    file = System.IO.Path.Combine(folder!,
                        session.Profile.ProjectName + " takeoff.xlsx");

                    _path.Text = file;
                }

                var block = _table != null ? Sheet(_table) : _flat != null ? Sheet(_flat) : null;

                if (block == null)
                {
                    _output.Children.Add(Ui.Line("Sum it first.", 0.8));
                    return;
                }

                try
                {
                    File.WriteAllBytes(file, XlsxWriter.Write("Takeoff", block));
                }
                catch (Exception e)
                {
                    _output.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _output.Children.Add(Ui.Line("Wrote " + file, 1, true));
                session.Record(ActivityKind.Report, "wrote a takeoff spreadsheet", file);
            }

            private TableBlock Sheet(TakeoffPivotTable table)
            {
                var headers = new List<string> { _rows };
                headers.AddRange(table.Columns);
                headers.Add("Total (" + UnitWord() + ")");

                var block = new TableBlock(headers.ToArray());

                foreach (var row in table.Rows)
                {
                    var cells = new List<string> { row };

                    foreach (var column in table.Columns) cells.Add(Read(table.Cell(row, column)));

                    cells.Add(Read(table.RowTotal(row)));
                    block.Row(cells.ToArray());
                }

                var totals = new List<string> { "All" };

                foreach (var column in table.Columns) totals.Add(Read(table.ColumnTotal(column)));

                totals.Add(Read(table.Grand));
                block.Row(totals.ToArray());

                return block;
            }

            private TableBlock Sheet(TakeoffResult result)
            {
                var block = new TableBlock(_rows, "Count", "Total (" + UnitWord() + ")", "Unreadable");

                foreach (var group in result.Groups.OrderByDescending(g => g.Count))
                    block.Row(group.Name,
                        group.Count.ToString("N0", CultureInfo.InvariantCulture),
                        Read(group),
                        group.Unreadable.ToString(CultureInfo.InvariantCulture));

                return block;
            }

            /// <summary>
            /// Which bucket an element falls into on one axis.
            ///
            /// Discipline and system come from what the project already worked out and from what the
            /// element carries, in that order — there is no table to fill in first, which is the
            /// difference between a takeoff somebody runs and one they mean to set up properly.
            /// </summary>
            private static string AxisOf(Session session, IModelItem item, string axis) => axis switch
            {
                "Type" => item.TypeName ?? PivotLine.None,
                "Model" => item.Model.DisplayName,
                "Discipline" => session.Profile.DisciplineOf(item.Model.Scope) ?? PivotLine.None,
                "System" => item.Property("Element", "System Name")
                            ?? item.Property("Element", "System")
                            ?? item.Property("Item", "System")
                            ?? PivotLine.None,
                "Level" => item.Property(NavWriteTransaction.TabName, "Level")
                           ?? item.Property("Element", "Level")
                           ?? item.Property("Item", "Level")
                           ?? PivotLine.None,
                _ => item.Category ?? PivotLine.None,
            };
        }

        /// <summary>
        /// The value of a measured property, whichever tab carries it.
        ///
        /// Named MeasureOf and not Measure or Quantity: both of those are Core type names this
        /// file imports, and a method that shadows one makes the type unusable in the whole class -
        /// which is a compile error a long way from the line that caused it.
        /// </summary>
        private static string? MeasureOf(IModelItem item, string measure) =>
            item.Property("Element", measure)
            ?? item.Property("Item", measure)
            ?? item.Property("Revit Material Takeoff", measure)
            ?? item.Property(NavWriteTransaction.TabName, measure);
    }
}
