using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
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

            private readonly TextBox _name = Ui.Text("Zone", null, 150);
            private readonly TextBox _value = Ui.Text(string.Empty, null, 200);
            private readonly StackPanel _status = Ui.Stack();
            private readonly StackPanel _applyRow = Ui.Stack();

            private ExistingValues _existing = ExistingValues.Keep;
            private WriteDiff? _diff;

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

                var preview = Ui.Runner("Show me what would change", _status, job =>
                {
                    _diff = null;
                    _applyRow.Children.Clear();

                    var name = _name.Text.Trim();

                    if (name.Length == 0)
                    {
                        _status.Children.Add(Ui.Problem("Give the property a name first."));
                        return;
                    }

                    job.Say("Reading what is there now...");

                    var diff = PropertyDiff.Of(NavWriteTransaction.TabName, name,
                        PropertyDiff.Resolve(_session.Model, _keys), _value.Text);

                    _diff = diff;

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
                    Ui.Field("Value", _value),
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
                    foreach (var entry in PropertyDiff.Resolve(_session.Model, _keys))
                    {
                        var item = entry.Value;
                        if (item == null) continue;

                        var kind = PropertyDiff.Classify(item.Property(NavWriteTransaction.TabName, name),
                                                         _value.Text);

                        if (!PropertyDiff.ShouldWrite(kind, _existing)) continue;

                        write.SetProperty(entry.Key, NavWriteTransaction.TabName, name, _value.Text);
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

                var rows = levels.Bands.Select(b => new TableRow(b,
                    b.Name,
                    b.Elevation.ToString("0.000", CultureInfo.InvariantCulture) + " m",
                    b.Support > 0 ? Ui.Count(b.Support, "element") : string.Empty,
                    b.IsDerived ? "inferred" : "from the model")).ToList();

                results.Children.Add(Ui.Table(rows,
                    ("Level", 140), ("Elevation", 100), ("Elements", 110), ("Source", 110)));

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
        public static UIElement Takeoff()
        {
            var results = Ui.Stack();

            var measure = "Volume";
            var group = "Category";

            var measurePicker = Ui.Choice(new[] { "Volume", "Area", "Length", "Count" }, measure, p => measure = p);
            var groupPicker = Ui.Choice(new[] { "Category", "Type", "Model", "Level" }, group, p => group = p);

            var run = Ui.Runner("Sum this model", results, job =>
            {
                var session = Host.Current;
                if (session == null) { results.Children.Add(Ui.Line(Host.NoModel)); return; }

                var lines = new List<TakeoffLine>();

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(lines.Count, "elements read")) return;

                    var value = measure == "Count" ? "1" : Measure(item, measure);

                    lines.Add(new TakeoffLine(item.DisplayName, GroupOf(item, group), value));
                }

                job.Say("Adding up...");
                var result = Core.Data.Takeoff.Sum(lines);

                results.Children.Add(Ui.Line(result.ToString(), 1, true));

                if (result.Groups.Count == 0)
                {
                    results.Children.Add(Ui.Empty("Nothing to measure.", "Nothing loaded carries that property."));
                    return;
                }

                var rows = result.Groups.OrderByDescending(g => g.Count).Select(g => new TableRow(g,
                    g.Name,
                    g.Count.ToString("N0", CultureInfo.InvariantCulture),
                    g.Total?.ToString() ?? (g.MixedKinds ? "(mixed units)" : "-"),
                    g.Unreadable > 0 ? g.Unreadable.ToString(CultureInfo.InvariantCulture) + " unreadable" : string.Empty)
                {
                    Tone = g.IsComplete ? Tone.Plain : Tone.Warn,
                }).ToList();

                results.Children.Add(Ui.Table(rows,
                    ("Group", 190), ("Count", 80), ("Total", 130), ("", 0)));

                if (!result.IsComplete)
                    results.Children.Add(Ui.Note("Some values could not be read as quantities. They are counted "
                        + "and named rather than dropped, because a total that quietly excludes what it could "
                        + "not parse is worse than no total."));

                session.Record(ActivityKind.Report, "took off " + measure.ToLowerInvariant() + " by "
                                                    + group.ToLowerInvariant() + ": " + result);
            });

            return Ui.Scroll(Ui.Stack(
                Ui.Heading("Takeoff"),
                Ui.Sub("Sums whatever the elements carry, with no mapping table to build first."),
                Ui.Field("Measure", measurePicker),
                Ui.Field("Group by", groupPicker),
                run,
                results));
        }

        private static string? Measure(IModelItem item, string measure) =>
            item.Property("Element", measure)
            ?? item.Property("Item", measure)
            ?? item.Property("Revit Material Takeoff", measure)
            ?? item.Property(NavWriteTransaction.TabName, measure);

        private static string GroupOf(IModelItem item, string group) => group switch
        {
            "Type" => item.TypeName ?? "(no type)",
            "Model" => item.Model.DisplayName,
            "Level" => item.Property(NavWriteTransaction.TabName, "Level")
                       ?? item.Property("Element", "Level")
                       ?? item.Property("Item", "Level")
                       ?? "(no level)",
            _ => item.Category ?? "(no category)",
        };
    }
}
