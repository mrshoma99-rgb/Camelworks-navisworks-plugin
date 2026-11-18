using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;
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
        /// The editor's shape is disjunctive normal form: groups are ORed, conditions inside a
        /// group are ANDed, and any condition can be negated. That is not a simplification of
        /// boolean algebra but all of it, and it is the exact shape the host can run, so what the
        /// user builds and what Navisworks executes are the same structure.
        /// </summary>
        private sealed class SetBuilderScreen
        {
            private readonly List<List<Condition>> _groups = new List<List<Condition>> { new List<Condition>() };
            private readonly StackPanel _editor = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _saved = Ui.Stack();
            private readonly TextBox _name = Ui.Text("New set", null, Ui.Wide.Phrase);

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
                    return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

                Redraw();
                RedrawSaved(session);

                // The expression being built is the working material, so it lives on the stage
                // with its dry run and its live count. The scalar inputs - the name, the bulk
                // ways in - are the setup column, and publishing is the one act at its foot.
                return Bench.Surface(Setup(session), Publish(session), Stage(session));
            }

            /// <summary>
            /// The scalar inputs: what the set is called, and the doors to the bulk tools.
            ///
            /// This screen makes one set from an expression, which is the right tool for the set
            /// somebody is thinking about now. The six disciplines' worth a job starts with arrive
            /// as a spreadsheet and want reading, not retyping — and the whole tree at once wants
            /// its own window — so both are a button here rather than a screen.
            /// </summary>
            private UIElement Setup(Session session)
            {
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
                        ? Ui.Done("Saved.")
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                    RedrawSaved(session);
                });

                var schedule = Ui.Button("From a schedule...", () =>
                {
                    var problem = ToolViews.Open("schedule");
                    if (problem != null) _results.Children.Add(Ui.Problem(problem));
                });

                var reshape = Ui.Button("Rebuild the tree...", () =>
                {
                    var problem = ToolViews.Open("restructure");
                    if (problem != null) _results.Children.Add(Ui.Problem(problem));
                });

                return Ui.Stack(
                    Bench.Group(string.Empty,
                        Ui.Field("Name", _name, "how it reads in the Navisworks selection tree"),
                        save),
                    Bench.Group("Many sets at once",
                        schedule,
                        reshape));
            }

            /// <summary>The one act the tool exists for: the rule, into the selection tree.</summary>
            private UIElement Publish(Session session) =>
                Ui.Runner("Publish as a Navisworks set", _results, job =>
                {
                    var title = _name.Text.Trim();

                    if (title.Length == 0)
                    {
                        _results.Children.Add(Ui.Line("Give the set a name first.", 0.8));
                        return;
                    }

                    job.Say("Searching...");
                    var outcome = session.Search.Publish(title, DnfCompiler.Compile(Expression()));

                    if (outcome.Problem != null)
                    {
                        _results.Children.Add(Ui.Problem(outcome.Problem));
                        return;
                    }

                    _results.Children.Add(Ui.Line(
                        "Published \"" + title + "\" — " + outcome, 1, true));

                    _results.Children.Add(outcome.IsLive
                        ? Ui.Note("Saved as a search set: it re-runs, so elements added later appear in it.")
                        : Ui.Note("Saved as a selection set holding these exact elements. This rule needs more "
                                  + "than one search, and Navisworks search sets can only hold one."));

                    // The set is the input to half the suite, and the screen used to end here —
                    // published into the tree with nothing saying what the week does with it next.
                    // These are the three tabs whose first question is "which set".
                    _results.Children.Add(Ui.Line(
                        "\"" + title + "\" is in the selection tree now, where the rest of the suite "
                        + "can pick it up.", 0.8));

                    _results.Children.Add(Ui.Across(
                        Ui.Button("Colour it...",
                                  () => Ui.Told(Shell.AppWindow.Show("scenes", "appearance"))),
                        Ui.Button("Take it off",
                                  () => Ui.Told(Shell.AppWindow.Show("quantify", "takeoff"))),
                        Ui.Button("Extract it as a model",
                                  () => Ui.Told(Shell.AppWindow.Show("exports", "extract")))));
                });

            /// <summary>
            /// The stage: the rule as it stands, its dry run, and the sets already kept.
            /// </summary>
            private UIElement Stage(Session session)
            {
                var preview = Ui.Runner("Select what it catches", _results, primary: false, work: job =>
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

                return Ui.Stack(
                    Ui.Eyebrow("The rule"),
                    _editor,
                    Ui.Small("Groups are ORed together; conditions inside a group are ANDed. A negated "
                             + "condition compiles to a subtraction rather than a not-equals — those are "
                             + "different sets, because a host condition on a property an element does not "
                             + "have matches nothing."),
                    Ui.Actions(preview),
                    _results,
                    Alike(session),
                    Ui.Eyebrow("Saved sets"),
                    _saved);
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
                            // A reference row whose set has not been picked yet is a half-typed
                            // row, same as a numeric comparison with no number: skipped so the
                            // preview stays usable while the rule is being written.
                            if (condition.SetReference.Trim().Length == 0) continue;

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

                    // Two doors into a group, because the tool's stated job is properties AND other
                    // sets — and until now only Load could produce a set-reference row, so the
                    // second half of the job had no way in from the screen.
                    var add = Ui.Quiet("+ and...", () => { group.Add(new Condition()); Redraw(); });

                    var inSet = Ui.Quiet("+ and in a set...", () =>
                    {
                        group.Add(new Condition { SetReference = string.Empty });
                        Redraw();
                    });

                    _editor.Children.Add(Ui.Across(add, inSet));
                }

                _editor.Children.Add(Ui.Across(
                    Ui.Quiet("+ Add an OR group", () => { _groups.Add(new List<Condition>()); Redraw(); })));
            }

            private UIElement Row(List<Condition> group, Condition condition)
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };

                row.Children.Add(Ui.Check("not", condition.Negated, v => condition.Negated = v));

                if (condition.SetReference != null)
                {
                    // A set-reference row used to render as a property row with its defaults on
                    // show, so a loaded rule that meant "in Ductwork" read as "Element.Category".
                    // It draws as what it is: membership of a set the document already has.
                    row.Children.Add(Ui.Line("in the set", 0.85));
                    row.Children.Add(Pickers.Set(condition.SetReference,
                                                 v => condition.SetReference = v, 220));
                }
                else
                {
                    // The tab and property come out of the model rather than out of memory — the
                    // exact spelling is the difference between a set and an empty set. The value
                    // stays typed: it is often a fragment (Contains "duct") no scan would offer.
                    row.Children.Add(Pickers.Tab(condition.Category, v => condition.Category = v, 110));
                    row.Children.Add(Pickers.Property(condition.Property, v => condition.Property = v, 110));

                    row.Children.Add(Ui.Choice(
                        Enum.GetNames(typeof(SetOperator)),
                        condition.Operator.ToString(),
                        v => { if (Enum.TryParse<SetOperator>(v, out var op)) condition.Operator = op; },
                        150));

                    row.Children.Add(Ui.Text(condition.Value, v => condition.Value = v, Ui.Wide.Word));
                }

                row.Children.Add(Ui.Icon(Glyphs.Close, "Remove this condition",
                                         () => { group.Remove(condition); Redraw(); }));

                foreach (var child in row.Children.OfType<FrameworkElement>())
                    child.Margin = new Thickness(0, 0, 5, 0);

                return row;
            }

            /// <summary>
            /// Everything like the one that is selected.
            ///
            /// The question people ask a model, and the one that sends them into the search dialog
            /// to rebuild by hand what they already have in front of them. The element carries
            /// thirty properties; they want the four that matter and everything matching those.
            /// </summary>
            private UIElement Alike(Session session)
            {
                var facets = Ui.Stack();
                var found = Ui.Stack();
                var ticks = new List<(Facet Facet, CheckBox Box)>();

                var read = Ui.Runner("Read what is selected", facets, primary: false, work: job =>
                {
                    ticks.Clear();

                    IModelItem? picked = null;

                    foreach (var item in session.Model.Traverse(TraversalScope.CurrentSelection))
                    {
                        picked = item;
                        break;
                    }

                    if (picked == null)
                    {
                        facets.Children.Add(Ui.Empty("Nothing is selected.",
                            "Pick one element in the model and read it."));
                        return;
                    }

                    facets.Children.Add(Ui.Line(picked.DisplayName, 1, true));

                    foreach (var facet in Similarity.Facets(picked.Properties()))
                    {
                        var box = Ui.Check(facet.Name + " = " + (facet.Value ?? "(blank)"),
                                           facet.Suggested, null);

                        ticks.Add((facet, box));
                        facets.Children.Add(box);
                    }

                    facets.Children.Add(Ui.Small("Ticked is what has to match. What a thing IS starts "
                                                 + "ticked; what says WHICH one it is does not, because "
                                                 + "matching on that finds only the one you picked."));
                });

                var select = Ui.Runner("Select everything like it", found, primary: false, work: job =>
                {
                    var chosen = ticks.Where(t => t.Box.IsChecked == true).Select(t => t.Facet).ToList();

                    if (chosen.Count == 0)
                    {
                        found.Children.Add(Ui.Note("Nothing is ticked, so nothing would be found."));
                        return;
                    }

                    found.Children.Add(Ui.Line(Similarity.Explain(chosen), 0.85));

                    job.Say("Searching...");
                    var outcome = session.Search.Run(DnfCompiler.Compile(Similarity.Like(chosen)));

                    if (outcome.Count == 0)
                    {
                        found.Children.Add(Ui.Empty("Nothing matched.",
                            "Untick the narrowest of them and try again."));
                        return;
                    }

                    session.Model.Select(outcome.Keys);

                    found.Children.Add(Ui.Line("Selected " + Ui.Count(outcome.Count, "element") + ".", 1, true));
                    found.Children.Add(Ui.Small("Name it and save it to the project if it is worth keeping."));

                    session.Record(ActivityKind.Regroup,
                        "selected " + Ui.Count(outcome.Count, "element") + " like the one picked");
                });

                return Ui.Stack(
                    Ui.Eyebrow("Everything like this one"),
                    Ui.Actions(read),
                    facets,
                    Ui.Actions(select),
                    found);
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

                var table = Ui.Table(rows, ("Set", 160), ("Rule", 0)).OnPick(item =>
                {
                    if (item is SavedSet set) Load(set);
                });

                _saved.Children.Add(table);

                // Clicking a row still opens it in the builder, which is the common act. The verbs
                // are for the rest: a set nobody wants any more used to be a row you could only
                // load, and the way to be rid of it was to edit the project file.
                _saved.Children.Add(Ui.Picking(table,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(picked.OfType<SavedSet>()
                                                     .Select(set => set.Name + "\t" + set.Expression.Describe()),
                                                 say, "set")),
                    new Ui.OnPicked("Delete {n}", (picked, say) =>
                    {
                        say.Children.Clear();

                        var going = picked.OfType<SavedSet>().ToList();

                        if (going.Count == 0) return;

                        say.Children.Add(Ui.Danger("Yes, delete " + Ui.Count(going.Count, "set"), () =>
                        {
                            var held = SetLibrary.FromJson(
                                session.Store.Section(ProjectStore.SetsSection)["items"]);

                            foreach (var set in going) held.Remove(set.Id);

                            session.Store.Section(ProjectStore.SetsSection).Set("items", held.ToJson());

                            RedrawSaved(session);

                            _saved.Children.Add(session.Store.Save()
                                ? Ui.Done("Deleted " + Ui.Count(going.Count, "set") + ".")
                                : Ui.Problem(session.Store.LastSaveProblem
                                             ?? "The project file could not be written."));
                        }));
                    }, danger: true)));
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
            /// <summary>
            /// How many distinct states the table lists before the rest are counted only.
            ///
            /// A model where thousands of elements each carry their own colour is a painted model,
            /// not a coordination problem, and a row each would be a table nobody can read.
            /// </summary>
            private const int StateRowCap = 100;

            /// <summary>
            /// Everywhere one state was applied.
            ///
            /// The table's row. A state is the thing a coordinator recognises — "something is red"
            /// — and where it was applied is what they do about it, so the row is the state and
            /// carries its places. One storey-level override is one place; four thousand elements
            /// coloured one at a time is four thousand, and the row says so instead of becoming
            /// four thousand rows.
            /// </summary>
            private sealed class Places
            {
                internal Places(IReadOnlyList<ScannedOrigin> origins)
                {
                    // Deepest first, so the place shown is the most specific one and adopting the
                    // row builds its layers in the order the stack needs.
                    Origins = origins.OrderByDescending(o => o.Origin.Depth)
                                     .ThenByDescending(o => o.Origin.Covers)
                                     .ToList();
                }

                internal IReadOnlyList<ScannedOrigin> Origins { get; }

                /// <summary>Any of them, since they are the same state by construction.</summary>
                internal AppearanceOrigin Origin => Origins[0].Origin;

                internal int Count => Origins.Count;

                internal int Covers => Origins.Sum(o => o.Origin.Covers);

                internal int Depth => Origins[0].Origin.Depth;

                /// <summary>The place, or how many there are when the state is scattered.</summary>
                internal string Where =>
                    Count == 1 ? Origins[0].Path : Ui.Count(Count, "place");
            }

            /// <summary>What makes two origins the same state. Not where, only what.</summary>
            private static string Shape(AppearanceOrigin origin) =>
                (origin.IsHidden ? "h" : "-")
                + "|" + (origin.Colour == null ? "-" : origin.Colour.Value.ToString())
                + "|" + (origin.Transparency == null
                    ? "-"
                    : origin.Transparency.Value.ToString("F4", CultureInfo.InvariantCulture));

            private readonly List<AppearanceLayer> _layers = new List<AppearanceLayer>();
            private readonly StackPanel _stack = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();
            private readonly StackPanel _foreign = Ui.Stack();
            private readonly StackPanel _travel = Ui.Stack();


            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

                _layers.AddRange(LayerStackJson.Read(session.Store.Section(ProjectStore.LayersSection)["stack"]));

                Redraw(session);

                // The stack is what this tool is, so the stack is the stage: the layers in their
                // precedence, what else is going on that they do not account for, and everything a
                // run reports. Making a layer is setup; putting the stack on the model is the foot.
                return Bench.Surface(
                    Ui.Stack(Adder(session), Travel(session)),
                    Apply(session),
                    Stage(session));
            }

            /// <summary>
            /// The scheme, off this model and onto another one.
            ///
            /// Everything else on this tool is better than the host at deciding what a federation
            /// looks like, and none of it could leave the document. A scheme agreed in a workshop
            /// is an afternoon's work that lived in one person's Navisworks and died with it.
            ///
            /// The identity is tried first and the name second, because a key does not survive a
            /// re-export — and how each element was found is reported, since found-by-name is a
            /// guess. Hidden elements travel only by choice: a colour scheme is a way of reading a
            /// model, a hidden element is a claim that something should not be looked at, and
            /// somebody taking the first rarely wants the second.
            /// </summary>
            private UIElement Travel(Session session)
            {
                var options = new TransferOptions();
                var note = Ui.Text(string.Empty, "what this scheme is", null, Ui.Wide.Phrase);

                var take = Ui.Runner("Take this model's scheme...", _travel, primary: false,
                                     work: job => Take(session, job, note.Text));

                var put = Ui.Runner("Bring a scheme in...", _travel, primary: false,
                                    work: job => Bring(session, job, options));

                return Bench.Group("Somewhere else",
                    Ui.Field("Call it", note),
                    Ui.Check("Bring the colours", options.Colours, on => options.Colours = on),
                    Ui.Check("Bring the transparency", options.Transparency,
                             on => options.Transparency = on),
                    Ui.Check("Bring what was hidden", options.Hidden, on => options.Hidden = on),
                    Ui.Check("Fall back to the element's name", options.ByPath,
                             on => options.ByPath = on),
                    take,
                    put);
            }

            /// <summary>Every element's appearance, read in chunks so Stop means something.</summary>
            private void Take(Session session, Job job, string note)
            {
                _travel.Children.Clear();

                job.Say("Looking through the model...");

                // One walk, as the scan below does, and for the same reason. A scheme is only the
                // elements that carry something, so the tree path — which this needs and the scan
                // does not — is read for those and for nothing else: the walk hands the item over
                // with the reading, so there is nothing to look up again.
                var states = new List<AppearanceState>();
                var paths = new Dictionary<ElementKey, string>();

                foreach (var found in session.View.ScanAppearance(
                             TraversalScope.WholeDocument,
                             seen => job.Step(seen, "elements looked at")))
                {
                    states.Add(found.State);
                    paths[found.Key] = string.Join(AppearanceEntry.PathSeparator, found.Item.TreePath);
                }

                if (job.IsCancelled) return;

                var snapshot = AppearanceTransfer.Capture(states, paths);

                snapshot.SourceModel = session.SavedPath;
                snapshot.WhenTicks = session.NowTicks;
                snapshot.Note = note.Trim().Length == 0 ? null : note.Trim();

                _travel.Children.Add(Ui.Line(snapshot.Explain(), 1, true));

                if (!snapshot.IsUsable)
                {
                    _travel.Children.Add(Ui.Empty("Nothing to take.",
                        "No element in this model carries a colour, a transparency or a hiding."));
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the appearance scheme",
                    Filter = "CamelWorks appearance|*.cwlook",
                    FileName = (snapshot.Note ?? "appearance") + ".cwlook",
                    AddExtension = true,
                    DefaultExt = ".cwlook",
                };

                if (!Ui.Asked(dialog)) return;

                try
                {
                    File.WriteAllText(dialog.FileName, snapshot.ToJson().ToJson());
                }
                catch (Exception e)
                {
                    _travel.Children.Add(Ui.Problem("Could not write it: " + Ui.Describe(e)));
                    return;
                }

                _travel.Children.Add(Ui.Done("Wrote " + dialog.FileName));

                session.Record(ActivityKind.Appearance, "took the appearance scheme: " + snapshot.Explain());
            }

            /// <summary>Read a scheme, find its elements here, and put it on as layers.</summary>
            private void Bring(Session session, Job job, TransferOptions options)
            {
                _travel.Children.Clear();

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Open an appearance scheme",
                    Filter = "CamelWorks appearance|*.cwlook|All files|*.*",
                    CheckFileExists = true,
                };

                if (!Ui.Asked(dialog)) return;

                JsonValue json;

                try
                {
                    json = JsonReader.Parse(File.ReadAllText(dialog.FileName));
                }
                catch (Exception e)
                {
                    _travel.Children.Add(Ui.Problem("Could not read it: " + Ui.Describe(e)));
                    return;
                }

                var refusal = AppearanceSnapshot.Refuses(json);

                if (refusal != null) { _travel.Children.Add(Ui.Problem(refusal)); return; }

                var snapshot = AppearanceSnapshot.FromJson(json);

                _travel.Children.Add(Ui.Line(snapshot.Explain(), 0.8));

                if (snapshot.Note != null) _travel.Children.Add(Ui.Line(snapshot.Note, 0.7));

                job.Say("Looking for those elements here...");

                var here = new List<KeyValuePair<ElementKey, string?>>();

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(here.Count, "elements listed")) return;
                    if (!item.HasGeometry) continue;

                    here.Add(new KeyValuePair<ElementKey, string?>(
                        item.Key, string.Join(AppearanceEntry.PathSeparator, item.TreePath)));
                }

                var match = AppearanceTransfer.Match(snapshot, here, options);

                _travel.Children.Add(Ui.Line(match.Explain(), 1, true));

                if (!match.IsUsable)
                {
                    _travel.Children.Add(Ui.Empty("Nothing to bring in.",
                        "None of that scheme's elements are in this model."));
                    return;
                }

                var made = AppearanceTransfer.Layers(match, options,
                    "transfer-" + session.NowTicks.ToString(CultureInfo.InvariantCulture),
                    snapshot.Note ?? "Brought in");

                if (made.Count == 0)
                {
                    _travel.Children.Add(Ui.Empty("Nothing to bring in.",
                        "Those elements are here, but nothing you ticked applies to any of them."));
                    return;
                }

                // Added to the stack rather than written to the model: it sits in the precedence
                // with everything else, and switching it off puts the model back.
                foreach (var layer in made) _layers.Add(layer);

                Redraw(session);

                _travel.Children.Add(Ui.Done("Added " + Ui.Count(made.Count, "layer")
                    + " to the stack. Nothing has moved yet - press \"Apply the stack\" when the "
                    + "order above is right."));

                session.Record(ActivityKind.Appearance,
                    "brought in an appearance scheme: " + match.Explain());
            }

            /// <summary>
            /// The stage: the stack in its precedence, what no layer accounts for, and where every
            /// run — apply, reset, scan, travel — reports.
            /// </summary>
            private UIElement Stage(Session session)
            {
                var save = Ui.Button("Save the stack", () =>
                {
                    _results.Children.Clear();

                    session.Store.Section(ProjectStore.LayersSection).Set("stack", LayerStackJson.Write(_layers));

                    _results.Children.Add(session.Store.Save()
                        ? Ui.Done("Saved to " + session.Store.Where + ".")
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));
                });

                // The inverse of Apply, and it has to exist: a panel that paints a federation and
                // cannot un-paint it is one people apply once and then work around. It used to be
                // called "Show everything" and only restored visibility, which is a caption
                // promising a reset over a control that does not perform one.
                var reset = Ui.Runner("Put the model back", _results, primary: false, work: job =>
                {
                    job.Say("Resolving layers...");

                    // One resolver for the whole pass, because it caches: ten layers over the same
                    // saved set would otherwise run the same search ten times.
                    var resolver = new HostResolver(session);
                    var all = new HashSet<ElementKey>();

                    foreach (var layer in _layers)
                        foreach (var key in resolver.Resolve(layer.Target))
                            all.Add(key);

                    var covered = all.ToList();

                    job.Say("Reading what the model looks like now...");
                    var current = session.View.ReadAppearance(covered);

                    // Somebody else's colouring stays. Clearing an override CamelWorks did not
                    // author, because it happened to be on an element a layer covers, is how a
                    // tool loses a colleague's afternoon.
                    var foreign = new HashSet<ElementKey>(
                        current.Where(state => state.IsForeign).Select(state => state.Key));

                    var ours = covered.Where(key => !foreign.Contains(key)).ToList();

                    session.View.ClearOverrides(ours);
                    session.View.SetVisible(covered, true);

                    _results.Children.Add(Ui.Done("Took the stack back off "
                                                  + Ui.Count(ours.Count, "element") + "."));

                    if (foreign.Count > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(foreign.Count, "element")
                            + " kept an override CamelWorks did not author. They are visible again "
                            + "but still coloured, as that override belongs to another tool."));
                });

                return Ui.Stack(
                    Ui.Eyebrow("Top of the list wins"),
                    _stack,
                    Ui.Actions(save, reset),
                    _results,
                    Elsewhere(session),
                    _travel);
            }

            /// <summary>The foot: the stack, resolved, planned and put onto the model.</summary>
            private UIElement Apply(Session session) =>
                Ui.Runner("Apply the stack", _results, job =>
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
                            + " carry an override CamelWorks did not author. They are left unchanged; "
                            + "overrides from other tools are never cleared."));

                    if (plan.IsEmpty)
                    {
                        _results.Children.Add(Ui.Empty("Nothing to change.",
                            "The model already looks the way this stack says it should."));
                    }
                    else
                    {
                        job.Say("Applying...");
                        Execute(session, plan);

                        _results.Children.Add(Ui.Done("Applied."));
                        session.Record(ActivityKind.Appearance, "applied the appearance stack: " + plan.Explain());
                    }

                    foreach (var report in fold.Layers.Where(l => l.IsDead))
                        _results.Children.Add(Ui.Note("\"" + report.Layer.Name + "\" covers "
                            + Ui.Count(report.Covers, "element") + " and decides nothing about any of them — "
                            + "every property it sets is overruled by a layer above it."));

                    Explain(session, fold);
                });

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
                var scan = Ui.Runner("Scan the model", _foreign, primary: false, work: job =>
                {
                    job.Say("Resolving layers...");
                    var fold = AppearanceStack.Fold(_layers, new HostResolver(session));

                    var claimed = new HashSet<ElementKey>();

                    foreach (var element in fold.Elements)
                        if (!element.IsUntouched) claimed.Add(element.Key);

                    job.Say("Looking through the model...");

                    // The tree, in and out of every node, rather than along its geometry leaves.
                    //
                    // The host applies an override to a node and every element under it takes it,
                    // so a walk of the leaves reports one act as thousands of rows — and adopting
                    // those made thousands of layers. It is also simply wrong about hiding: hiding
                    // is recorded on the item it was applied to and inherited without being marked,
                    // so a leaf inside a hidden storey reports itself visible and a scan of the
                    // leaves saw a hidden storey as nothing hidden at all.
                    //
                    // IViewSession.ScanOrigins walks the tree and reports each state at the level
                    // it was applied: read, for hiding, and inferred from the subtree for colour,
                    // which the host stores on geometry and keeps no record of the level for.
                    var report = session.View.ScanOrigins(
                        TraversalScope.WholeDocument,
                        seen => job.Step(seen, "elements looked at"));

                    // A stopped scan is not a scan. Reporting what it got to would put a figure on
                    // screen that reads as the model's answer and is the model's answer up to
                    // wherever the user lost patience.
                    if (report.Stopped || job.IsCancelled) return;

                    var ours = report.Origins.Where(o => claimed.Contains(o.Item.Key)).ToList();
                    var theirs = report.Origins.Where(o => !claimed.Contains(o.Item.Key)).ToList();

                    _foreign.Children.Add(Ui.Line(
                        theirs.Count == 0
                            ? report.Elements.ToString("N0", CultureInfo.InvariantCulture)
                              + " elements scanned, nothing on them this stack does not account for"
                            : Ui.Count(theirs.Count, "state") + " no layer accounts for, over "
                              + report.Elements.ToString("N0", CultureInfo.InvariantCulture)
                              + " elements", 1, true));

                    if (theirs.Count == 0)
                    {
                        _foreign.Children.Add(Ui.Empty("Nothing else is going on.",
                            "Every element is either untouched or accounted for by a layer above. "
                            + "That is a real result — the scan ran over the whole federation."));
                        return;
                    }

                    var hidden = theirs.Where(o => o.Origin.IsHidden).Sum(o => o.Origin.Covers);
                    var painted = theirs.Where(o => !o.Origin.IsHidden).Sum(o => o.Origin.Covers);

                    _foreign.Children.Add(Ui.Across(
                        Ui.Figure(hidden.ToString("N0", CultureInfo.InvariantCulture), "hidden",
                                  hidden > 0 ? Tone.Warn : Tone.Plain),
                        Ui.Figure(painted.ToString("N0", CultureInfo.InvariantCulture), "overridden"),
                        Ui.Figure(ours.Count.ToString("N0", CultureInfo.InvariantCulture), "ours already")));

                    // One row per state, not per place, and the difference is a model nobody can
                    // read. A storey-level override is one state in one place; four thousand
                    // elements somebody coloured one at a time is one state in four thousand
                    // places, and a table with a row each would be useless. Grouped, both are one
                    // row that says which it is.
                    //
                    // Deepest place first inside each group, and groups ordered by their deepest,
                    // because that is the order the layers have to go in: a state on a deeper node
                    // must decide its elements before one on a shallower node gets to.
                    var groups = theirs
                        .GroupBy(o => Shape(o.Origin), StringComparer.Ordinal)
                        .Select(g => new Places(g.ToList()))
                        .OrderByDescending(g => g.Depth)
                        .ThenByDescending(g => g.Covers)
                        .ToList();

                    var shown = groups.Take(StateRowCap).ToList();
                    var rest = groups.Skip(StateRowCap).ToList();

                    var rows = shown.Select(g => new TableRow(g,
                        g.Where,
                        g.Origin.Describe(),
                        g.Covers.ToString("N0", CultureInfo.InvariantCulture),
                        g.Origin.IsHidden
                            ? "hidden there, so everything under it is"
                            : g.Covers > g.Count ? "everything under there is" : string.Empty)
                    {
                        Tone = g.Origin.IsHidden ? Tone.Warn : Tone.Plain,
                    }).ToList();

                    var table = Ui.Table(rows, ("Where", 240), ("State", 150), ("Elements", 80), ("", 0));

                    _foreign.Children.Add(table);

                    // The verbs are on the rows. "Pick a row, then choose what to do with it" was
                    // a table and a separate row of buttons that did not say what they were aimed
                    // at, and nothing there worked on more than one state at a time.
                    _foreign.Children.Add(Ui.Picking(table,
                        new Ui.OnPicked("Select {n}", (picked, say) =>
                        {
                            // The nodes themselves. Selecting a branch selects what is under it,
                            // which is both what the user means and one call instead of thousands.
                            var keys = picked.OfType<Places>()
                                             .SelectMany(g => g.Origins)
                                             .Select(o => o.Item.Key)
                                             .ToList();

                            session.Model.Select(keys);
                            say.Children.Add(Ui.Line("Selected " + Ui.Count(keys.Count, "place") + ".", 0.8));
                        }),
                        new Ui.OnPicked("Make {n} a layer", (picked, say) => Adopt(session, picked, say)),
                        new Ui.OnPicked("Put {n} back", (picked, say) => PutBack(session, picked, say))));

                    if (rest.Count > 0)
                        _foreign.Children.Add(Ui.Note(
                            Ui.Count(rest.Count, "further state") + " covering "
                            + Ui.Count(rest.Sum(g => g.Covers), "element")
                            + " are counted above but not listed. A model where thousands of "
                            + "elements each carry their own colour is a painted model, not a "
                            + "coordination problem, and a table of them would be unreadable."));

                    if (theirs.Any(o => !o.Origin.IsRead))
                        _foreign.Children.Add(Ui.Note(
                            "Where a colour or a transparency was applied is not something the host "
                            + "records: it stores them on the geometry, so an override put on a "
                            + "storey is pushed down to every element under it and the storey keeps "
                            + "no note of it. The place shown for one of those is the highest node "
                            + "whose every element is in that one state, which is what a "
                            + "storey-level override looks like afterwards. Hiding is different "
                            + "— the host does record where that was done, and those rows are read "
                            + "rather than worked out."));
                });

                return Ui.Stack(
                    Ui.Eyebrow("What else is going on"),
                    scan,
                    _foreign);
            }

            /// <summary>
            /// Turn each picked place into a layer of this model's own stack.
            ///
            /// One layer per place, naming the node — not one per element that ended up in the
            /// state, which on a storey-level override was thousands of layers and the reason the
            /// layer list stopped being readable.
            /// </summary>
            private void Adopt(Session session, IReadOnlyList<object> picked, Panel say)
            {
                // Shallowest first, because the stack is held bottom-first and the last layer in
                // it wins. The scan hands them over deepest first, which is the reading order;
                // this is the applying order, and they are opposites. Get it the wrong way round
                // and a storey-wide colour sits above the one somebody put on a branch inside it,
                // which makes the more specific layer cover elements and decide nothing about any
                // of them - what the stack reports as a dead layer.
                var ordered = picked.OfType<Places>()
                                    .OrderBy(g => g.Depth)
                                    .ToList();

                var made = 0;

                foreach (var group in ordered)
                {
                    // One layer per state, naming every place it was applied. A group of four
                    // thousand individually coloured elements is one layer with four thousand
                    // nodes in it, not four thousand layers - which is the whole complaint this
                    // answers.
                    var layer = group.Origin.AsLayer(
                        group.Origins.Select(o => o.Item.Key).ToList(),
                        "adopted-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)
                        + "-" + made.ToString(CultureInfo.InvariantCulture),
                        group.Where);

                    _layers.Add(layer);
                    made++;

                    session.Record(ActivityKind.Appearance, "adopted " + layer.Name);
                }

                if (made == 0) return;

                Redraw(session);

                say.Children.Add(Ui.Line(Ui.Count(made, "layer") + " is now on top of the stack"
                    + (made > 1 ? ", the deepest place highest" : string.Empty)
                    + ". The model looks exactly as it did — what changed is that the stack now "
                    + "says why.", 0.8));
            }

            /// <summary>Clear somebody else's overrides off each picked place.</summary>
            private void PutBack(Session session, IReadOnlyList<object> picked, Panel say)
            {
                var places = 0;
                var elements = 0;

                foreach (var group in picked.OfType<Places>())
                {
                    // The nodes, not their descendants. The host clears an override on a node by
                    // pushing the clear down, exactly as it pushed the override down, so this is
                    // one call per state rather than one per element - and for a hiding it is the
                    // only call that works, because the hiding is recorded on the node and nothing
                    // below it is marked at all.
                    var keys = group.Origins.Select(o => o.Item.Key).ToList();

                    if (group.Origin.IsHidden) session.View.SetVisible(keys, true);
                    else session.View.ClearOverrides(keys);

                    places += keys.Count;
                    elements += group.Covers;
                }

                if (places == 0) return;

                say.Children.Add(Ui.Line("Reset " + Ui.Count(places, "place") + ", covering "
                    + Ui.Count(elements, "element") + ". Scan again to see what is left.", 0.8));

                session.Record(ActivityKind.Appearance,
                    "reset " + Ui.Count(places, "place") + " overridden by another tool");
            }

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

                _results.Children.Add(Ui.Eyebrow("The selected element"));
                _results.Children.Add(Ui.Line(appearance.Explain(), 0.85));
            }

            private void Redraw(Session session)
            {
                _stack.Children.Clear();

                if (_layers.Count == 0)
                {
                    // The first question on landing is not "what do I add" but "what am I looking
                    // at" — a federation is nearly always already coloured or part-hidden by
                    // somebody. The scan below is the answer, so the empty stack points at it.
                    _stack.Children.Add(Ui.Empty("No layers yet, so CamelWorks is deciding nothing "
                        + "about how the model looks.",
                        "What you see now is the host's state — isolates, colours and hidden elements "
                        + "already applied. Press \"Scan the model\" below to list them, or add a "
                        + "layer from a set or the current selection."));
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
                    row.Children.Add(Swatch(layer));

                    row.Children.Add(Ui.Line(layer.Name, 1, true));
                    row.Children.Add(Ui.Pill(layer.Target.ReResolves ? "rule" : "fixed",
                        layer.Target.ReResolves ? Tone.Good : Tone.Plain));
                    row.Children.Add(Ui.Line(layer.Target.Description, 0.7));
                    row.Children.Add(Ui.Line(Effects(layer), 0.85));

                    var up = Ui.Icon(Glyphs.Up, "Move this layer up, so it wins over the ones below",
                                     () => Move(session, index, 1));

                    var down = Ui.Icon(Glyphs.Down, "Move this layer down",
                                       () => Move(session, index, -1));

                    var drop = Ui.Icon(Glyphs.Bin, "Remove this layer",
                                       () => { _layers.RemoveAt(index); Redraw(session); });

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

            /// <summary>
            /// The colour this layer puts on the model, as a chip.
            ///
            /// The literal colour, never a Tone. A layers panel that paints a federation and shows
            /// no colour anywhere is asking somebody to remember what "Level 04 only" is set to —
            /// and a chip drawn from the theme's own red would be a preview of the pane rather
            /// than of the model, and would change when somebody switched to dark.
            /// </summary>
            /// <param name="layer">The layer.</param>
            private static UIElement Swatch(AppearanceLayer layer)
            {
                var chip = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Center,
                    BorderThickness = new Thickness(1),
                };

                chip.Tint(Border.BorderBrushProperty, "BdStrong");

                if (layer.Colour.HasValue)
                {
                    var colour = layer.Colour.Value;

                    chip.Background = new SolidColorBrush(
                        Color.FromRgb(colour.R, colour.G, colour.B));

                    return chip;
                }

                // No colour of its own. Left as the outline, so a layer that only hides or only
                // fades does not claim a colour it never writes.
                chip.Tint(Border.BackgroundProperty, "Track");
                return chip;
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

            /// <summary>A new layer: hidden or coloured, over whatever it is pointed at.</summary>
            private UIElement Adder(Session session)
            {
                var name = Ui.Text("New layer", null, Ui.Wide.Name);
                var note = Ui.Text(string.Empty, null, Ui.Wide.Phrase);
                var colour = Ui.Text(string.Empty, null, Ui.Wide.Number);
                var transparency = Ui.Text(string.Empty, null, Ui.Wide.Tight);

                var decides = "leave visibility alone";
                var visibility = Ui.Choice(new[] { "leave visibility alone", "show", "hide" }, decides, v => decides = v);

                var from = "Current selection";
                var library = SetLibrary.FromJson(session.Store.Section(ProjectStore.SetsSection)["items"]);

                var sources = new List<string> { "Current selection", "Everything" };
                sources.AddRange(library.Sets.Select(s => "Set: " + s.Name));

                // The document's own sets too, whatever tool made them. A coordinator's sets
                // mostly live in the Navisworks tree, not in the project library, and a layer
                // that could not cover them sent people back to select-by-hand.
                try
                {
                    var taken = new HashSet<string>(library.Sets.Select(s => s.Name),
                                                    StringComparer.OrdinalIgnoreCase);

                    sources.AddRange(session.Model.Sets()
                        .Select(s => s.DisplayName)
                        .Where(n => !string.IsNullOrWhiteSpace(n) && !taken.Contains(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Select(n => "Navisworks set: " + n));
                }
                catch (Exception)
                {
                    // A host that cannot list its sets leaves the choice as it was; the project
                    // library and the selection still work, and that is not worth an error.
                }

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
                    else if (from.StartsWith("Navisworks set: ", StringComparison.Ordinal))
                    {
                        // A rule target by reference, so the layer follows the set: republish
                        // the set with more elements in it and the layer covers them too.
                        var name = from.Substring("Navisworks set: ".Length);
                        target = LayerTarget.Set(SetExpression.InSet(name, name), name);
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

                return Bench.Group("Add a layer",
                    Ui.Field("Name", name),
                    Ui.Field("Covers", source),
                    Ui.Field("Visibility", visibility),
                    Ui.Field("Colour (#rrggbb)", colour),
                    Ui.Field("Transparency (0-1)", transparency),
                    Ui.Field("Why", note),
                    add);
            }
        }

        // ===================================================================================
        // Viewpoints
        // ===================================================================================

        /// <summary>
        /// The saved viewpoints, and the five things worth doing to a run of them at once.
        ///
        /// It used to be seven cards, three of which asked you to type what the names started
        /// with — which is a selection mechanism invented to work around a list that could not be
        /// selected in. The list can be selected in now, so the verbs act on what is ticked and
        /// three text boxes and a "count them" step disappear.
        /// </summary>
        private sealed class ViewpointScreen
        {
            private readonly StackPanel _list = Ui.Stack();
            private readonly StackPanel _results = Ui.Stack();

            /// <summary>The table itself, so the verbs below it can read what is ticked.</summary>
            private ListView? _table;

            /// <summary>
            /// Where a batch says what it did.
            ///
            /// The batches live in the configuration window now, and a run reporting onto a stage
            /// behind a modal is a run nobody sees. So each page hands its own panel over before
            /// it builds the group, and the runner writes where the person pressing it is looking.
            /// </summary>
            private StackPanel _say = Ui.Stack();

            private string _size = "1920 x 1080";

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null)
                    return Bench.Surface(null, null, Ui.Stack(Ui.Empty("No model is open.", Host.NoModel)));

                Redraw(session);

                var save = Ui.Runner("Save this view", _results, primary: true, work: _ =>
                {
                    var view = session.Viewpoints.SaveCurrent(
                        "View " + (session.Viewpoints.All().Count + 1).ToString(CultureInfo.InvariantCulture));

                    if (view == null)
                    {
                        _results.Children.Add(Ui.Problem(session.Viewpoints.LastProblem
                            ?? "The document would not take the viewpoint."));

                        return;
                    }

                    _results.Children.Add(Ui.Done("Saved \"" + view.Value.Name + "\"."));
                    Redraw(session);
                });

                // The table is the whole tab: what the document holds, and what every batch acts
                // on. The six batches were six cards down the setup column, which made a screen
                // whose subject is a list of viewpoints four fifths a form - so each is a symbol
                // at the foot, opening on its own page, and the table gets the room.
                var generate = Generate(session);

                var foot = new WrapPanel();

                foot.Children.Add(save);
                foot.Children.Add(new Border { Width = 14 });

                void Batch(string glyph, string tip, string section)
                {
                    foot.Children.Add(Ui.Icon(glyph, tip, () => Batches(session, section)));
                }

                Batch(Glyphs.Pencil, "Rename them", "rename");
                Batch(Glyphs.Folder, "File them into a folder", "folder");
                Batch(Glyphs.Camera, "Render them to images", "render");
                Batch(Glyphs.Theme, "Copy overrides onto the rest", "overrides");
                Batch(Glyphs.Issue, "A viewpoint per issue", "issues");
                Batch(Glyphs.Bin, "Delete them", "delete");

                return Bench.Surface(
                    null,
                    foot,
                    Ui.Stack(
                        _list,
                        Ui.Small("Tick the ones to act on. With nothing ticked, the batches act on "
                                 + "all of them - except deleting, which never does."),
                        _results,
                        Ui.Rule(),
                        Ui.Eyebrow("Make one for each element"),
                        generate.Setup,
                        generate.Stage));
            }

            /// <summary>
            /// The six batches, each on its own page, opened on the one whose symbol was pressed.
            ///
            /// Every page hands the group a fresh panel to report into before building it, so a
            /// rename that says how many it renamed says it where the button was, rather than on
            /// a stage the window is covering.
            /// </summary>
            private void Batches(Session session, string section)
            {
                UIElement Page(Func<Session, UIElement> build)
                {
                    var say = Ui.Stack();

                    _say = say;

                    return Ui.Stack(build(session), say);
                }

                var pages = new List<Ui.Page>
                {
                    new Ui.Page("Rename them", () => Page(Rename)),
                    new Ui.Page("File them into a folder", () => Page(Refolder)),
                    new Ui.Page("Render them to images", () => Page(Render)),
                    new Ui.Page("Copy overrides onto the rest", () => Page(CopyOverrides)),
                    new Ui.Page("A viewpoint per issue", () => Page(Walkthrough)),
                    new Ui.Page("Delete them", () => Page(Sweep)),
                };

                var order = new[] { "rename", "folder", "render", "overrides", "issues", "delete" };
                var at = Math.Max(0, Array.IndexOf(order, section));

                if (at > 0)
                {
                    var wanted = pages[at];
                    pages.RemoveAt(at);
                    pages.Insert(0, wanted);
                }

                Ui.Configure("Viewpoints", () => Redraw(session), pages.ToArray());
            }

            /// <summary>
            /// One viewpoint per element in the selection.
            ///
            /// This tab could rename, refile and delete viewpoints, and could not make one. That is
            /// backwards: making them is the forty minutes of clicking, framing and typing that
            /// nobody does, which is why a review is one person driving and everybody else watching.
            /// Renaming forty viewpoints is a nuisance; not having them is the reason there is
            /// nothing to rename.
            ///
            /// Split across the surface: the recipe's inputs are a setup group, and the two-stage
            /// commit — show what it would make, then make it — leads its part of the stage,
            /// because the list of names it proposes is a result.
            /// </summary>
            private (UIElement Setup, UIElement Stage) Generate(Session session)
            {
                var recipe = new ViewpointRecipe();

                var pattern = Ui.Text(recipe.Pattern, text => recipe.Pattern = text, Ui.Wide.Phrase);
                var folder = Ui.Text(string.Empty, text => recipe.Folder = text, Ui.Wide.Word);

                var start = Ui.Text("1", text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        recipe.Start = n;
                }, 70);

                var padding = Ui.Text("3", text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        recipe.Padding = n;
                }, 70);

                var margin = Ui.Text("2", text =>
                {
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                        recipe.Margin = m;
                }, 70);

                var from = Ui.Choice(Enum.GetNames(typeof(ViewFrom)), recipe.From.ToString(),
                    pick => { if (Enum.TryParse<ViewFrom>(pick, out var v)) recipe.From = v; }, 130);

                var section = Ui.Check("Cut a section just in front of each one", false,
                                       v => recipe.Section = v);

                ViewpointRun? run = null;

                var make = Ui.Commit("Show me what it would make", "Make", "viewpoint",
                    plan: (job, into) =>
                    {
                        run = ViewpointPlanner.Plan(Selected(session, job), recipe,
                                                    session.Viewpoints.All().Select(v => v.Name).ToList());

                        into.Children.Add(Ui.Line(run.Explain(), 1, true));

                        foreach (var why in run.Skipped.Take(5)) into.Children.Add(Ui.Note(why));

                        if (run.Views.Count > 0)
                        {
                            var rows = run.Views.Take(200)
                                .Select(v => new TableRow(v, v.Name))
                                .ToList();

                            var named = Ui.Table(rows, ("It would be called", 0));

                            into.Children.Add(Ui.Picking(named,
                                new Ui.OnPicked("Copy {n}",
                                    (picked, say) => Ui.Copy(Ui.Lines(named, picked), say, "viewpoint"))));
                        }

                        return run.Views.Count;
                    },
                    apply: job => Capture(session, run, recipe, job));

                return (
                    Bench.Group("Make one for each element",
                        Ui.Field("Name them", pattern, "{n} is the number, {name} is the element's own name."),
                        Ui.Field("Start at", start),
                        // "Least digits", not "Digits": the field is a minimum width, so 3
                        // gives 001 and leaves 200 as 200. Labelled "Digits" it read as a
                        // truncation the preview then contradicted.
                        Ui.Field("Least digits", padding),
                        Ui.Field("Look from", from),
                        Ui.Field("Room around (m)", margin),
                        Ui.Field("Put them in folder", folder),
                        section),
                    Ui.Stack(
                        Ui.Eyebrow("Make one for each element"),
                        make));
            }

            /// <summary>What is selected, with each element's own name, in tree order.</summary>
            private static IReadOnlyList<KeyValuePair<ElementKey, string?>> Selected(Session session, Job job)
            {
                var picked = new List<KeyValuePair<ElementKey, string?>>();
                var read = 0;

                foreach (var item in session.Model.Traverse(TraversalScope.CurrentSelection))
                {
                    if (!job.Step(read++, "elements read")) break;
                    if (!item.HasGeometry) continue;

                    picked.Add(new KeyValuePair<ElementKey, string?>(item.Key, item.DisplayName));
                }

                return picked;
            }

            /// <summary>
            /// Frame each element and save the view.
            ///
            /// The camera is borrowed and put back. Somebody who runs this has a view they were
            /// working from, and a tool that makes forty viewpoints and leaves them looking at the
            /// fortieth has taken something from them to do it.
            /// </summary>
            private void Capture(Session session, ViewpointRun? run, ViewpointRecipe recipe, Job job)
            {
                if (run == null || run.IsEmpty) return;

                var made = 0;
                var failed = 0;
                var refused = 0;

                using (session.View.BorrowCamera())
                {
                    foreach (var view in run.Views)
                    {
                        if (!job.Step(made + failed, run.Views.Count, "viewpoints made")) break;

                        var item = session.Model.Resolve(view.Key);

                        if (item == null) { failed++; continue; }

                        var one = new[] { view.Key };

                        if (recipe.From == ViewFrom.Above) session.View.LookDown(item.Bounds);
                        else session.View.ZoomTo(one, recipe.Margin);

                        if (recipe.Section) session.View.SetSectionBox(item.Bounds, recipe.SectionOffset);

                        if (session.Viewpoints.SaveCurrent(view.Name,
                                string.IsNullOrWhiteSpace(recipe.Folder) ? null : recipe.Folder) == null)
                        {
                            refused++;
                            continue;
                        }

                        made++;
                    }

                    if (recipe.Section) session.View.SetSectionBoxEnabled(false);
                }

                _results.Children.Add(Ui.Line("Made " + Ui.Count(made, "viewpoint") + ".", 1, true));

                if (refused > 0)
                    _results.Children.Add(Ui.Problem(session.Viewpoints.LastProblem
                        ?? "The document would not take " + Ui.Count(refused, "viewpoint") + "."));

                if (failed > 0)
                    _results.Children.Add(Ui.Note(Ui.Count(failed, "element")
                        + " stopped resolving between the plan and the capture, so they were skipped."));

                session.Record(ActivityKind.Regroup, "made " + Ui.Count(made, "viewpoint") + " from the selection");
            }

            /// <summary>
            /// The viewpoints the verbs act on: the ticked ones, or all of them.
            ///
            /// Falling back to all of them is right for four of the five verbs — renaming or
            /// filing a whole document's worth is the common case — and wrong for the fifth, so
            /// deleting asks for the ticks itself rather than taking this answer.
            /// </summary>
            private IReadOnlyList<SavedView> Chosen(Session session)
            {
                var ticked = Ticked();

                return ticked.Count > 0 ? ticked : session.Viewpoints.All();
            }

            /// <summary>Only what is ticked, which may be nothing.</summary>
            private IReadOnlyList<SavedView> Ticked() =>
                _table == null
                    ? Array.Empty<SavedView>()
                    : _table.PickedMany().OfType<SavedView>().ToList();

            /// <summary>Renaming and renumbering, in one pattern.</summary>
            private UIElement Rename(Session session)
            {
                var pattern = Ui.Text("{n}. {name}", null, Ui.Wide.Name);
                var start = Ui.Text("1", null, Ui.Wide.Tight);

                var run = Ui.Runner("Rename them", _say, primary: false, work: job =>
                {
                    var views = Chosen(session);

                    if (!int.TryParse(start.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        n = 1;

                    var done = 0;
                    var refused = 0;

                    foreach (var view in views)
                    {
                        if (!job.Step(done, views.Count, "renamed")) break;

                        var name = pattern.Text
                            .Replace("{n}", n.ToString("00", CultureInfo.InvariantCulture))
                            .Replace("{name}", view.Name)
                            .Replace("{folder}", view.Folder ?? string.Empty);

                        if (!string.Equals(name, view.Name, StringComparison.Ordinal))
                        {
                            if (session.Viewpoints.Rename(view.Id, name)) done++;
                            else refused++;
                        }

                        n++;
                    }

                    _say.Children.Add(Ui.Done("Renamed " + Ui.Count(done, "viewpoint") + "."));

                    if (refused > 0)
                        _say.Children.Add(Ui.Problem(session.Viewpoints.LastProblem
                            ?? "The document would not take " + Ui.Count(refused, "viewpoint") + "."));

                    Redraw(session);
                });

                return Bench.Group("Rename them",
                    Ui.Field("Pattern", pattern, "{n} the number, {name} the current name, "
                                                 + "{folder} the folder"),
                    Ui.Field("Start at", start),
                    run);
            }

            /// <summary>
            /// Move viewpoints into a folder, in bulk.
            ///
            /// The host has folders in the Saved Viewpoints panel and gives you one way to use
            /// them: drag, one viewpoint at a time. Forty viewpoints saved out of a clash run land
            /// flat in the root, and filing them is forty drags nobody does — so they stay in the
            /// root, and the next run's forty land on top of them.
            /// </summary>
            private UIElement Refolder(Session session)
            {
                var into = Ui.Text("Coordination", null, Ui.Wide.Name);

                var move = Ui.Runner("Move them", _say, primary: false, work: job =>
                {
                    var wanted = into.Text.Trim();
                    var moving = Chosen(session);

                    var moved = 0;
                    var already = 0;
                    var refused = 0;

                    foreach (var view in moving)
                    {
                        if (!job.Step(moved, moving.Count, "moved")) break;

                        // Already there is not a move and not a failure. Counting it as either
                        // would make a re-run of the same sweep read as though it had done work.
                        if (string.Equals(view.Folder ?? string.Empty, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            already++;
                            continue;
                        }

                        if (session.Viewpoints.MoveTo(view.Id, wanted.Length == 0 ? null : wanted) == null)
                            refused++;
                        else
                            moved++;
                    }

                    _say.Children.Add(Ui.Done("Moved " + Ui.Count(moved, "viewpoint")
                        + (wanted.Length == 0 ? " to the top level." : " into \"" + wanted + "\".")));

                    if (already > 0)
                        _say.Children.Add(Ui.Line(Ui.Count(already, "viewpoint")
                            + " were already there and were left alone.", 0.7));

                    if (refused > 0)
                        _say.Children.Add(Ui.Note(Ui.Count(refused, "viewpoint")
                            + " could not be moved. A viewpoint the host will not take out of its folder "
                            + "keeps its old place rather than being lost, so nothing has gone missing."));

                    session.Record(ActivityKind.Export, "filed " + Ui.Count(moved, "viewpoint"));
                    Redraw(session);
                });

                return Bench.Group("File them into a folder",
                    Ui.Field("Into folder", into, "made if it is not there; one already in "
                                                  + "it is left where it is"),
                    move);
            }

            /// <summary>
            /// Render viewpoints to PNGs, in one pass.
            ///
            /// The thing a coordination pack is made of, and the reason people still take
            /// screenshots one at a time with the snipping tool. The host renders a viewpoint
            /// off-screen perfectly well and has never offered to do it for all of them.
            ///
            /// The camera is borrowed for the whole run and put back at the end, so a batch over
            /// eighty viewpoints does not leave somebody looking at the eightieth.
            /// </summary>
            private UIElement Render(Session session)
            {
                var into = Ui.Text(SuggestedRenderFolder(session), null, Ui.Wide.Path);
                var size = Ui.Choice(new[] { "1280 x 720", "1920 x 1080", "2560 x 1440" }, "1920 x 1080",
                                     pick => _size = pick);

                var render = Ui.Runner("Render them", _say, primary: false, work: job =>
                {
                    var views = Chosen(session);
                    var folder = into.Text.Trim();

                    if (folder.Length == 0)
                    {
                        folder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            Deliverable.Safe(session.Profile.ProjectName + " viewpoints"));

                        into.Text = folder;
                    }

                    try { Directory.CreateDirectory(folder); }
                    catch (Exception e)
                    {
                        _say.Children.Add(Ui.Problem("Could not make that folder: " + Ui.Describe(e)));
                        return;
                    }

                    var (width, height) = Size(_size);

                    var written = 0;
                    var blank = 0;
                    var failed = new List<string>();
                    var n = 1;

                    using (session.View.BorrowCamera())
                    {
                        foreach (var view in views)
                        {
                            if (!job.Step(written, views.Count, "rendered")) break;

                            job.Say("Rendering " + view.Name + "...");

                            try
                            {
                                session.Viewpoints.Apply(view.Id);

                                var png = session.View.Capture(width, height);

                                // Null is the documented answer from a machine that cannot render
                                // off-screen. Writing a zero-byte PNG so the count looks right
                                // would hand somebody a pack of broken files.
                                if (png == null || png.Length == 0) { blank++; n++; continue; }

                                var name = n.ToString("000", CultureInfo.InvariantCulture)
                                         + " - " + Deliverable.Safe(view.Name) + ".png";

                                File.WriteAllBytes(Path.Combine(folder, name), png);
                                written++;
                            }
                            catch (Exception e)
                            {
                                failed.Add(view.Name + " - " + Ui.Describe(e));
                            }

                            n++;
                        }
                    }

                    _say.Children.Add(Ui.Done("Wrote " + Ui.Count(written, "image") + " to " + folder + "."));

                    if (blank > 0)
                        _say.Children.Add(Ui.Note(Ui.Count(blank, "viewpoint")
                            + " produced no image. Off-screen rendering is not available in every "
                            + "configuration - a machine with no graphics context, a document still "
                            + "loading. Nothing was written for those rather than an empty file."));

                    foreach (var problem in failed.Take(6)) _say.Children.Add(Ui.Problem(problem));

                    session.Record(ActivityKind.Export, "rendered " + Ui.Count(written, "viewpoint"));
                });

                return Bench.Group("Render them to images",
                    Ui.PathField("Write into", into, PathKind.Folder,
                                 hint: "suggested beside the model, so the pack lands where the "
                                       + "job lives"),
                    Ui.Field("Size", size),
                    render,
                    Ui.Small("The camera goes back where it was when the run finishes."));
            }

            /// <summary>
            /// A render folder beside the document, or in the user's own documents.
            ///
            /// The box used to open blank and the fallback only revealed itself after the run —
            /// which is a decision the tool can state up front instead of taking silently. Same
            /// reasoning as the export paths: the person collecting the pack looks next to the
            /// model first.
            /// </summary>
            private static string SuggestedRenderFolder(Session session)
            {
                var name = Deliverable.Safe(session.Profile.ProjectName + " viewpoints");

                try
                {
                    var saved = session.SavedPath;

                    if (!string.IsNullOrWhiteSpace(saved))
                    {
                        var folder = Path.GetDirectoryName(saved);

                        if (!string.IsNullOrEmpty(folder)) return Path.Combine(folder!, name);
                    }

                    var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    return string.IsNullOrEmpty(documents) ? name : Path.Combine(documents, name);
                }
                catch (Exception)
                {
                    // A profile or path the host will not give up still leaves a usable relative
                    // name; the run's own CreateDirectory says so if even that cannot be made.
                    return name;
                }
            }

            /// <summary>The chosen render size.</summary>
            private static (int Width, int Height) Size(string choice) => choice switch
            {
                "1280 x 720" => (1280, 720),
                "2560 x 1440" => (2560, 1440),
                _ => (1920, 1080),
            };

            /// <summary>
            /// Give a run of viewpoints the overrides one of them carries.
            ///
            /// Somebody sets up the colours once — services by system, structure greyed back — and
            /// then has forty viewpoints that do not have them, because the overrides were saved
            /// into whichever one happened to be current at the time.
            ///
            /// The cost is stated where the button is and not buried: the host's only way to
            /// rewrite a saved viewpoint replaces its redlines too, so a viewpoint somebody drew on
            /// loses the drawing. There is no call that does one without the other.
            /// </summary>
            private UIElement CopyOverrides(Session session)
            {
                var views = session.Viewpoints.All();

                if (views.Count < 2) return Ui.Stack();

                var names = views.Select(v => v.ToString()).ToList();

                var from = names[0];
                var source = Ui.Choice(names, from, pick => from = pick, Ui.Wide.Phrase);

                var copy = Ui.Runner("Copy them across", _say, primary: false, work: job =>
                {
                    var chosen = session.Viewpoints.All()
                        .FirstOrDefault(v => string.Equals(v.ToString(), from, StringComparison.Ordinal));

                    if (string.IsNullOrEmpty(chosen.Id))
                    {
                        _say.Children.Add(Ui.Problem("That viewpoint is no longer in the document."));
                        return;
                    }

                    var targets = Chosen(session)
                        .Where(v => !string.Equals(v.Id, chosen.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (targets.Count == 0)
                    {
                        _say.Children.Add(Ui.Line("There is nothing else to put them on.", 0.8));
                        return;
                    }

                    var done = 0;
                    var refused = 0;

                    using (session.View.BorrowCamera())
                    {
                        foreach (var target in targets)
                        {
                            if (!job.Step(done, targets.Count, "copied")) break;

                            if (session.Viewpoints.CopyOverrides(chosen.Id, target.Id)) done++;
                            else refused++;
                        }
                    }

                    _say.Children.Add(Ui.Done("Copied the overrides onto " + Ui.Count(done, "viewpoint")
                        + "."));

                    if (refused > 0)
                        _say.Children.Add(Ui.Note(Ui.Count(refused, "viewpoint")
                            + " were left exactly as they were, because the host would not rewrite them. "
                            + "A refusal costs nothing: none of them has been changed."));

                    session.Record(ActivityKind.Export,
                        "copied viewpoint overrides onto " + Ui.Count(done, "viewpoint"));

                    Redraw(session);
                });

                return Bench.Group("Copy overrides onto the rest",
                    Ui.Field("Take them from", source),
                    copy,
                    Ui.Note("Rewriting a saved viewpoint replaces its redlines with the source's. That is "
                            + "the host's own behaviour and there is no call that avoids it, so a viewpoint "
                            + "any redlines on the target are lost. Nothing else about the target changes: "
                            + "not its name, its folder or its camera."));
            }

            /// <summary>
            /// Delete a run of viewpoints.
            ///
            /// The only bulk delete worth having and the one that has to be hardest to do by
            /// accident. A week of generated walkthroughs is exactly the thing that needs clearing
            /// out before the next one, and doing it one right-click at a time is why people stop
            /// generating them. It is the one verb here that will not fall back to "all of them":
            /// an empty selection deleting the document's viewpoints is not a thing to offer.
            /// </summary>
            private UIElement Sweep(Session session)
            {
                var status = Ui.Stack();

                var ask = Ui.Danger("Delete", () =>
                {
                    status.Children.Clear();

                    var doomed = Ticked();

                    if (doomed.Count == 0)
                    {
                        status.Children.Add(Ui.Problem("Nothing is ticked. Deleting will not fall back "
                            + "to every viewpoint in the document, which is what the other verbs here "
                            + "do and the one thing this one must not."));

                        return;
                    }

                    status.Children.Add(Ui.Line(Ui.Count(doomed.Count, "viewpoint") + ": "
                        + string.Join(", ", doomed.Take(6).Select(v => v.Name))
                        + (doomed.Count > 6 ? ", ..." : string.Empty), 0.8));

                    status.Children.Add(Ui.Danger("Yes, delete " + Ui.Count(doomed.Count, "viewpoint"), () =>
                    {
                        var gone = 0;

                        foreach (var view in doomed)
                            if (session.Viewpoints.Delete(view.Id)) gone++;

                        status.Children.Add(Ui.Done("Deleted " + Ui.Count(gone, "viewpoint")
                            + ". Ctrl+Z will not bring them back; save the document only when you are sure."));

                        session.Record(ActivityKind.Export, "deleted " + Ui.Count(gone, "viewpoint"));
                        Redraw(session);
                    }));
                });

                return Bench.Group("Delete them", ask, status);
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
                var folder = Ui.Text("Coordination", null, Ui.Wide.Name);
                var pattern = Ui.Text("{n}. {group}", null, Ui.Wide.Name);

                var make = Ui.Runner("Save one per group on the board", _say, primary: false, work: job =>
                {
                    var board = CoordinateViews.LastBoard;

                    if (board == null)
                    {
                        // The missing input is built where it is missing, rather than somewhere
                        // else the user has to go and find.
                        _say.Children.Add(CoordinateViews.LoadHere("These viewpoints are of "
                            + "the board's groups, so there has to be a board to make them from."));

                        return;
                    }

                    var into = folder.Text.Trim();
                    var made = 0;
                    var empty = 0;
                    var refused = 0;

                    // The camera is borrowed once for the whole run and put back at the end. Doing
                    // it per group would leave the view visibly jumping for as long as this takes.
                    using (session.View.BorrowCamera())
                    {
                        var n = 1;

                        foreach (var group in board.Groups)
                        {
                            if (!job.Step(made, board.Groups.Count, "viewpoints saved")) break;

                            var points = group.Items.Select(i => (i.X, i.Y, i.Z)).ToList();

                            if (points.Count == 0) { empty++; n++; continue; }

                            job.Say("Framing " + group.Name + "...");
                            session.View.FrameBox(CoordinateViews.BoxOf(points));

                            var name = pattern.Text
                                .Replace("{n}", n.ToString("00", CultureInfo.InvariantCulture))
                                .Replace("{group}", group.Name)
                                .Replace("{who}", group.AssignedTo ?? "unassigned")
                                .Replace("{count}", group.Items.Count.ToString(CultureInfo.InvariantCulture));

                            if (session.Viewpoints.SaveCurrent(name, into.Length == 0 ? null : into) == null)
                            {
                                refused++;
                                n++;
                                continue;
                            }

                            made++;
                            n++;
                        }
                    }

                    _say.Children.Add(Ui.Done("Saved " + Ui.Count(made, "viewpoint")
                        + (into.Length == 0 ? "." : " into \"" + into + "\".")));

                    if (refused > 0)
                        _say.Children.Add(Ui.Problem(session.Viewpoints.LastProblem
                            ?? "The document would not take " + Ui.Count(refused, "viewpoint") + "."));

                    if (empty > 0)
                        _say.Children.Add(Ui.Note(Ui.Count(empty, "group")
                            + " had no positioned results to frame, so no viewpoint was made for "
                            + (empty == 1 ? "it" : "them") + "."));

                    session.Record(ActivityKind.Export, "saved " + Ui.Count(made, "viewpoint")
                                                        + " from the board");
                    Redraw(session);
                });

                return Bench.Group("A viewpoint per issue",
                    Ui.Field("Folder", folder),
                    Ui.Field("Name pattern", pattern,
                             "like \"01. Duct through transfer beam\" — {n} number, {group} "
                             + "group, {who} party, {count} clashes"),
                    make,
                    Ui.Small("Each one carries the overrides that were in force when it was made. If "
                             + "you want them clean, apply the Appearance stack you want first and "
                             + "make them again."));
            }

            private void Redraw(Session session)
            {
                _list.Children.Clear();
                _table = null;

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

                // The last two carried no header, so "carries overrides" sat in a nameless column
                // next to the folder and read as one.
                var table = Ui.Table(rows,
                    ("Viewpoint", 200), ("Folder", 120), ("Appearance", 130), ("Markup", 0)).Multi();

                // The camera follows a single tick and stays put for several. Flying the view to
                // the first of eight viewpoints somebody is selecting in order to delete them is
                // the host's own worst habit, not one to copy.
                table.OnPick(item =>
                {
                    if (table.PickedMany().Count == 1 && item is SavedView view)
                        session.Viewpoints.Apply(view.Id);
                });

                _table = table;

                // The bar, with no buttons on it. Every action here - rename, re-folder, render,
                // delete - is its own group in the setup column, because each needs its own inputs,
                // and each already works on what is ticked or on everything when nothing is. What was
                // missing is the part that says how many are ticked and lets somebody tick or
                // clear the lot without dragging down two hundred rows.
                _list.Children.Add(Ui.Picking(table));
            }
        }
    }
}
