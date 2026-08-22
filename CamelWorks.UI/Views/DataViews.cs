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
        /// Browse and edit properties in one place.
        ///
        /// The scope defaults to the current selection rather than the whole federation, because
        /// the whole federation is the answer nobody wants first and the one that takes longest to
        /// produce.
        /// </summary>
        public static UIElement Manager()
        {
            var scope = Selection;
            var results = Ui.Stack();
            var editor = Ui.Stack();

            var picker = Ui.Choice(new[] { Selection, WholeModel }, scope, pick => scope = pick);

            var read = Ui.Runner("Read properties", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var keys = new List<ElementKey>();
                var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                var blanks = new Dictionary<string, int>(StringComparer.Ordinal);

                var traversal = scope == Selection ? TraversalScope.CurrentSelection : TraversalScope.WholeDocument;

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
                        scope == Selection ? "Nothing is selected." : "Nothing is loaded.",
                        scope == Selection
                            ? "Select elements in the model, or switch the scope to everything loaded."
                            : "Append a model first."));
                    return;
                }

                var rows = values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p =>
                    new TableRow(p.Key,
                        p.Key,
                        p.Value.Count == 1 ? p.Value.First() : "(" + p.Value.Count + (p.Value.Count > 5 ? "+" : string.Empty) + " values)",
                        blanks.TryGetValue(p.Key, out var blank) && blank > 0
                            ? blank.ToString(CultureInfo.InvariantCulture) + " blank"
                            : string.Empty)).ToList();

                results.Children.Add(Ui.Line(Ui.Count(keys.Count, "element") + ", "
                    + Ui.Count(rows.Count, "property"), 1, true));

                results.Children.Add(Ui.Table(rows, ("Property", 260), ("Value", 200), ("", 0)));

                editor.Children.Clear();
                editor.Children.Add(Writer(session, keys));
                editor.Children.Add(new ColumnScreen(session, keys).Build());
            });

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("Data"),
                Ui.Sub("Every property the elements carry, folded to one row each, with what varies said "
                       + "plainly rather than shown as the first element's value."),
                Ui.Field("Scope", picker),
                read,
                results,
                editor));
        }

        /// <summary>
        /// The write half, built only once something has been read — because the set of elements it
        /// writes to is exactly the set that was read, and offering to write before then would mean
        /// guessing at what.
        /// </summary>
        private static UIElement Writer(Session session, IReadOnlyList<ElementKey> keys) =>
            new WriteScreen(session, keys).Build();

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
            /// <summary>What the picker says for each policy.</summary>
            private const string Keep = "leave it alone";

            private const string Replace = "replace it";

            private readonly Session _session;
            private readonly IReadOnlyList<ElementKey> _keys;

            /// <summary>What the value picker says for each of the two ways to make one.</summary>
            private const string Fixed = "the same for every element";

            private const string Worked = "worked out from other properties";

            private readonly TextBox _name = Ui.Text("Zone", null, 150);
            private readonly TextBox _value = Ui.Text(string.Empty, null, 200);
            private readonly TextBox _formula = Ui.Text(string.Empty, null, 340);
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _applyRow = Ui.Stack();

            private ExistingValues _existing = ExistingValues.Keep;
            private string _how = Fixed;
            private WriteDiff? _diff;
            private Formula? _parsed;

            internal WriteScreen(Session session, IReadOnlyList<ElementKey> keys)
            {
                _session = session;
                _keys = keys;
            }

            internal UIElement Build()
            {
                var policy = Ui.Choice(new[] { Keep, Replace }, Keep, pick =>
                {
                    _existing = pick == Replace ? ExistingValues.Overwrite : ExistingValues.Keep;
                    if (_diff != null) ShowApply();
                });

                var how = Ui.Choice(new[] { Fixed, Worked }, _how, pick => _how = pick);

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

                        _status.Children.Add(Ui.Table(rows,
                            ("Element", 200), ("Now", 130), ("Would become", 130), ("", 0)));

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

                return Ui.Card("Write a property",
                    "Goes into a tab of its own called \"" + NavWriteTransaction.TabName
                    + "\", so it is never mixed with the authoring tool's data.",
                    Ui.Field("Name", _name),
                    Ui.Field("The value is", how),
                    Ui.Field("Value", _value),
                    Ui.Field("Formula", _formula),
                    Ui.Note("A formula reads other properties: {Element/Mark}. Join text with & and do "
                            + "arithmetic with + - * / — two different operators on purpose, because one "
                            + "that did both would turn a door reference of \"12\" and a level of \"3\" "
                            + "into 15 instead of \"12-3\". An element missing something the formula "
                            + "needs is left alone rather than written a zero."),
                    Ui.Field("If a value is already there", policy),
                    preview,
                    Ui.Note("Ctrl+Z will not undo this. Navisworks does not put property writes on its "
                            + "undo stack, so CamelWorks records what it changed in the project file "
                            + "instead of pretending otherwise."),
                    _status,
                    _applyRow);
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
                if (diff == null) return;

                var writes = diff.Writes(_existing);

                if (writes == 0)
                {
                    _applyRow.Children.Add(Ui.Note(
                        "Nothing would be written: every element in scope already holds a value, and "
                        + "existing values are being left alone. Switch to \"" + Replace
                        + "\" if replacing them is what you meant."));
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
            private static Func<IModelItem, string?> ValueOf(Formula formula) => item =>
                formula.Evaluate((category, name) => category.Length == 0
                    ? AnyTab(item, name)
                    : item.Property(category, name)).Value;

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

            private readonly TextBox _column = Ui.Text(string.Empty, null, 150);
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
                        Ui.Button("Remove it from " + Ui.Count(census.Present, "element"),
                                  () => Remove(column, census.Present))));
                });

                return Ui.Card("Rename or remove a column",
                    "Only columns in CamelWorks' own tab. Renaming what the authoring tool wrote would "
                    + "make the model disagree with the model it came from.",
                    Ui.Field("Column", _column),
                    Ui.Field("New name", _renamed),
                    count,
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

                _actions.Children.Add(Ui.Line("Removed from " + Ui.Count(removed, "element")
                    + (removed == carriers ? "." : " of " + carriers + "."), 1, true));

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

                var fromModel = LevelSet.FromModel(named);
                var levels = fromModel.Bands.Count > 0 ? fromModel : LevelSet.Derive(elevations);

                results.Children.Add(Ui.Line(levels.ToString(), 1, true));

                results.Children.Add(Ui.Line(levels.IsFromModel
                    ? "Read from the models' own Level property."
                    : "The models carry no usable level property, so these were inferred from a histogram "
                      + "of element base heights.", 0.75));

                if (levels.Bands.Count == 0)
                {
                    results.Children.Add(Ui.Empty("No levels.",
                        "Nothing loaded carries geometry to infer them from."));
                    return;
                }

                var units = session.Profile.Units;

                var rows = levels.Bands.Select(b => new TableRow(b,
                    b.Name,
                    units.Bare(b.Elevation),
                    b.Support > 0 ? Ui.Count(b.Support, "element") : string.Empty,
                    b.IsDerived ? "inferred" : "from the model")).ToList();

                results.Children.Add(Ui.Table(rows,
                    ("Level", 140), ("Elevation (" + units.LengthUnit + ")", 110),
                    ("Elements", 110), ("Source", 110)));

                after.Children.Clear();
                after.Children.Add(Stamp(session, levels, items));
            });

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("Levels & zones"),
                Ui.Sub("Uses the models' own levels where they have them and a height histogram where they "
                       + "do not — and says which, because a level called \"Level +3.600\" is honest about "
                       + "being a guess in a way that \"Level 2\" is not."),
                run,
                results,
                after));
        }

        private static UIElement Stamp(Session session, LevelSet levels,
                                       IReadOnlyList<(ElementKey Key, double Base, double Top)> items)
        {
            var status = Ui.Stack();

            var apply = Ui.Runner("Stamp the level onto every element", status, job =>
            {
                using (var write = new NavWriteTransaction(session.Model, "CamelWorks: assign levels"))
                {
                    var placed = 0;

                    foreach (var item in items)
                    {
                        if (!job.Step(placed, "elements placed")) return;

                        var level = levels.LevelOf(item.Base, item.Top);
                        if (level == null) continue;

                        write.SetProperty(item.Key, NavWriteTransaction.TabName, "Level", level);
                        placed++;
                    }

                    if (placed == 0)
                    {
                        status.Children.Add(Ui.Empty("Nothing to stamp.",
                            "No element sits inside any of the derived bands."));
                        return;
                    }

                    job.Say("Writing...");
                    var written = write.Commit();

                    status.Children.Add(Ui.Line("Stamped " + Ui.Count(written, "element") + ".", 1, true));
                    session.Record(ActivityKind.Write, "stamped levels onto " + Ui.Count(written, "element"));
                }
            });

            return Ui.Card("Put the level on the elements",
                "Writes Level into the CamelWorks tab, so clash grouping, reports and set rules can all "
                + "use it — including on models that never had a level property to begin with.",
                apply,
                status);
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

            private static readonly string[] Axes =
            {
                "Category", "Type", "Model", "Level", "Discipline", "System",
            };

            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly TextBox _path = Ui.Text(string.Empty, null, 340);

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

                var run = Ui.Runner("Sum it", _results, Run);

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Takeoff"),
                    Ui.Sub("Sums whatever the elements carry, with no mapping table to build first. Pick a "
                           + "second axis and it becomes a pivot: concrete by level, ductwork by system."),
                    Ui.Field("Scope", scope),
                    Ui.Field("Measure", measure),
                    Ui.Field("Rows", rows),
                    Ui.Field("Columns", down),
                    run,
                    _results,
                    _output));
            }

            private void Run(Job job)
            {
                _table = null;
                _flat = null;
                _output.Children.Clear();

                var session = Host.Current;
                if (session == null) { _results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var traversal = _scope == Selection
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.WholeDocument;

                var flat = new List<TakeoffLine>();
                var pivot = new List<PivotLine>();
                var wide = _columns != NoColumns;

                foreach (var item in session.Model.Traverse(traversal))
                {
                    if (!job.Step(flat.Count + pivot.Count, "elements read")) return;

                    var value = _measure == "Count" ? "1" : Measure(item, _measure);
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

                _output.Children.Add(Ui.Field("Write to", _path));
                _output.Children.Add(Ui.Across(Ui.Button("Write it to a spreadsheet", () => Export(session))));
            }

            /// <summary>The single-axis answer.</summary>
            private void Narrow(Session session, IReadOnlyList<TakeoffLine> lines)
            {
                var result = Core.Data.Takeoff.Sum(lines);
                _flat = result;

                _results.Children.Add(Ui.Line(result.ToString(), 1, true));

                if (result.Groups.Count == 0)
                {
                    _results.Children.Add(Ui.Empty("Nothing to measure.",
                        "Nothing in scope carries that property."));
                    return;
                }

                var rows = result.Groups.OrderByDescending(g => g.Count).Select(g => new TableRow(g,
                    g.Name,
                    g.Count.ToString("N0", CultureInfo.InvariantCulture),
                    g.Total?.ToString() ?? (g.MixedKinds ? "(mixed units)" : "-"),
                    g.Unreadable > 0
                        ? g.Unreadable.ToString(CultureInfo.InvariantCulture) + " unreadable"
                        : string.Empty)
                {
                    Tone = g.IsComplete ? Tone.Plain : Tone.Warn,
                }).ToList();

                _results.Children.Add(Ui.Table(rows,
                    ("Group", 190), ("Count", 80), ("Total", 130), ("", 0)));

                Caveat(result.IsComplete);

                session.Record(ActivityKind.Report, "took off " + _measure.ToLowerInvariant()
                                                    + " by " + _rows.ToLowerInvariant() + ": " + result);
            }

            /// <summary>The two-axis answer.</summary>
            private void Wide(Session session, IReadOnlyList<PivotLine> lines)
            {
                var table = TakeoffPivot.Of(lines);
                _table = table;

                _results.Children.Add(Ui.Line(table.Explain(), 1, true));

                if (table.IsEmpty)
                {
                    _results.Children.Add(Ui.Empty("Nothing to measure.",
                        "Nothing in scope carries that property."));
                    return;
                }

                var headers = new List<(string Header, double Width)> { (_rows, 170) };

                foreach (var column in table.Columns) headers.Add((column, 110));
                headers.Add(("Total", 110));

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

                _results.Children.Add(Ui.Table(rows, headers.ToArray()));

                Caveat(table.Grand.IsComplete);

                if (table.DroppedRows > 0 || table.DroppedColumns > 0)
                    _results.Children.Add(Ui.Note("The largest rows and columns are shown. What is not on "
                        + "screen is still in every total on it, so the numbers add up whether or not "
                        + "the thing they came from has a row of its own."));

                session.Record(ActivityKind.Report, "took off " + _measure.ToLowerInvariant()
                                                    + " by " + _rows.ToLowerInvariant() + " and "
                                                    + _columns.ToLowerInvariant() + ": " + table);
            }

            private void Caveat(bool complete)
            {
                if (complete) return;

                _results.Children.Add(Ui.Note("Some values could not be read as quantities, or a cell held "
                    + "more than one kind of unit. They are counted and named rather than dropped, because "
                    + "a total that quietly excludes what it could not parse is worse than no total."));
            }

            /// <summary>How one cell reads: the total, or why there is not one.</summary>
            private static string Read(TakeoffGroup? group)
            {
                if (group == null || group.Count == 0) return string.Empty;
                if (group.MixedKinds) return "(mixed units)";

                return group.Total?.ToString()
                       ?? group.Count.ToString("N0", CultureInfo.InvariantCulture) + " elements";
            }

            /// <summary>Write the same table the screen is showing, so the two cannot disagree.</summary>
            private void Export(Session session)
            {
                _output.Children.Clear();
                _output.Children.Add(Ui.Field("Write to", _path));

                var file = _path.Text.Trim();

                if (file.Length == 0)
                {
                    file = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
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
                headers.Add("Total");

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
                var block = new TableBlock(_rows, "Count", "Total", "Unreadable");

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

        private static string? Measure(IModelItem item, string measure) =>
            item.Property("Element", measure)
            ?? item.Property("Item", measure)
            ?? item.Property("Revit Material Takeoff", measure)
            ?? item.Property(NavWriteTransaction.TabName, measure);
    }
}
