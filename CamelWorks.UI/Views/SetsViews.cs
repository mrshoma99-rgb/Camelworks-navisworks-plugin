using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
using CamelWorks.Nav;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The Sets &amp; Views workspace: build sets by rule, decide what the model looks like, and keep
    /// viewpoints in order.
    /// </summary>
    public static class SetsViews
    {
        private const string WholeModel = "Everything loaded";
        private const string Selection = "Current selection";

        /// <summary>The boolean set builder.</summary>
        public static UIElement Builder() => new SetBuilderScreen().Build();

        /// <summary>The layers system.</summary>
        public static UIElement Appearance() => new AppearanceScreen().Build();

        /// <summary>Bulk viewpoint operations.</summary>
        public static UIElement Viewpoints() => new ViewpointScreen().Build();

        // ===================================================================================
        // Resolving a layer target or a set expression against the host
        // ===================================================================================

        /// <summary>
        /// Turns a layer's target into the elements it currently covers.
        ///
        /// A rule target re-runs its search every time this is asked, which is the whole point of a
        /// rule target: the layer picks up elements added since it was made. Results are cached for
        /// the life of one fold so that ten layers over the same set do not run ten searches.
        /// </summary>
        internal sealed class HostResolver : ILayerResolver
        {
            private readonly Session _session;
            private readonly Dictionary<string, IReadOnlyCollection<ElementKey>> _cache =
                new Dictionary<string, IReadOnlyCollection<ElementKey>>(StringComparer.Ordinal);

            internal HostResolver(Session session) => _session = session;

            public IReadOnlyCollection<ElementKey> Resolve(LayerTarget target)
            {
                if (target.Expression == null) return target.Keys;

                var signature = target.Expression.Describe();

                if (_cache.TryGetValue(signature, out var cached)) return cached;

                var outcome = _session.Search.Run(DnfCompiler.Compile(target.Expression));
                _cache[signature] = outcome.Keys;
                return outcome.Keys;
            }
        }

        // ===================================================================================
        // Set builder
        // ===================================================================================

        /// <summary>
        /// The set builder, held as a small object rather than a function because it edits a list
        /// the screen redraws itself from.
        ///
        /// <b>The editor's shape is disjunctive normal form.</b> Groups are ORed, conditions inside
        /// a group are ANDed, and any condition can be negated. That is not a simplification of
        /// boolean algebra — it is all of it, and it happens to be the exact shape the host can run,
        /// so what the user builds and what Navisworks executes are the same structure.
        /// </summary>
        private sealed class SetBuilderScreen
        {
            private readonly List<List<Condition>> _groups = new List<List<Condition>> { new List<Condition>() };
            private readonly StackPanel _editor = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _saved = Ui.Stack();
            private readonly TextBox _name = Ui.Text("New set", null, 220);

            private sealed class Condition
            {
                internal bool Negated;
                internal string Category = "Element";
                internal string Property = "Category";
                internal SetOperator Operator = SetOperator.Equals;
                internal string Value = string.Empty;
                internal string? SetReference;
            }

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Ui.Scroll(Ui.Stack(Ui.Heading("Sets"), Ui.Sub(Host.NoModel)));

                Redraw();
                RedrawSaved(session);

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Sets"),
                    Ui.Sub("Groups are ORed together; conditions inside a group are ANDed. Any condition can "
                           + "be negated, and a negation compiles to a subtraction rather than a not-equals — "
                           + "those are different sets, because a host condition on a property an element does "
                           + "not have matches nothing."),
                    Ui.Card("Rule", null, _editor),
                    Ui.Field("Name", _name),
                    Actions(session),
                    _results,
                    new FamilyScreen(session, RedrawSaved).Build(),
                    new TreeScreen(session, RedrawSaved).Build(),
                    Ui.Card("Saved sets", "Kept in the project file, so they travel with the job.", _saved)));
            }

            private UIElement Actions(Session session)
            {
                var preview = Ui.Runner("Run", _results, job =>
                {
                    var plan = DnfCompiler.Compile(Expression());

                    _results.Children.Add(Ui.Line(plan.Explain(), 1, true));

                    foreach (var warning in plan.Warnings) _results.Children.Add(Ui.Note(warning));

                    if (plan.MatchesNothing)
                    {
                        _results.Children.Add(Ui.Empty("Matches nothing.",
                            "The rule contradicts itself — a condition and its negation are both required."));
                        return;
                    }

                    job.Say("Searching...");
                    var outcome = session.Search.Run(plan);

                    _results.Children.Add(Ui.Line(Ui.Count(outcome.Count, "element") + " found", 1, true));

                    foreach (var note in outcome.Notes.Skip(plan.Warnings.Count))
                        _results.Children.Add(Ui.Note(note));

                    if (outcome.Count > 0) session.Model.Select(outcome.Keys);
                });

                var save = Ui.Button("Save to project", () =>
                {
                    _results.Children.Clear();

                    var library = SetLibrary.FromJson(session.Store.Section(ProjectStore.SetsSection)["items"]);
                    var title = _name.Text.Trim();

                    if (title.Length == 0)
                    {
                        _results.Children.Add(Ui.Line("Give the set a name first.", 0.8));
                        return;
                    }

                    var existing = library.Sets.FirstOrDefault(
                        s => string.Equals(s.Name, title, StringComparison.OrdinalIgnoreCase));

                    library.Put(new SavedSet(existing?.Id ?? library.NextId("set"), title, Expression()));

                    session.Store.Section(ProjectStore.SetsSection).Set("items", library.ToJson());

                    _results.Children.Add(session.Store.Save()
                        ? Ui.Line("Saved.", 0.8)
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                    RedrawSaved(session);
                });

                var publish = Ui.Runner("Publish as a Navisworks set", _results, job =>
                {
                    var title = _name.Text.Trim();

                    if (title.Length == 0)
                    {
                        _results.Children.Add(Ui.Line("Give the set a name first.", 0.8));
                        return;
                    }

                    job.Say("Searching...");
                    var outcome = session.Search.Publish(title, DnfCompiler.Compile(Expression()));

                    _results.Children.Add(Ui.Line(
                        "Published \"" + title + "\" — " + outcome, 1, true));

                    _results.Children.Add(outcome.IsLive
                        ? Ui.Note("Saved as a search set: it re-runs, so elements added later appear in it.")
                        : Ui.Note("Saved as a selection set holding these exact elements. This rule needs more "
                                  + "than one search, and Navisworks search sets can only hold one."));
                });

                return Ui.Across(save, preview, publish);
            }

            private SetExpression Expression()
            {
                var clauses = new List<SetExpression>();

                foreach (var group in _groups)
                {
                    var parts = new List<SetExpression>();

                    foreach (var condition in group)
                    {
                        SetExpression leaf;

                        if (condition.SetReference != null)
                        {
                            leaf = SetExpression.InSet(condition.SetReference, condition.SetReference);
                        }
                        else
                        {
                            if (condition.Category.Trim().Length == 0) continue;

                            try
                            {
                                leaf = SetExpression.Where(new SetCondition(
                                    condition.Category.Trim(),
                                    condition.Property.Trim().Length == 0 ? null : condition.Property.Trim(),
                                    condition.Operator,
                                    NeedsValue(condition.Operator) ? condition.Value : null));
                            }
                            catch (ArgumentException)
                            {
                                // A half-typed row — a numeric comparison with no number in it yet.
                                // Skipped rather than throwing, so the preview stays usable while
                                // the rule is being written.
                                continue;
                            }
                        }

                        parts.Add(condition.Negated ? SetExpression.Not(leaf) : leaf);
                    }

                    if (parts.Count > 0) clauses.Add(SetExpression.And(parts.ToArray()));
                }

                return clauses.Count == 0 ? SetExpression.Nothing : SetExpression.Or(clauses.ToArray());
            }

            private static bool NeedsValue(SetOperator op) =>
                op != SetOperator.Defined && op != SetOperator.HasCategory;

            private void Redraw()
            {
                _editor.Children.Clear();

                for (var g = 0; g < _groups.Count; g++)
                {
                    var group = _groups[g];

                    if (g > 0) _editor.Children.Add(Ui.Line("or", 0.6, true));

                    foreach (var condition in group.ToList())
                        _editor.Children.Add(Row(group, condition));

                    var add = Ui.Button("and...", () => { group.Add(new Condition()); Redraw(); });
                    add.MinWidth = 70;
                    _editor.Children.Add(Ui.Across(add));
                }

                _editor.Children.Add(Ui.Across(
                    Ui.Button("Add an OR group", () => { _groups.Add(new List<Condition>()); Redraw(); })));
            }

            private UIElement Row(List<Condition> group, Condition condition)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };

                row.Children.Add(Ui.Check("not", condition.Negated, v => condition.Negated = v));
                row.Children.Add(Ui.Text(condition.Category, v => condition.Category = v, 110));
                row.Children.Add(Ui.Text(condition.Property, v => condition.Property = v, 110));

                row.Children.Add(Ui.Choice(
                    Enum.GetNames(typeof(SetOperator)),
                    condition.Operator.ToString(),
                    v => { if (Enum.TryParse<SetOperator>(v, out var op)) condition.Operator = op; },
                    150));

                row.Children.Add(Ui.Text(condition.Value, v => condition.Value = v, 130));

                var remove = Ui.Button("x", () => { group.Remove(condition); Redraw(); });
                remove.MinWidth = 26;
                row.Children.Add(remove);

                foreach (var child in row.Children.OfType<FrameworkElement>())
                    child.Margin = new Thickness(0, 0, 5, 0);

                return row;
            }

            private void RedrawSaved(Session session)
            {
                _saved.Children.Clear();

                var library = SetLibrary.FromJson(session.Store.Section(ProjectStore.SetsSection)["items"]);

                if (library.Sets.Count == 0)
                {
                    _saved.Children.Add(Ui.Empty("None saved yet.",
                        "Sets you save here are stored in the project file beside the model, not in your "
                        + "own profile, so whoever opens the job next has them too."));
                    return;
                }

                var rows = library.Sets
                    .Select(s => new TableRow(s, s.Name, s.Expression.Describe()))
                    .ToList();

                _saved.Children.Add(Ui.Table(rows, ("Set", 160), ("Rule", 0)).OnPick(item =>
                {
                    if (item is SavedSet set) Load(set);
                }));
            }

            private void Load(SavedSet set)
            {
                _name.Text = set.Name;
                _groups.Clear();

                foreach (var clause in Clauses(set.Expression))
                {
                    var group = new List<Condition>();

                    foreach (var part in Parts(clause))
                    {
                        var negated = part is NotExpression;
                        var inner = negated ? ((NotExpression)part).Inner : part;

                        if (inner is ConditionExpression c)
                            group.Add(new Condition
                            {
                                Negated = negated,
                                Category = c.Condition.Category,
                                Property = c.Condition.Property ?? string.Empty,
                                Operator = c.Condition.Operator,
                                Value = c.Condition.Value ?? string.Empty,
                            });
                        else if (inner is SetReferenceExpression r)
                            group.Add(new Condition { Negated = negated, SetReference = r.SetId });
                    }

                    _groups.Add(group);
                }

                if (_groups.Count == 0) _groups.Add(new List<Condition>());

                Redraw();
            }

            private static IEnumerable<SetExpression> Clauses(SetExpression expression) =>
                expression is OrExpression union ? union.Parts : new[] { expression };

            private static IEnumerable<SetExpression> Parts(SetExpression expression) =>
                expression is AndExpression all ? all.Parts : new[] { expression };
        }

        // ===================================================================================
        // Generating a family of sets
        // ===================================================================================

        /// <summary>
        /// One set per level, per discipline, per zone — made in one pass instead of one at a time.
        ///
        /// This is an hour of clicking that gets done on every job, from a list somebody reads off
        /// the properties panel. The list is already in the model, and a single pass produces both
        /// the values and how many elements carry each one — which is the number that decides
        /// whether a set is worth having at all.
        ///
        /// Nothing has to be set up first. The property boxes start on the two things every
        /// federation has an answer for, and the run tells you what it found rather than making you
        /// know in advance.
        /// </summary>
        private sealed class FamilyScreen
        {
            private readonly Session _session;
            private readonly Action<Session> _refresh;

            private readonly TextBox _category = Ui.Text("Element", null, 150);
            private readonly TextBox _property = Ui.Text("Level", null, 150);
            private readonly TextBox _pattern = Ui.Text("{value}", null, 180);
            private readonly TextBox _floor = Ui.Text("1", null, 60);
            private readonly StackPanel _preview = Ui.Stack();
            private readonly StackPanel _status = Ui.Stack();

            private SetFamilyPlan? _plan;

            internal FamilyScreen(Session session, Action<Session> refresh)
            {
                _session = session;
                _refresh = refresh;
            }

            internal UIElement Build()
            {
                var scope = WholeModel;
                var picker = Ui.Choice(new[] { WholeModel, Selection }, scope, pick => scope = pick);

                var look = Ui.Runner("See what it would make", _preview, job =>
                {
                    _plan = null;
                    _status.Children.Clear();

                    var category = _category.Text.Trim();
                    var property = _property.Text.Trim();

                    if (category.Length == 0 || property.Length == 0)
                    {
                        _preview.Children.Add(Ui.Problem("Name the property to split on first."));
                        return;
                    }

                    job.Say("Reading the model...");

                    var values = new List<string?>();
                    var traversal = scope == Selection
                        ? TraversalScope.CurrentSelection
                        : TraversalScope.WholeDocument;

                    foreach (var item in _session.Model.Traverse(traversal))
                    {
                        if (values.Count % 2000 == 0 && !job.Step(values.Count, "elements read")) return;
                        values.Add(item.Property(category, property));
                    }

                    if (values.Count == 0)
                    {
                        _preview.Children.Add(Ui.Empty("Nothing in scope.",
                            scope == Selection
                                ? "Select some elements, or switch the scope to the whole model."
                                : "No models are loaded."));
                        return;
                    }

                    var library = SetLibrary.FromJson(_session.Store.Section(ProjectStore.SetsSection)["items"]);

                    var plan = SetFamily.Of(category, property, values, new FamilyOptions
                    {
                        NamePattern = _pattern.Text,
                        Floor = Floor(_floor.Text),
                        Existing = library.Sets.Select(set => set.Name).ToList(),
                    });

                    _plan = plan;
                    Show(plan, values.Count);
                });

                return Ui.Card("Make a family of sets",
                    "One set per value of a property. Each one is a rule, not a list, so a set made "
                    + "today still covers the walls somebody adds tomorrow.",
                    Ui.Field("Scope", picker),
                    Ui.Field("Property tab", _category),
                    Ui.Field("Property", _property),
                    Ui.Field("Name pattern", _pattern),
                    Ui.Field("Skip values under", _floor),
                    look,
                    _preview,
                    _status);
            }

            private void Show(SetFamilyPlan plan, int scanned)
            {
                _preview.Children.Add(Ui.Line(plan.Explain(), 1, true));
                _preview.Children.Add(Ui.Line(Ui.Count(scanned, "element") + " read", 0.6));

                if (plan.Sets.Count == 0)
                {
                    _preview.Children.Add(Ui.Empty("Nothing to split on.",
                        "No element in scope carries \"" + plan.Category + " / " + plan.Property
                        + "\". Check the spelling against the properties panel — the tab name matters "
                        + "as much as the property name."));
                    return;
                }

                foreach (var set in plan.Sets)
                {
                    var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var proposed = set;

                    row.Children.Add(Ui.Check(string.Empty, set.IsSelected, v => proposed.IsSelected = v));
                    row.Children.Add(Ui.Line(set.Name, 1, true));
                    row.Children.Add(Ui.Line(Ui.Count(set.Elements, "element"), 0.7));

                    if (set.Note != null) row.Children.Add(Ui.Pill(set.Note, Tone.Warn));

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 8, 0);

                    _preview.Children.Add(row);
                }

                if (plan.Blanks > 0)
                    _preview.Children.Add(Ui.Note(Ui.Count(plan.Blanks, "element")
                        + " carry no value for this property and are in none of these sets. They are "
                        + "not an \"unassigned\" set: an element with no level is one somebody has to "
                        + "look at, and collecting them would make that gap look handled."));

                _preview.Children.Add(Ui.Across(
                    Ui.Button("Save the ticked ones", Save),
                    Ui.Button("Save and publish them", Publish)));
            }

            /// <summary>Write the ticked sets into the project file.</summary>
            private void Save()
            {
                _status.Children.Clear();

                var wanted = Wanted();
                if (wanted == null) return;

                var library = SetLibrary.FromJson(_session.Store.Section(ProjectStore.SetsSection)["items"]);

                foreach (var set in wanted)
                {
                    var existing = library.Sets.FirstOrDefault(
                        s => string.Equals(s.Name, set.Name, StringComparison.OrdinalIgnoreCase));

                    library.Put(new SavedSet(existing?.Id ?? library.NextId("set"), set.Name, set.Expression)
                    {
                        Note = "One of a family split on " + _plan!.Category + " / " + _plan.Property + ".",
                    });
                }

                _session.Store.Section(ProjectStore.SetsSection).Set("items", library.ToJson());

                _status.Children.Add(_session.Store.Save()
                    ? Ui.Line("Saved " + Ui.Count(wanted.Count, "set") + " to the project.", 1, true)
                    : Ui.Problem(_session.Store.LastSaveProblem ?? "Could not write the project file."));

                _session.Record(ActivityKind.Reconcile,
                    "made " + Ui.Count(wanted.Count, "set") + " split on " + _plan!.Property);

                _refresh(_session);
            }

            /// <summary>
            /// Write them, then push each one into the host as a native search set.
            ///
            /// Saved first and published second, deliberately: publishing is the half that can fail
            /// per set, and a family that saved cleanly and published nine of eleven is recoverable.
            /// A family that published and then failed to save is eleven host sets nobody can find
            /// again from CamelWorks.
            /// </summary>
            private void Publish()
            {
                Save();

                var wanted = Wanted();
                if (wanted == null) return;

                var published = 0;
                var live = 0;
                var refused = new List<string>();

                foreach (var set in wanted)
                {
                    try
                    {
                        var outcome = _session.Search.Publish(set.Name, DnfCompiler.Compile(set.Expression));

                        published++;
                        if (outcome.IsLive) live++;
                    }
                    catch (Exception e)
                    {
                        // Named, not counted, and the loop carries on. A family where two of eleven
                        // were refused is a family with two holes in it, and a total does not say
                        // which two.
                        refused.Add(set.Name + " - " + Ui.Describe(e));
                    }
                }

                _status.Children.Add(Ui.Line("Published " + Ui.Count(published, "set")
                    + " into the host's selection sets.", 1, true));

                if (live < published)
                    _status.Children.Add(Ui.Note(Ui.Count(published - live, "set")
                        + " went in as a fixed list of elements rather than a live search, because the "
                        + "host could not express the rule as a search. Those will not pick up elements "
                        + "added later; the CamelWorks set will."));

                foreach (var problem in refused) _status.Children.Add(Ui.Problem(problem));

                if (refused.Count > 0)
                    _status.Children.Add(Ui.Note("Every set here is saved in the project whatever the host "
                        + "did with it, so a refusal costs nothing but a second attempt."));

                _session.Record(ActivityKind.Export, "published " + Ui.Count(published, "set"));
            }

            /// <summary>The ticked sets, or null with a message when there are none.</summary>
            private IReadOnlyList<ProposedSet>? Wanted()
            {
                var plan = _plan;

                if (plan == null)
                {
                    _status.Children.Add(Ui.Line("Press the button above first.", 0.8));
                    return null;
                }

                var wanted = plan.Sets.Where(s => s.IsSelected).ToList();

                if (wanted.Count == 0)
                {
                    _status.Children.Add(Ui.Line("Nothing is ticked.", 0.8));
                    return null;
                }

                return wanted;
            }

            private static int Floor(string? text) =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
                    ? value
                    : FamilyOptions.DefaultFloor;
        }

        // ===================================================================================
        // A whole library at once
        // ===================================================================================

        /// <summary>
        /// The nested version: level, then discipline, then category, filed into real folders.
        ///
        /// The single-property generator above makes one flat run of sets. This makes the library —
        /// a folder per level holding a folder per discipline holding a set per category, which is
        /// what the Selection Tree on a real job looks like and what somebody otherwise assembles by
        /// hand over an afternoon, once per project, differently each time.
        ///
        /// Three properties deep and no further, and it refuses rather than truncating past its
        /// ceiling. Both are deliberate and both are explained on screen rather than enforced
        /// silently.
        ///
        /// Nothing has to be set up first. The three boxes start on the three things every
        /// federation has an answer for, and the run reports what it actually found.
        /// </summary>
        private sealed class TreeScreen
        {
            private readonly Session _session;
            private readonly Action<Session> _refresh;

            private readonly TextBox _category = Ui.Text("Element", null, 150);
            private readonly TextBox _first = Ui.Text("Level", null, 140);
            private readonly TextBox _second = Ui.Text("Discipline", null, 140);
            private readonly TextBox _third = Ui.Text(string.Empty, null, 140);
            private readonly TextBox _pattern = Ui.Text("{value}", null, 160);
            private readonly TextBox _floor = Ui.Text("1", null, 60);
            private readonly StackPanel _preview = Ui.Stack();
            private readonly StackPanel _status = Ui.Stack();

            private readonly HashSet<string> _skipped =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private SetTreePlan? _plan;

            internal TreeScreen(Session session, Action<Session> refresh)
            {
                _session = session;
                _refresh = refresh;
            }

            internal UIElement Build()
            {
                var scope = WholeModel;
                var picker = Ui.Choice(new[] { WholeModel, Selection }, scope, pick => scope = pick);

                var look = Ui.Runner("See the library it would make", _preview, job =>
                {
                    _plan = null;
                    _skipped.Clear();
                    _status.Children.Clear();

                    var category = _category.Text.Trim();

                    var properties = new[] { _first.Text, _second.Text, _third.Text }
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList();

                    if (category.Length == 0 || properties.Count == 0)
                    {
                        _preview.Children.Add(Ui.Problem(
                            "Name the property tab and at least the first property to split on."));
                        return;
                    }

                    job.Say("Reading the model...");

                    var rows = new List<IReadOnlyList<string?>>();
                    var traversal = scope == Selection
                        ? TraversalScope.CurrentSelection
                        : TraversalScope.WholeDocument;

                    foreach (var item in _session.Model.Traverse(traversal))
                    {
                        if (rows.Count % 2000 == 0 && !job.Step(rows.Count, "elements read")) return;

                        rows.Add(properties.Select(p => item.Property(category, p)).ToList());
                    }

                    if (rows.Count == 0)
                    {
                        _preview.Children.Add(Ui.Empty("Nothing in scope.",
                            scope == Selection
                                ? "Select some elements, or switch the scope to the whole model."
                                : "No models are loaded."));
                        return;
                    }

                    var plan = SetTree.Of(category, properties, rows, new SetTreeOptions
                    {
                        NamePattern = _pattern.Text,
                        Floor = Floor(_floor.Text),
                    });

                    _plan = plan;
                    Show(plan, rows.Count);
                });

                return Ui.Card("Make a whole library, in folders",
                    "Up to three properties, nested. Every set is a rule that carries its whole path, so it "
                    + "still means the same thing if somebody drags it out of its folder.",
                    Ui.Field("Scope", picker),
                    Ui.Field("Property tab", _category),
                    Ui.Field("Split on", _first),
                    Ui.Field("Then on", _second),
                    Ui.Field("Then on", _third),
                    Ui.Field("Set name", _pattern),
                    Ui.Field("Skip branches under", _floor),
                    look,
                    _preview,
                    _status);
            }

            private void Show(SetTreePlan plan, int scanned)
            {
                _preview.Children.Add(Ui.Line(plan.Explain(), 1, true));

                // What was read, not what the plan counted: a refusal counts nothing, and "0
                // elements read" under a refusal reads as though the scope were empty.
                _preview.Children.Add(Ui.Line(Ui.Count(scanned, "element") + " read", 0.6));

                if (plan.Refusal != null)
                {
                    _preview.Children.Add(Ui.Note(
                        "Nothing has been made and nothing has been changed. Adjust the boxes above and "
                        + "run it again."));
                    return;
                }

                if (plan.Roots.Count == 0)
                {
                    _preview.Children.Add(Ui.Empty("Nothing to split on.",
                        "No element in scope carries all of those properties. Check the spelling against "
                        + "the properties panel — the tab name matters as much as the property name."));
                    return;
                }

                foreach (var root in plan.Roots) Draw(root, 0);

                if (plan.Blanks > 0)
                    _preview.Children.Add(Ui.Note(Ui.Count(plan.Blanks, "element")
                        + " carry no value for one of these properties and are in none of these sets. They "
                        + "are not collected into an \"unassigned\" folder: an element with no level is one "
                        + "somebody has to go and look at, and filing it tidily is how that stops looking "
                        + "like a gap. Health will list them."));

                _preview.Children.Add(Ui.Note(
                    "Untick a folder to leave the whole branch out. Publishing makes real folders in the "
                    + "host's Sets panel and writes the rule into each set's comment, so a colleague "
                    + "without CamelWorks can read what it matches."));

                _preview.Children.Add(Ui.Across(
                    Ui.Button("Save them to the project", Save),
                    Ui.Button("Save and publish into folders", Publish)));
            }

            /// <summary>One branch, indented by its depth.</summary>
            private void Draw(SetBranch branch, int depth)
            {
                var path = string.Join("/", branch.Path);

                var row = new WrapPanel { Margin = new Thickness(depth * 18, 2, 0, 2) };

                row.Children.Add(Ui.Check(string.Empty, true, on =>
                {
                    if (on) _skipped.Remove(path);
                    else _skipped.Add(path);
                }));

                row.Children.Add(Ui.Line(branch.IsLeaf ? branch.Name : branch.Value + "/", 1, !branch.IsLeaf));
                row.Children.Add(Ui.Line(Ui.Count(branch.Elements, "element"), 0.7));

                if (!branch.IsLeaf)
                    row.Children.Add(Ui.Pill(Ui.Count(branch.Leaves().Count(), "set"), Tone.Plain));

                foreach (var child in row.Children.OfType<FrameworkElement>())
                    child.Margin = new Thickness(0, 0, 8, 0);

                _preview.Children.Add(row);

                foreach (var child in branch.Children) Draw(child, depth + 1);
            }

            /// <summary>Write the ticked sets into the project file, path and all.</summary>
            private void Save()
            {
                _status.Children.Clear();

                var wanted = Wanted();
                if (wanted == null) return;

                var library = SetLibrary.FromJson(_session.Store.Section(ProjectStore.SetsSection)["items"]);

                foreach (var leaf in wanted)
                {
                    // The long name, not the short one. The project's set list is flat, and a
                    // "Ducts" under Level 01 and a "Ducts" under Level 02 would be one entry.
                    var name = leaf.FullName;

                    var existing = library.Sets.FirstOrDefault(
                        s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

                    library.Put(new SavedSet(existing?.Id ?? library.NextId("set"), name, leaf.Expression!)
                    {
                        Note = "One of a library split on "
                             + string.Join(" / ", _plan!.Properties) + ".",
                    });
                }

                _session.Store.Section(ProjectStore.SetsSection).Set("items", library.ToJson());

                _status.Children.Add(_session.Store.Save()
                    ? Ui.Line("Saved " + Ui.Count(wanted.Count, "set") + " to the project.", 1, true)
                    : Ui.Problem(_session.Store.LastSaveProblem ?? "Could not write the project file."));

                _session.Record(ActivityKind.Reconcile,
                    "made " + Ui.Count(wanted.Count, "set") + " split on "
                    + string.Join(" / ", _plan!.Properties));

                _refresh(_session);
            }

            /// <summary>
            /// Write them, then push each one into the host inside its folder.
            ///
            /// Saved first and published second, for the same reason the flat generator does it:
            /// publishing is the half that can fail per set, and a library that saved cleanly and
            /// published forty of forty-five is recoverable from CamelWorks. The other order leaves
            /// host sets nobody can find their way back to.
            /// </summary>
            private void Publish()
            {
                Save();

                var wanted = Wanted();
                if (wanted == null) return;

                var published = 0;
                var live = 0;
                var refused = new List<string>();

                foreach (var leaf in wanted)
                {
                    try
                    {
                        var folders = leaf.Path.Take(leaf.Path.Count - 1).ToList();

                        var outcome = _session.Search.PublishInFolder(
                            folders, leaf.Name,
                            DnfCompiler.Compile(leaf.Expression!),
                            SetTree.Comment(leaf));

                        published++;
                        if (outcome.IsLive) live++;
                    }
                    catch (Exception e)
                    {
                        // Named, not counted, and the loop carries on. A library with three holes
                        // in it is recoverable; a total that says "42 of 45" is not.
                        refused.Add(leaf.FullName + " - " + Ui.Describe(e));
                    }
                }

                _status.Children.Add(Ui.Line("Published " + Ui.Count(published, "set")
                    + " into the host's selection sets, in folders.", 1, true));

                if (live < published)
                    _status.Children.Add(Ui.Note(Ui.Count(published - live, "set")
                        + " went in as a fixed list of elements rather than a live search, because the "
                        + "host could not express the rule as a search. Those will not pick up elements "
                        + "added later; the CamelWorks set will."));

                foreach (var problem in refused) _status.Children.Add(Ui.Problem(problem));

                if (refused.Count > 0)
                    _status.Children.Add(Ui.Note("Every set here is saved in the project whatever the host "
                        + "did with it, so a refusal costs nothing but a second attempt."));

                _session.Record(ActivityKind.Export, "published " + Ui.Count(published, "set") + " in folders");
            }

            /// <summary>The sets under no unticked branch, or null with a message when there are none.</summary>
            private IReadOnlyList<SetBranch>? Wanted()
            {
                var plan = _plan;

                if (plan == null || plan.Refusal != null)
                {
                    _status.Children.Add(Ui.Line("Run it above first.", 0.8));
                    return null;
                }

                // A folder unticked takes its whole branch with it, which is the point of ticking a
                // folder rather than forty sets.
                var wanted = plan.Sets
                    .Where(leaf => !Enumerable.Range(1, leaf.Path.Count).Any(
                        depth => _skipped.Contains(string.Join("/", leaf.Path.Take(depth)))))
                    .ToList();

                if (wanted.Count == 0)
                {
                    _status.Children.Add(Ui.Line("Everything is unticked.", 0.8));
                    return null;
                }

                return wanted;
            }

            private static int Floor(string? text) =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
                    ? value
                    : 1;
        }

        // ===================================================================================
        // Appearance
        // ===================================================================================

        /// <summary>
        /// The layers system.
        ///
        /// The question the host cannot answer is "what is hidden right now, and why" — and it is
        /// the question that stops a federation being handed over. Navisworks hides an element and
        /// forgets everything about the act; a week later four hundred elements are missing and
        /// nobody dares un-hide them in case something was hidden for a reason.
        ///
        /// So every change goes through a named layer that says what it decides and why, the fold
        /// reports which layer won each property and which were overruled, and overrides CamelWorks
        /// did not author are shown and never cleared.
        /// </summary>
        private sealed class AppearanceScreen
        {
            /// <summary>How many elements are read from the host in one go while scanning.</summary>
            private const int ScanChunk = 5000;

            private readonly List<AppearanceLayer> _layers = new List<AppearanceLayer>();
            private readonly StackPanel _stack = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _adder = Ui.Stack();
            private readonly StackPanel _foreign = Ui.Stack();

            private ForeignGroup? _picked;

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Ui.Scroll(Ui.Stack(Ui.Heading("Appearance"), Ui.Sub(Host.NoModel)));

                _layers.AddRange(LayerStackJson.Read(session.Store.Section(ProjectStore.LayersSection)["stack"]));

                Redraw(session);
                BuildAdder(session);

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Appearance"),
                    Ui.Sub("Every override and every hidden element, as a stack of named layers. The layer at "
                           + "the top wins, property by property — a layer that only sets a colour leaves "
                           + "visibility to the one underneath."),
                    Ui.Card("Layers", "Top of the list wins.", _stack),
                    _adder,
                    Actions(session),
                    _results,
                    Elsewhere(session)));
            }

            private UIElement Actions(Session session)
            {
                var apply = Ui.Runner("Apply the stack", _results, job =>
                {
                    job.Say("Resolving layers...");

                    var fold = AppearanceStack.Fold(_layers, new HostResolver(session));

                    job.Say("Reading what the model looks like now...");

                    var keys = fold.Elements.Select(e => e.Key).ToList();
                    var current = session.View.ReadAppearance(keys);

                    var plan = AppearancePlanner.Plan(fold, current);

                    _results.Children.Add(Ui.Line(fold.ToString(), 1, true));
                    _results.Children.Add(Ui.Line(plan.Explain(), 0.8));

                    if (plan.Foreign.Count > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(plan.Foreign.Count, "element")
                            + " carry an override CamelWorks did not author. They are left exactly as they are: "
                            + "clearing somebody else's work because it was in the way is how a tool loses trust."));

                    if (plan.IsEmpty)
                    {
                        _results.Children.Add(Ui.Empty("Nothing to change.",
                            "The model already looks the way this stack says it should."));
                    }
                    else
                    {
                        job.Say("Applying...");
                        Execute(session, plan);

                        _results.Children.Add(Ui.Line("Applied.", 1, true));
                        session.Record(ActivityKind.Appearance, "applied the appearance stack: " + plan.Explain());
                    }

                    foreach (var report in fold.Layers.Where(l => l.IsDead))
                        _results.Children.Add(Ui.Note("\"" + report.Layer.Name + "\" covers "
                            + Ui.Count(report.Covers, "element") + " and decides nothing about any of them — "
                            + "every property it sets is overruled by a layer above it."));

                    Explain(session, fold);
                });

                var save = Ui.Button("Save the stack", () =>
                {
                    _results.Children.Clear();

                    session.Store.Section(ProjectStore.LayersSection).Set("stack", LayerStackJson.Write(_layers));

                    _results.Children.Add(session.Store.Save()
                        ? Ui.Line("Saved to " + session.Store.Where + ".", 0.8)
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
                });

                var reset = Ui.Button("Show everything", () =>
                {
                    _results.Children.Clear();

                    // Deliberately not "clear all overrides": that would also throw away colouring
                    // somebody applied by hand outside CamelWorks.
                    //
                    // One resolver for the whole pass, because it caches: ten layers over the same
                    // saved set would otherwise run the same search ten times.
                    var resolver = new HostResolver(session);
                    var all = new HashSet<ElementKey>();

                    foreach (var layer in _layers)
                        foreach (var key in resolver.Resolve(layer.Target))
                            all.Add(key);

                    session.View.SetVisible(all.ToList(), true);

                    _results.Children.Add(Ui.Line("Un-hid " + Ui.Count(all.Count, "element")
                                                  + " the stack covers. Colours are left alone.", 0.8));
                });

                return Ui.Across(apply, save, reset);
            }

            /// <summary>
            /// Everything the model is in that this stack did not ask for.
            ///
            /// The layer list above says what CamelWorks intends. It says nothing about the isolate
            /// somebody left switched on in Clash Detective, the transparency a colleague applied
            /// last Thursday, or the elements a saved viewpoint hid — and those are exactly why a
            /// federation looks wrong to one person and fine to another. Until they are on screen,
            /// nobody can tell which of the two they are looking at.
            ///
            /// Three things can be done with a row, and the difference between them is who ends up
            /// responsible. Select shows you. Adopt writes a layer that reproduces the state
            /// exactly, so the stack now accounts for it and nothing moves. Put back is the only
            /// thing in CamelWorks that undoes somebody else's work, which is why it is a button a
            /// person presses on one named row rather than anything that happens on Apply.
            /// </summary>
            private UIElement Elsewhere(Session session)
            {
                var scan = Ui.Runner("Scan the model", _foreign, job =>
                {
                    _picked = null;

                    job.Say("Resolving layers...");
                    var fold = AppearanceStack.Fold(_layers, new HostResolver(session));

                    job.Say("Listing elements...");

                    var keys = new List<ElementKey>();

                    foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                    {
                        if (keys.Count % 2000 == 0 && !job.Step(keys.Count, "elements listed")) return;
                        if (item.HasGeometry) keys.Add(item.Key);
                    }

                    // Read in chunks so the Stop button means something. A federation is hundreds of
                    // thousands of elements and each reading crosses into the host; one call for all
                    // of them would be a frozen window with no way out of it.
                    var states = new List<AppearanceState>(keys.Count);

                    for (var at = 0; at < keys.Count; at += ScanChunk)
                    {
                        if (!job.Step(at, "elements read")) return;

                        var chunk = keys.GetRange(at, Math.Min(ScanChunk, keys.Count - at));
                        states.AddRange(session.View.ReadAppearance(chunk));
                    }

                    job.Say("Working out what is not ours...");
                    var audit = ForeignAppearance.Find(states, fold);

                    _foreign.Children.Add(Ui.Line(audit.Explain(), 1, true));

                    if (audit.IsClean)
                    {
                        _foreign.Children.Add(Ui.Empty("Nothing else is going on.",
                            "Every element is either untouched or accounted for by a layer above. "
                            + "That is a real result — the scan ran over the whole federation."));
                        return;
                    }

                    _foreign.Children.Add(Ui.Across(
                        Ui.Figure(audit.Hidden.ToString("N0", CultureInfo.InvariantCulture), "hidden",
                                  audit.Hidden > 0 ? Tone.Warn : Tone.Plain),
                        Ui.Figure((audit.Total - audit.Hidden).ToString("N0", CultureInfo.InvariantCulture),
                                  "overridden"),
                        Ui.Figure(audit.Claimed.ToString("N0", CultureInfo.InvariantCulture), "ours already")));

                    var rows = audit.Groups.Select(g => new TableRow(g,
                        g.Describe(),
                        g.Keys.Count.ToString("N0", CultureInfo.InvariantCulture),
                        g.IsHidden ? "you cannot see these" : string.Empty)
                    {
                        Tone = g.IsHidden ? Tone.Warn : Tone.Plain,
                    }).ToList();

                    _foreign.Children.Add(Ui.Table(rows, ("State", 260), ("Elements", 90), ("", 0))
                        .OnPick(item => _picked = item as ForeignGroup));

                    if (audit.OtherGroups > 0)
                        _foreign.Children.Add(Ui.Note(
                            Ui.Count(audit.OtherGroups, "further state") + " covering "
                            + Ui.Count(audit.OtherElements, "element")
                            + " are counted above but not listed. A model where thousands of elements "
                            + "each carry their own colour is a painted model, not a coordination "
                            + "problem, and a table of them would be unreadable."));

                    _foreign.Children.Add(Ui.Line("Pick a row, then choose what to do with it.", 0.6));
                    _foreign.Children.Add(Ui.Across(SelectButton(session), AdoptButton(session), PutBackButton(session)));
                });

                return Ui.Card("What else is going on",
                    "Everything hidden or overridden that no layer above accounts for. Reading it is a "
                    + "pass over the whole federation, so it happens when you ask rather than when the "
                    + "tab opens.",
                    scan,
                    _foreign);
            }

            private System.Windows.Controls.Button SelectButton(Session session) =>
                Ui.Button("Select them", () =>
                {
                    if (_picked == null) return;
                    session.Model.Select(_picked.Keys);
                });

            private System.Windows.Controls.Button AdoptButton(Session session) =>
                Ui.Button("Make it a layer", () =>
                {
                    if (_picked == null) return;

                    var layer = ForeignAppearance.Adopt(_picked,
                        "adopted-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

                    _layers.Add(layer);
                    Redraw(session);

                    _foreign.Children.Add(Ui.Line("\"" + layer.Name + "\" is now on top of the stack. "
                        + "The model looks exactly as it did — what changed is that the stack now says "
                        + "why.", 0.8));

                    session.Record(ActivityKind.Appearance, "adopted " + layer.Name);
                });

            private System.Windows.Controls.Button PutBackButton(Session session) =>
                Ui.Button("Put them back", () =>
                {
                    if (_picked == null) return;

                    var keys = _picked.Keys;

                    session.View.ClearOverrides(keys);
                    if (_picked.IsHidden) session.View.SetVisible(keys, true);

                    _foreign.Children.Add(Ui.Line("Reset " + Ui.Count(keys.Count, "element")
                        + ". Scan again to see what is left.", 0.8));

                    session.Record(ActivityKind.Appearance,
                        "reset " + Ui.Count(keys.Count, "element") + " somebody else had overridden");

                    _picked = null;
                });

            private static void Execute(Session session, AppearancePlan plan)
            {
                if (plan.Clear.Count > 0) session.View.ClearOverrides(plan.Clear);
                if (plan.Hide.Count > 0) session.View.SetVisible(plan.Hide, false);
                if (plan.Show.Count > 0) session.View.SetVisible(plan.Show, true);

                foreach (var batch in plan.Colours) session.View.SetColour(batch.Keys, batch.Colour);

                foreach (var batch in plan.Transparencies)
                    session.View.SetTransparency(batch.Keys, batch.Transparency);
            }

            private void Explain(Session session, AppearanceFold fold)
            {
                var selected = session.Model.SelectedKeys();
                if (selected.Count == 0) return;

                var appearance = fold.For(selected[0]);
                if (appearance == null) return;

                _results.Children.Add(Ui.Card("The selected element",
                    "Why it looks the way it does.", Ui.Line(appearance.Explain(), 0.85)));
            }

            private void Redraw(Session session)
            {
                _stack.Children.Clear();

                if (_layers.Count == 0)
                {
                    _stack.Children.Add(Ui.Empty("No layers yet.",
                        "Add one from the current selection or from a saved set. Until then CamelWorks is not "
                        + "deciding anything about how the model looks."));
                    return;
                }

                // Held bottom-first, because that is the order AppearanceStack.Fold applies it and
                // the order it is saved in. Shown the other way up, the way every layers panel does.
                for (var i = _layers.Count - 1; i >= 0; i--)
                {
                    var layer = _layers[i];
                    var index = i;

                    var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 3) };

                    row.Children.Add(Ui.Check(string.Empty, layer.IsEnabled, v => layer.IsEnabled = v));

                    row.Children.Add(Ui.Line(layer.Name, 1, true));
                    row.Children.Add(Ui.Pill(layer.Target.ReResolves ? "rule" : "fixed",
                        layer.Target.ReResolves ? Tone.Good : Tone.Plain));
                    row.Children.Add(Ui.Line(layer.Target.Description, 0.7));
                    row.Children.Add(Ui.Line(Effects(layer), 0.85));

                    var up = Ui.Button("up", () => Move(session, index, 1));
                    var down = Ui.Button("down", () => Move(session, index, -1));
                    var drop = Ui.Button("remove", () => { _layers.RemoveAt(index); Redraw(session); });

                    foreach (var button in new[] { up, down, drop }) button.MinWidth = 54;

                    row.Children.Add(up);
                    row.Children.Add(down);
                    row.Children.Add(drop);

                    foreach (var child in row.Children.OfType<FrameworkElement>())
                        child.Margin = new Thickness(0, 0, 6, 0);

                    _stack.Children.Add(row);

                    if (!string.IsNullOrEmpty(layer.Note))
                        _stack.Children.Add(Ui.Line(layer.Note!, 0.6));
                }
            }

            private static string Effects(AppearanceLayer layer)
            {
                var parts = new List<string>();

                if (layer.Visible != null) parts.Add(layer.Visible.Value ? "show" : "hide");
                if (layer.Colour != null) parts.Add(layer.Colour.Value.ToString());

                if (layer.Transparency != null)
                    parts.Add(layer.Transparency.Value.ToString("0%", CultureInfo.InvariantCulture) + " transparent");

                return parts.Count == 0 ? "decides nothing" : string.Join(", ", parts);
            }

            private void Move(Session session, int index, int by)
            {
                var to = index + by;
                if (to < 0 || to >= _layers.Count) return;

                var layer = _layers[index];
                _layers.RemoveAt(index);
                _layers.Insert(to, layer);

                Redraw(session);
            }

            private void BuildAdder(Session session)
            {
                var name = Ui.Text("New layer", null, 170);
                var note = Ui.Text(string.Empty, null, 240);
                var colour = Ui.Text(string.Empty, null, 90);
                var transparency = Ui.Text(string.Empty, null, 70);

                var decides = "leave visibility alone";
                var visibility = Ui.Choice(new[] { "leave visibility alone", "show", "hide" }, decides, v => decides = v);

                var from = "Current selection";
                var library = SetLibrary.FromJson(session.Store.Section(ProjectStore.SetsSection)["items"]);

                var sources = new List<string> { "Current selection", "Everything" };
                sources.AddRange(library.Sets.Select(s => "Set: " + s.Name));

                var source = Ui.Choice(sources, from, v => from = v);

                var add = Ui.Button("Add layer", () =>
                {
                    LayerTarget target;

                    if (from == "Everything")
                    {
                        target = LayerTarget.Everything();
                    }
                    else if (from.StartsWith("Set: ", StringComparison.Ordinal))
                    {
                        var set = library.Sets.FirstOrDefault(
                            s => string.Equals(s.Name, from.Substring(5), StringComparison.Ordinal));

                        if (set == null) return;
                        target = LayerTarget.Set(set.Expression, set.Name);
                    }
                    else
                    {
                        var keys = session.Model.SelectedKeys();

                        if (keys.Count == 0)
                        {
                            _results.Children.Clear();
                            _results.Children.Add(Ui.Line("Nothing is selected.", 0.8));
                            return;
                        }

                        target = LayerTarget.Elements(keys);
                    }

                    var layer = new AppearanceLayer(
                        "layer-" + (_layers.Count + 1).ToString(CultureInfo.InvariantCulture)
                        + "-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture),
                        name.Text.Trim().Length == 0 ? "Layer " + (_layers.Count + 1) : name.Text.Trim(),
                        target)
                    {
                        Note = note.Text.Trim().Length == 0 ? null : note.Text.Trim(),
                        Visible = decides == "show" ? true : decides == "hide" ? (bool?)false : null,
                    };

                    if (Colour.TryParse(colour.Text, out var parsed)) layer.Colour = parsed;

                    if (double.TryParse(transparency.Text, NumberStyles.Float, CultureInfo.InvariantCulture,
                                        out var t) && t >= 0 && t <= 1)
                        layer.Transparency = t;

                    // Top of the stack wins, and a layer you just made is the one you are thinking
                    // about — so it goes on top, which in fold order is the end of the list.
                    _layers.Add(layer);
                    Redraw(session);
                });

                _adder.Children.Add(Ui.Card("Add a layer", null,
                    Ui.Field("Name", name),
                    Ui.Field("Covers", source),
                    Ui.Field("Visibility", visibility),
                    Ui.Field("Colour (#rrggbb)", colour),
                    Ui.Field("Transparency (0-1)", transparency),
                    Ui.Field("Why", note),
                    Ui.Across(add)));
            }
        }

        // ===================================================================================
        // Viewpoints
        // ===================================================================================

        /// <summary>
        /// Bulk rename and renumber, a walkthrough saved from the board, and a delete by name.
        ///
        /// Not re-foldering: moving a saved viewpoint between folders is not something the seam
        /// does, and claiming it here while the code only renames would be the doc lying about the
        /// product. A viewpoint saved into a folder gets there at save time.
        /// </summary>
        private sealed class ViewpointScreen
        {
            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Ui.Scroll(Ui.Stack(Ui.Heading("Viewpoints"), Ui.Sub(Host.NoModel)));

                Redraw(session);

                var pattern = Ui.Text("{n}. {name}", null, 200);
                var start = Ui.Text("1", null, 60);

                var renumber = Ui.Runner("Rename all", _results, job =>
                {
                    var views = session.Viewpoints.All();

                    if (!int.TryParse(start.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        n = 1;

                    var done = 0;

                    foreach (var view in views)
                    {
                        if (!job.Step(done, "renamed")) break;

                        var name = pattern.Text
                            .Replace("{n}", n.ToString("00", CultureInfo.InvariantCulture))
                            .Replace("{name}", view.Name)
                            .Replace("{folder}", view.Folder ?? string.Empty);

                        if (!string.Equals(name, view.Name, StringComparison.Ordinal))
                        {
                            session.Viewpoints.Rename(view.Id, name);
                            done++;
                        }

                        n++;
                    }

                    _results.Children.Add(Ui.Line("Renamed " + Ui.Count(done, "viewpoint") + ".", 1, true));
                    Redraw(session);
                });

                var save = Ui.Button("Save this view", () =>
                {
                    _results.Children.Clear();

                    var view = session.Viewpoints.SaveCurrent(
                        "View " + (session.Viewpoints.All().Count + 1).ToString(CultureInfo.InvariantCulture));

                    _results.Children.Add(Ui.Line("Saved \"" + view.Name + "\".", 0.8));
                    Redraw(session);
                });

                return Ui.Scroll(Ui.Stack(
                    Ui.Heading("Viewpoints"),
                    Ui.Sub("A saved viewpoint carries the overrides that were in force when it was made, so "
                           + "applying one changes what the Appearance stack says is true. The list says which "
                           + "ones do."),
                    Ui.Across(save),
                    Ui.Card("Rename in bulk",
                        "{n} is the number, {name} the current name, {folder} the folder.",
                        Ui.Field("Pattern", pattern),
                        Ui.Field("Start at", start),
                        renumber),
                    Walkthrough(session),
                    _results,
                    Ui.Card("Saved viewpoints", null, _list),
                    Sweep(session)));
            }

            /// <summary>
            /// One viewpoint per group on the board, framed on the group.
            ///
            /// This is the artefact a coordination meeting is run from and the one nobody has time
            /// to make: hand somebody an NWD and they click down the list, and every click puts
            /// them in front of a problem rather than at a coordinate they have to fly to.
            ///
            /// CamelWorks already knows how to frame a group — the BCF viewpoints and the report's
            /// pictures use the same framing, off to one side and above, because a duct through a
            /// beam is invisible in plan. The only thing missing was somewhere to put them.
            /// </summary>
            private UIElement Walkthrough(Session session)
            {
                var folder = Ui.Text("Coordination", null, 180);
                var pattern = Ui.Text("{n}. {group}", null, 200);

                var make = Ui.Runner("Save one per group on the board", _results, job =>
                {
                    var board = CoordinateViews.LastBoard;

                    if (board == null)
                    {
                        _results.Children.Add(Ui.Note("Load the board in Triage first. These viewpoints are "
                            + "of its groups, so there has to be a board to make them from."));
                        return;
                    }

                    var into = folder.Text.Trim();
                    var made = 0;
                    var empty = 0;

                    // The camera is borrowed once for the whole run and put back at the end. Doing
                    // it per group would leave the view visibly jumping for as long as this takes.
                    using (session.View.BorrowCamera())
                    {
                        var n = 1;

                        foreach (var group in board.Groups)
                        {
                            if (!job.Step(made, "viewpoints saved")) break;

                            var points = group.Items.Select(i => (i.X, i.Y, i.Z)).ToList();

                            if (points.Count == 0) { empty++; n++; continue; }

                            job.Say("Framing " + group.Name + "...");
                            session.View.FrameBox(CoordinateViews.BoxOf(points));

                            var name = pattern.Text
                                .Replace("{n}", n.ToString("00", CultureInfo.InvariantCulture))
                                .Replace("{group}", group.Name)
                                .Replace("{who}", group.AssignedTo ?? "unassigned")
                                .Replace("{count}", group.Items.Count.ToString(CultureInfo.InvariantCulture));

                            session.Viewpoints.SaveCurrent(name, into.Length == 0 ? null : into);

                            made++;
                            n++;
                        }
                    }

                    _results.Children.Add(Ui.Line("Saved " + Ui.Count(made, "viewpoint")
                        + (into.Length == 0 ? "." : " into \"" + into + "\"."), 1, true));

                    if (empty > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(empty, "group")
                            + " had no positioned results to frame, so no viewpoint was made for "
                            + (empty == 1 ? "it" : "them") + "."));

                    _results.Children.Add(Ui.Note("Each one carries the overrides that were in force when it "
                        + "was made. If you want them clean, apply the Appearance stack you want first and "
                        + "make them again."));

                    session.Record(ActivityKind.Export, "saved " + Ui.Count(made, "viewpoint")
                                                        + " from the board");
                    Redraw(session);
                });

                return Ui.Card("A viewpoint per issue",
                    "The walkthrough a coordination meeting is actually run from.",
                    Ui.Field("Folder", folder),
                    Ui.Field("Name pattern", pattern),
                    Ui.Note("{n} is the number, {group} the group, {who} the party it is assigned to, "
                            + "{count} how many clashes are in it."),
                    make);
            }

            /// <summary>
            /// Delete every viewpoint whose name starts with something.
            ///
            /// The only bulk delete worth having and the one that has to be hardest to do by
            /// accident. A week of generated walkthroughs is exactly the thing that needs clearing
            /// out before the next one, and doing it one right-click at a time is why people stop
            /// generating them.
            /// </summary>
            private UIElement Sweep(Session session)
            {
                var starts = Ui.Text(string.Empty, null, 200);
                var status = Ui.Stack();

                var count = Ui.Button("Count them", () =>
                {
                    status.Children.Clear();

                    var prefix = starts.Text.Trim();

                    if (prefix.Length == 0)
                    {
                        status.Children.Add(Ui.Problem("Type what their names start with. An empty box would "
                            + "match every viewpoint in the document, which is not something to offer by "
                            + "accident."));
                        return;
                    }

                    var doomed = Matching(session, prefix);

                    if (doomed.Count == 0)
                    {
                        status.Children.Add(Ui.Line("Nothing starts with \"" + prefix + "\".", 0.8));
                        return;
                    }

                    status.Children.Add(Ui.Line(Ui.Count(doomed.Count, "viewpoint") + " start with \""
                        + prefix + "\": " + string.Join(", ", doomed.Take(6).Select(v => v.Name))
                        + (doomed.Count > 6 ? ", ..." : string.Empty), 0.8));

                    status.Children.Add(Ui.Button("Delete " + Ui.Count(doomed.Count, "viewpoint"), () =>
                    {
                        var gone = 0;

                        foreach (var view in doomed)
                        {
                            session.Viewpoints.Delete(view.Id);
                            gone++;
                        }

                        status.Children.Add(Ui.Line("Deleted " + Ui.Count(gone, "viewpoint")
                            + ". Ctrl+Z will not bring them back; save the document only when you are sure.",
                            1, true));

                        session.Record(ActivityKind.Export, "deleted " + Ui.Count(gone, "viewpoint"));
                        Redraw(session);
                    }));
                });

                return Ui.Card("Clear out a batch",
                    "Deletes by name, and counts before it does anything.",
                    Ui.Field("Names starting with", starts),
                    Ui.Across(count),
                    status);
            }

            private static IReadOnlyList<SavedView> Matching(Session session, string prefix) =>
                session.Viewpoints.All()
                    .Where(v => v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            private void Redraw(Session session)
            {
                _list.Children.Clear();

                var views = session.Viewpoints.All();

                if (views.Count == 0)
                {
                    _list.Children.Add(Ui.Empty("None saved.",
                        "Save one from wherever the camera is now."));
                    return;
                }

                var rows = views.Select(v => new TableRow(v,
                    v.Name,
                    v.Folder ?? string.Empty,
                    v.HasOverrides ? "carries overrides" : string.Empty,
                    v.HasRedlines ? "has redlines" : string.Empty)).ToList();

                _list.Children.Add(Ui.Table(rows,
                    ("Viewpoint", 200), ("Folder", 120), ("", 130), ("", 0))
                    .OnPick(item =>
                    {
                        if (item is SavedView view) session.Viewpoints.Apply(view.Id);
                    }));
            }
        }
    }
}
