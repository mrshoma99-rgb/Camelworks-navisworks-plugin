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

                if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

                Redraw();
                RedrawSaved(session);

                // Building one set is what the tab is, so it is not in a box. Generating many is a
                // second act with a different consequence, and it earns the fold below.
                return Ui.Scroll(Ui.Stack(
                    Ui.Form(
                        _editor,
                        Ui.Rule(),
                        Ui.Field("Name", _name, "how it reads in the Navisworks selection tree")),
                    Actions(session),
                    _results,
                    Ui.Small("Groups are ORed together; conditions inside a group are ANDed. A negated "
                             + "condition compiles to a subtraction rather than a not-equals — those are "
                             + "different sets, because a host condition on a property an element does not "
                             + "have matches nothing."),
                    Reshape(),
                    Ui.Rule(),
                    Alike(session),
                    Ui.Rule(),
                    Schedule(),
                    Ui.Rule(),
                    Ui.Eyebrow("Saved sets"),
                    Ui.Small("Kept in the project file, so they travel with the job."),
                    _saved));
            }

            private UIElement Actions(Session session)
            {
                var preview = Ui.Runner("Select what it catches", _results, job =>
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
                        ? Ui.Done("Saved.")
                        : Ui.Problem(session.Store.LastSaveProblem ?? "Could not write the project file."));

                    RedrawSaved(session);
                });

                var publish = Ui.Runner("Publish as a Navisworks set", _results, primary: false, work: job =>
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

                return Ui.Actions(preview, save, publish);
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

                    var add = Ui.Quiet("+ and...", () => { group.Add(new Condition()); Redraw(); });
                    _editor.Children.Add(Ui.Across(add));
                }

                _editor.Children.Add(Ui.Across(
                    Ui.Quiet("+ Add an OR group", () => { _groups.Add(new List<Condition>()); Redraw(); })));
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

                var select = Ui.Runner("Select everything like it", found, work: job =>
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
                    found.Children.Add(Ui.Small("Save it as a set above if it is worth keeping."));

                    session.Record(ActivityKind.Regroup,
                        "selected " + Ui.Count(outcome.Count, "element") + " like the one picked");
                });

                return Ui.Marked(Glyphs.Find, "Everything like this one",
                    "Select one element, read what it carries, and tick what has to match.",
                    Ui.Actions(read),
                    facets,
                    Ui.Actions(select),
                    found);
            }

            /// <summary>
            /// The way to forty sets at once.
            ///
            /// This screen makes one set from an expression, which is the right tool for the set
            /// you are thinking about now. It is the wrong tool for the six disciplines' worth a
            /// job starts with, which arrive as a spreadsheet and want reading, not retyping.
            /// </summary>
            /// <summary>
            /// The whole library at once, from the properties the model already carries.
            ///
            /// This screen builds one set from an expression, which is the right tool for the set
            /// somebody is thinking about now. It is the wrong tool for the tree - four levels,
            /// each reading its own property tab, with somewhere for the elements that carry
            /// nothing and a list of what moved since the last revision. That wants a window.
            /// </summary>
            private UIElement Reshape()
            {
                var open = Ui.Button("Rebuild the tree from properties...", () =>
                {
                    var problem = RestructureWindow.Show();
                    if (problem != null) _results.Children.Add(Ui.Problem(problem));
                });

                return Ui.Stack(
                    Ui.Eyebrow("The whole library at once"),
                    Ui.Small("The Selection Tree is the shape the model was exported in, not the "
                             + "shape the job is run in. Name the properties that describe the "
                             + "project - level, discipline, system, package - and it builds the "
                             + "library to match, keeps the recipe, and tells you what moved when "
                             + "the next revision lands."),
                    Ui.Actions(open));
            }

            private UIElement Schedule()
            {
                var open = Ui.Button("Open a schedule from a spreadsheet...", () =>
                {
                    var problem = ScheduleWindow.Show();
                    if (problem != null) _results.Children.Add(Ui.Problem(problem));
                });

                return Ui.Stack(
                    Ui.Eyebrow("Many sets at once"),
                    Ui.Small("Already have the sets as a spreadsheet - a row per condition, or a row "
                             + "per set with a column per property? Read it rather than retyping it, "
                             + "and run the same schedule again when the next revision lands."),
                    Ui.Actions(open));
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
            private readonly StackPanel _travel = Ui.Stack();

            private ForeignGroup? _picked;

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

                _layers.AddRange(LayerStackJson.Read(session.Store.Section(ProjectStore.LayersSection)["stack"]));

                Redraw(session);
                BuildAdder(session);

                // The stack is what this tab is, so it is not in a box: the layers, then the
                // button that puts them on the model, then the two things you can do next.
                return Ui.Scroll(Ui.Stack(
                    Ui.Eyebrow("Top of the list wins"),
                    _stack,
                    Actions(session),
                    _results,
                    _adder,
                    Ui.Rule(),
                    Elsewhere(session),
                    Ui.Rule(),
                    Travel(session)));
            }

            /// <summary>
            /// The scheme, off this model and onto another one.
            ///
            /// Everything else on this tab is better than the host at deciding what a federation
            /// looks like, and none of it could leave the document. A scheme agreed in a workshop
            /// is an afternoon's work that lived in one person's Navisworks and died with it.
            /// </summary>
            private UIElement Travel(Session session)
            {
                var options = new TransferOptions();
                var note = Ui.Text(string.Empty, "what this scheme is", null, 240);

                var take = Ui.Runner("Take this model's scheme...", _travel, primary: false,
                                     work: job => Take(session, job, note.Text));

                var put = Ui.Runner("Bring a scheme in...", _travel, primary: false,
                                    work: job => Bring(session, job, options));

                return Ui.Stack(
                    Ui.Eyebrow("Somewhere else"),
                    Ui.Small("Every override in this model, written down and replayed into another. "
                             + "The identity is tried first and the name second, because a key does "
                             + "not survive a re-export - and how each element was found is reported, "
                             + "since found-by-name is a guess."),
                    Ui.Form(
                        Ui.Field("Call it", note),
                        Ui.Check("Bring the colours", options.Colours, on => options.Colours = on),
                        Ui.Check("Bring the transparency", options.Transparency,
                                 on => options.Transparency = on),
                        Ui.Check("Bring what was hidden", options.Hidden, on => options.Hidden = on),
                        Ui.Small("A colour scheme is a way of reading a model; a hidden element is a "
                                 + "claim that something should not be looked at. Somebody taking the "
                                 + "first rarely wants the second."),
                        Ui.Check("Fall back to the element's name", options.ByPath,
                                 on => options.ByPath = on)),
                    Ui.Actions(take, put),
                    _travel);
            }

            /// <summary>Every element's appearance, read in chunks so Stop means something.</summary>
            private void Take(Session session, Job job, string note)
            {
                _travel.Children.Clear();

                job.Say("Listing the model...");

                var keys = new List<ElementKey>();
                var paths = new Dictionary<ElementKey, string>();

                foreach (var item in session.Model.Traverse(TraversalScope.WholeDocument))
                {
                    if (!job.Step(keys.Count, "elements listed")) return;
                    if (!item.HasGeometry) continue;

                    keys.Add(item.Key);
                    paths[item.Key] = string.Join(AppearanceEntry.PathSeparator, item.TreePath);
                }

                var states = new List<AppearanceState>(keys.Count);

                for (var at = 0; at < keys.Count; at += ScanChunk)
                {
                    if (!job.Step(at, keys.Count, "elements read")) return;

                    var chunk = keys.GetRange(at, Math.Min(ScanChunk, keys.Count - at));
                    states.AddRange(session.View.ReadAppearance(chunk));
                }

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

                if (dialog.ShowDialog() != true) return;

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

                if (dialog.ShowDialog() != true) return;

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

                        _results.Children.Add(Ui.Done("Applied."));
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
                            + "but still coloured, because that colour is somebody else's work."));
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
                        if (!job.Step(keys.Count, "elements listed")) return;
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

                return Ui.Marked(Glyphs.Find, "What else is going on",
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

                _adder.Children.Add(Ui.Section("Add a layer",
                    "hidden or coloured, over whatever you point it at", false,
                    Ui.Form(
                        Ui.Pair(Ui.Field("Name", name), Ui.Field("Covers", source)),
                        Ui.Pair(Ui.Field("Visibility", visibility),
                                Ui.Field("Colour (#rrggbb)", colour)),
                        Ui.Pair(Ui.Field("Transparency (0-1)", transparency),
                                Ui.Field("Why", note))),
                    add));
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

            private string _size = "1920 x 1080";

            internal UIElement Build()
            {
                var session = Host.Current;

                if (session == null) return Ui.Scroll(Ui.Stack(Ui.Problem(Host.NoModel)));

                Redraw(session);

                var save = Ui.Runner("Save this view", _results, primary: true, work: _ =>
                {
                    var view = session.Viewpoints.SaveCurrent(
                        "View " + (session.Viewpoints.All().Count + 1).ToString(CultureInfo.InvariantCulture));

                    _results.Children.Add(Ui.Done("Saved \"" + view.Name + "\"."));
                    Redraw(session);
                });

                return Ui.Scroll(Ui.Stack(
                    save,
                    _list,
                    Ui.Small("Tick the ones to act on. With nothing ticked, everything below acts on "
                             + "all of them — except deleting, which never does."),
                    Rename(session),
                    Refolder(session),
                    Render(session),
                    CopyOverrides(session),
                    Sweep(session),
                    _results,
                    Ui.Rule(),
                    Generate(session),
                    Ui.Rule(),
                    Walkthrough(session)));
            }

            /// <summary>
            /// One viewpoint per element in the selection.
            ///
            /// This tab could rename, refile and delete viewpoints, and could not make one. That is
            /// backwards: making them is the forty minutes of clicking, framing and typing that
            /// nobody does, which is why a review is one person driving and everybody else watching.
            /// Renaming forty viewpoints is a nuisance; not having them is the reason there is
            /// nothing to rename.
            /// </summary>
            private UIElement Generate(Session session)
            {
                var recipe = new ViewpointRecipe();

                var pattern = Ui.Text(recipe.Pattern, text => recipe.Pattern = text, 220);
                var folder = Ui.Text(string.Empty, text => recipe.Folder = text, 160);

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

                            into.Children.Add(Ui.Table(rows, ("It would be called", 0)));
                        }

                        return run.Views.Count;
                    },
                    apply: job => Capture(session, run, recipe, job));

                return Ui.Marked(Glyphs.Camera, "Make one for each element",
                    "Select what the walkthrough is about, and get a numbered viewpoint framed on "
                    + "every one of them. Nothing has to be set up first.",
                    Ui.Form(
                        Ui.Field("Name them", pattern, "{n} is the number, {name} is the element's own name."),
                        Ui.Pair(Ui.Field("Start at", start), Ui.Field("Digits", padding)),
                        Ui.Pair(Ui.Field("Look from", from), Ui.Field("Room around (m)", margin)),
                        Ui.Field("Put them in folder", folder)),
                    section,
                    make);
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

                        session.Viewpoints.SaveCurrent(view.Name,
                            string.IsNullOrWhiteSpace(recipe.Folder) ? null : recipe.Folder);

                        made++;
                    }

                    if (recipe.Section) session.View.SetSectionBoxEnabled(false);
                }

                _results.Children.Add(Ui.Line("Made " + Ui.Count(made, "viewpoint") + ".", 1, true));

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
                var pattern = Ui.Text("{n}. {name}", null, 200);
                var start = Ui.Text("1", null, 60);

                var run = Ui.Runner("Rename them", _results, primary: false, work: job =>
                {
                    var views = Chosen(session);

                    if (!int.TryParse(start.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        n = 1;

                    var done = 0;

                    foreach (var view in views)
                    {
                        if (!job.Step(done, views.Count, "renamed")) break;

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

                    _results.Children.Add(Ui.Done("Renamed " + Ui.Count(done, "viewpoint") + "."));
                    Redraw(session);
                });

                return Ui.Section("Rename them",
                    () => pattern.Text.Replace("{n}", start.Text.PadLeft(2, '0'))
                                      .Replace("{name}", "Riser at core")
                                      .Replace("{folder}", "Coordination"),
                    false,
                    Ui.Form(Ui.Pair(
                        Ui.Field("Pattern", pattern, "{n} the number, {name} the current name, "
                                                     + "{folder} the folder"),
                        Ui.Field("Start at", start))),
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
                var into = Ui.Text("Coordination", null, 180);

                var move = Ui.Runner("Move them", _results, primary: false, work: job =>
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

                    _results.Children.Add(Ui.Done("Moved " + Ui.Count(moved, "viewpoint")
                        + (wanted.Length == 0 ? " to the top level." : " into \"" + wanted + "\".")));

                    if (already > 0)
                        _results.Children.Add(Ui.Line(Ui.Count(already, "viewpoint")
                            + " were already there and were left alone.", 0.7));

                    if (refused > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(refused, "viewpoint")
                            + " could not be moved. A viewpoint the host will not take out of its folder "
                            + "keeps its old place rather than being lost, so nothing has gone missing."));

                    session.Record(ActivityKind.Export, "filed " + Ui.Count(moved, "viewpoint"));
                    Redraw(session);
                });

                return Ui.Section("File them into a folder", () => into.Text.Trim(), false,
                    Ui.Form(Ui.Field("Into folder", into, "made if it is not there; one already in "
                                                          + "it is left where it is")),
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
                var into = Ui.Text(string.Empty, null, 340);
                var size = Ui.Choice(new[] { "1280 x 720", "1920 x 1080", "2560 x 1440" }, "1920 x 1080",
                                     pick => _size = pick);

                var render = Ui.Runner("Render them", _results, primary: false, work: job =>
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
                        _results.Children.Add(Ui.Problem("Could not make that folder: " + Ui.Describe(e)));
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

                    _results.Children.Add(Ui.Done("Wrote " + Ui.Count(written, "image") + " to " + folder + "."));

                    if (blank > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(blank, "viewpoint")
                            + " produced no image. Off-screen rendering is not available in every "
                            + "configuration - a machine with no graphics context, a document still "
                            + "loading. Nothing was written for those rather than an empty file."));

                    foreach (var problem in failed.Take(6)) _results.Children.Add(Ui.Problem(problem));

                    session.Record(ActivityKind.Export, "rendered " + Ui.Count(written, "viewpoint"));
                });

                return Ui.Section("Render them to images", () => _size, false,
                    Ui.Form(Ui.Pair(
                        Ui.PathField("Write into", into, PathKind.Folder,
                                     hint: "blank writes them to your documents"),
                        Ui.Field("Size", size))),
                    render,
                    Ui.Small("The camera goes back where it was when the run finishes."));
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
                var source = Ui.Choice(names, from, pick => from = pick, 260);

                var copy = Ui.Runner("Copy them across", _results, primary: false, work: job =>
                {
                    var chosen = session.Viewpoints.All()
                        .FirstOrDefault(v => string.Equals(v.ToString(), from, StringComparison.Ordinal));

                    if (string.IsNullOrEmpty(chosen.Id))
                    {
                        _results.Children.Add(Ui.Problem("That viewpoint is no longer in the document."));
                        return;
                    }

                    var targets = Chosen(session)
                        .Where(v => !string.Equals(v.Id, chosen.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (targets.Count == 0)
                    {
                        _results.Children.Add(Ui.Line("There is nothing else to put them on.", 0.8));
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

                    _results.Children.Add(Ui.Done("Copied the overrides onto " + Ui.Count(done, "viewpoint")
                        + "."));

                    if (refused > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(refused, "viewpoint")
                            + " were left exactly as they were, because the host would not rewrite them. "
                            + "A refusal costs nothing: none of them has been changed."));

                    session.Record(ActivityKind.Export,
                        "copied viewpoint overrides onto " + Ui.Count(done, "viewpoint"));

                    Redraw(session);
                });

                return Ui.Section("Copy overrides onto the rest", "from " + names[0], false,
                    Ui.Form(Ui.Field("Take them from", source)),
                    copy,
                    Ui.Note("Rewriting a saved viewpoint replaces its redlines with the source's. That is "
                            + "the host's own behaviour and there is no call that avoids it, so a viewpoint "
                            + "somebody has drawn on will lose the drawing. Nothing else about the target "
                            + "changes - not its name, not its folder, not its camera."));
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

                var ask = Ui.Danger("Delete the ticked ones", () =>
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
                        {
                            session.Viewpoints.Delete(view.Id);
                            gone++;
                        }

                        status.Children.Add(Ui.Done("Deleted " + Ui.Count(gone, "viewpoint")
                            + ". Ctrl+Z will not bring them back; save the document only when you are sure."));

                        session.Record(ActivityKind.Export, "deleted " + Ui.Count(gone, "viewpoint"));
                        Redraw(session);
                    }));
                });

                return Ui.Section("Delete them", "asks once more first", false, ask, status).Grave();
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

                            session.Viewpoints.SaveCurrent(name, into.Length == 0 ? null : into);

                            made++;
                            n++;
                        }
                    }

                    _results.Children.Add(Ui.Done("Saved " + Ui.Count(made, "viewpoint")
                        + (into.Length == 0 ? "." : " into \"" + into + "\".")));

                    if (empty > 0)
                        _results.Children.Add(Ui.Note(Ui.Count(empty, "group")
                            + " had no positioned results to frame, so no viewpoint was made for "
                            + (empty == 1 ? "it" : "them") + "."));

                    session.Record(ActivityKind.Export, "saved " + Ui.Count(made, "viewpoint")
                                                        + " from the board");
                    Redraw(session);
                });

                return Ui.Marked(Glyphs.Camera, "A viewpoint per issue",
                    "The walkthrough a coordination meeting is actually run from: one saved view per "
                    + "group on the board, framed on the group.",
                    Ui.Form(Ui.Pair(
                        Ui.Field("Folder", folder),
                        Ui.Field("Name pattern", pattern,
                                 "like \"01. Duct through transfer beam\" — {n} number, {group} "
                                 + "group, {who} party, {count} clashes"))),
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

                var table = Ui.Table(rows,
                    ("Viewpoint", 200), ("Folder", 120), ("", 130), ("", 0)).Multi();

                // The camera follows a single tick and stays put for several. Flying the view to
                // the first of eight viewpoints somebody is selecting in order to delete them is
                // the host's own worst habit, not one to copy.
                table.OnPick(item =>
                {
                    if (table.PickedMany().Count == 1 && item is SavedView view)
                        session.Viewpoints.Apply(view.Id);
                });

                _table = table;
                _list.Children.Add(table);
            }
        }
    }
}
