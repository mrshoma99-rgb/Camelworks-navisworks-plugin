using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// A whole library of sets from an ordered list of properties, in one pass.
    ///
    /// <b>Why this is a new screen rather than a moved one.</b> Every other tool the capability
    /// sweep turned up existed behind the wrong door. These two engines - <see cref="SetTree"/>
    /// and <see cref="SetFamily"/> - had no door at all: tests, no caller anywhere in the UI. A
    /// coordinator who wanted a set per level, per system, per zone built them one at a time in
    /// the Set Builder, which is exactly the afternoon this was written to remove.
    ///
    /// <b>Not the same as Organise Sets.</b> That reshapes a library that already exists into the
    /// folders the properties imply. This makes the library. They share the publishing path and
    /// nothing else.
    ///
    /// Steps, because the order is real: the properties on offer come out of the model, and the
    /// tree cannot be costed before the properties are chosen.
    /// </summary>
    public static class SetLibraryWindow
    {
        /// <summary>Show the window.</summary>
        /// <returns>Something to tell the user, or null when it opened.</returns>
        public static string? Show()
        {
            var session = Host.Current;
            if (session == null) return Host.NoModel;

            return ToolWindow.Show(Tools.Find("setlib"), () => new Screen(session).Build(), 820, 640);
        }

        private sealed class Screen
        {
            /// <summary>How many distinct values make a property worth offering as a level.</summary>
            private const int TooMany = 60;

            private readonly Session _session;
            private readonly Panel _found = Ui.Stack();
            private readonly Panel _preview = Ui.Stack();
            private readonly Panel _output = Ui.Stack();

            private readonly List<string> _chosen = new List<string>();
            private readonly Dictionary<string, HashSet<string>> _values =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            private readonly List<KeyValuePair<ElementKey, Dictionary<string, string?>>> _read =
                new List<KeyValuePair<ElementKey, Dictionary<string, string?>>>();

            private readonly TextBox _pattern;
            private readonly TextBox _floor;

            private string _category = "Element";
            private SetTreePlan? _plan;

            internal Screen(Session session)
            {
                _session = session;
                _pattern = Ui.Text("{value}", null, 200);
                _floor = Ui.Text("1", null, 70);
            }

            internal UIElement Build()
            {
                _found.Children.Add(Ui.Empty("Nothing read yet.",
                    "Read the model and it will offer the properties that actually sort it."));

                _preview.Children.Add(Ui.Empty("No properties chosen.",
                    "Tick two or three, in the order the folders should nest."));

                var read = Ui.Stack(
                    Ui.Sub("Which properties sort this model into a library somebody can use?"),
                    Ui.Runner("Read the model", _found, Read),
                    _found);

                var shape = Ui.Stack(
                    Ui.Form(
                        Ui.Pair(Ui.Field("Name each set", _pattern,
                                         "{value} is the property's value, {path} the folders above it"),
                                Ui.Field("Skip sets smaller than", _floor, "elements"))),
                    Ui.Quiet("Cost it", Cost),
                    _preview);

                var publish = Ui.Stack(
                    Ui.Runner("Publish the library", _output, Publish),
                    Ui.Small("Saved into the project first and pushed to the host second, so a host "
                             + "that refuses one set leaves a library you can publish again rather "
                             + "than half a tree with no record of the other half."),
                    _output);

                var steps = new[]
                {
                    new Ui.Step("Read the model", () => read,
                                () => _read.Count == 0 ? "Read the model first." : null),
                    new Ui.Step("Choose the levels", () => shape,
                                () => _plan == null || !_plan.IsUsable
                                          ? "Choose properties and cost it first."
                                          : null),
                    new Ui.Step("Publish", () => publish),
                };

                return Ui.Steps(steps, null, null);
            }

            /// <summary>
            /// One pass, keeping every property's distinct values.
            ///
            /// Distinct-value count is the whole offer: a property with two values makes two sets
            /// and a property with nine thousand makes a Selection Tree nobody can scroll. Showing
            /// the count beside each name is what stops somebody picking the second kind.
            /// </summary>
            private void Read(Job job)
            {
                _read.Clear();
                _values.Clear();
                _chosen.Clear();
                _plan = null;

                var seen = 0;

                foreach (var item in _session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(seen++, "elements read")) return;

                    var row = new Dictionary<string, string?>(StringComparer.Ordinal);

                    foreach (var property in item.Properties())
                    {
                        if (string.IsNullOrEmpty(property.Value)) continue;

                        row[property.Name] = property.Value;

                        if (!_values.TryGetValue(property.Name, out var set))
                            _values[property.Name] = set = new HashSet<string>(StringComparer.Ordinal);

                        if (set.Count <= TooMany) set.Add(property.Value!);
                    }

                    _read.Add(new KeyValuePair<ElementKey, Dictionary<string, string?>>(item.Key, row));
                }

                _found.Children.Clear();

                if (_read.Count == 0)
                {
                    _found.Children.Add(Ui.Empty("Nothing is loaded.", "Append a model first."));
                    return;
                }

                var offer = _values
                    .Where(p => p.Value.Count > 1 && p.Value.Count <= TooMany)
                    .OrderBy(p => p.Value.Count)
                    .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _found.Children.Add(Ui.Line(Ui.Count(_read.Count, "element") + " read, "
                    + Ui.Count(offer.Count, "property") + " worth sorting by.", 1, true));

                if (offer.Count == 0)
                {
                    _found.Children.Add(Ui.Note("Every property here has either one value or more "
                        + "than " + TooMany + ". Neither makes a library: one value makes one set, "
                        + "and a property with a value per element makes a set per element."));
                    return;
                }

                foreach (var property in offer)
                {
                    var name = property.Key;

                    _found.Children.Add(Ui.Check(
                        name + "  -  " + Ui.Count(property.Value.Count, "value"),
                        false,
                        on =>
                        {
                            // Order is the folder nesting, so ticking appends rather than sorting:
                            // "Discipline then Level" and "Level then Discipline" are different
                            // libraries and the tick order is how somebody says which they meant.
                            if (on) { if (!_chosen.Contains(name)) _chosen.Add(name); }
                            else _chosen.Remove(name);
                        }));
                }

                _found.Children.Add(Ui.Small("Ticked in order: the first is the outermost folder."));
            }

            /// <summary>Cost the tree before anything is made.</summary>
            private void Cost()
            {
                _preview.Children.Clear();

                if (_chosen.Count == 0)
                {
                    _preview.Children.Add(Ui.Note("Tick at least one property."));
                    return;
                }

                var options = new SetTreeOptions { NamePattern = _pattern.Text };

                if (int.TryParse(_floor.Text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                 out var floor) && floor > 0)
                    options.Floor = floor;

                var rows = _read.Select(r => (IReadOnlyList<string?>)_chosen
                    .Select(p => r.Value.TryGetValue(p, out var v) ? v : null).ToList());

                _plan = SetTree.Of(_category, _chosen, rows, options);

                _preview.Children.Add(Ui.Line(_plan.Explain(), 1, true));

                if (!_plan.IsUsable) return;

                var sets = _plan.Sets;

                _preview.Children.Add(Ui.Table(
                    sets.Take(40).Select(leaf => new TableRow(leaf,
                        leaf.FullName,
                        leaf.Elements.ToString(CultureInfo.InvariantCulture))).ToList(),
                    ("Set", 0), ("Elements", 100)));

                if (sets.Count > 40)
                    _preview.Children.Add(Ui.Small("The first 40 of " + sets.Count + " shown."));

                if (_plan.Blanks > 0)
                    _preview.Children.Add(Ui.Note(Ui.Count(_plan.Blanks, "element")
                        + " carry no value for one of these properties, so they land in no set. "
                        + "That is a gap in the data, not in the library."));
            }

            /// <summary>Write the library into the project, then into the host.</summary>
            private void Publish(Job job)
            {
                var plan = _plan;
                if (plan == null || !plan.IsUsable) return;

                var sets = plan.Sets;

                var library = SetLibrary.FromJson(
                    _session.Store.Section(ProjectStore.SetsSection)["items"]);

                var published = 0;
                var live = 0;
                var refused = new List<string>();

                foreach (var leaf in sets)
                {
                    if (!job.Step(published, sets.Count, "sets published")) break;

                    var existing = library.Sets.FirstOrDefault(
                        s => string.Equals(s.Name, leaf.FullName, StringComparison.OrdinalIgnoreCase));

                    library.Put(new SavedSet(existing?.Id ?? library.NextId("set"),
                                             leaf.FullName, leaf.Expression!)
                    {
                        Note = "Generated from " + string.Join(" / ", _chosen) + ".",
                    });

                    try
                    {
                        var folders = leaf.Path.Take(leaf.Path.Count - 1).ToList();

                        var outcome = _session.Search.PublishInFolder(
                            folders, leaf.Name, DnfCompiler.Compile(leaf.Expression!),
                            SetTree.Comment(leaf));

                        published++;
                        if (outcome.IsLive) live++;
                    }
                    catch (Exception e)
                    {
                        refused.Add(leaf.FullName + " - " + Ui.Describe(e));
                    }
                }

                _session.Store.Section(ProjectStore.SetsSection).Set("items", library.ToJson());

                _output.Children.Add(_session.Store.Save()
                    ? Ui.Done("Published " + Ui.Count(published, "set") + " into folders.")
                    : Ui.Problem(_session.Store.LastSaveProblem ?? "Could not write the project file."));

                if (live < published)
                    _output.Children.Add(Ui.Note(Ui.Count(published - live, "set")
                        + " went in as a fixed list of elements rather than a live search, because "
                        + "the host could not express the rule as one search. Those will not pick up "
                        + "elements added later; the CamelWorks set will."));

                foreach (var problem in refused.Take(6)) _output.Children.Add(Ui.Problem(problem));

                _session.Record(ActivityKind.Regroup,
                                "generated " + Ui.Count(published, "set") + " from "
                                + string.Join(" / ", _chosen));
            }
        }
    }
}
