using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;
using CamelWorks.Nav;
using CamelWorks.UI.Shell;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The Selection Tree, rebuilt in the shape the project is actually run in.
    ///
    /// The tree a model arrives with is the shape its authoring tool exported: a file, a level, and
    /// whatever the exporter grouped by. Nobody coordinates in that shape. They coordinate in zones,
    /// packages, systems and trades, and every tool downstream - a clash matrix, a viewpoint run, a
    /// takeoff - is harder than it needs to be until the library matches.
    ///
    /// A window rather than a card, because the recipe is four rows of four fields, and the answer
    /// is a tree and a list of what moved since last time. Neither fits a column four hundred and
    /// seventy pixels wide, and the pane version of this was three text boxes that could only read
    /// one property tab.
    /// </summary>
    public static class RestructureWindow
    {
        /// <summary>Where the recipe and the last run live in the project file.</summary>
        public const string Key = "restructure";


        /// <summary>The surface, for a desk to bench.</summary>
        /// <param name="session">The open document.</param>
        public static System.Windows.UIElement Surface(Session session) =>
            new Editor(session).View;

        private sealed class Editor
        {
            private const string WholeModel = "the whole model";
            private const string Selection = "what is selected";

            private const string GatherThem = "give them a branch";
            private const string LeaveThem = "leave them out";

            private readonly Session _session;

            private readonly StackPanel _levels = Ui.Stack();
            private readonly StackPanel _said = Ui.Stack();
            private readonly StackPanel _preview = Ui.Stack();
            private readonly StackPanel _moved = Ui.Stack();
            private readonly StackPanel _output = Ui.Stack();
            private readonly StackPanel _saved = Ui.Stack();

            private RestructureRecipe _recipe;
            private RestructureSnapshot _last;
            private RestructurePlan? _plan;

            private string _scope = WholeModel;

            internal Editor(Session session)
            {
                _session = session;

                var stored = session.Store.Section(ProjectStore.SetsSection)[Key];

                // Zero setup: a project that has never been restructured opens on a recipe that
                // reads the two properties every federation has, so the first press does something.
                _recipe = stored.Has("recipe")
                    ? RestructureRecipe.FromJson(stored["recipe"])
                    : Starter();

                _last = RestructureSnapshot.FromJson(stored["last"]);

                View = Build();
            }

            internal UIElement View { get; }

            private static RestructureRecipe Starter()
            {
                var recipe = new RestructureRecipe();
                recipe.Add("Element", "Level");
                recipe.Add("Element", "Category");
                return recipe;
            }

            /// <summary>
            /// Three decisions, in the order each one needs the last.
            ///
            /// This was two scrolling columns, so "Publish" sat beside the levels you were still
            /// naming, pointed at a tree nothing had read yet. The dependency is real: the recipe
            /// says what the tree is built from, the read says what that recipe actually produces
            /// out of this model, and only then is there anything to publish.
            ///
            /// The recipe stays one step rather than three. Scope, levels and naming are one
            /// decision taken in one place - a wizard that asked for them on separate screens
            /// would be a form with extra clicks, which is the thing this is meant to be instead
            /// of.
            ///
            /// Each step's surface is built once and handed back on every visit, so going back to
            /// change a level does not discard the tree already read.
            /// </summary>
            private UIElement Build()
            {
                var recipe = Ui.Stack(
                    Bench.Group("What it reads",
                        Ui.Field("Scope", Ui.Choice(new[] { WholeModel, Selection }, _scope,
                                                    pick => _scope = pick, 200))),
                    Bench.Group("The levels, outermost first", _levels),
                    Bench.Group("What comes out", Shape()),
                    Bench.Group("This recipe", Keep(), _saved));

                var tree = Ui.Stack(
                    Ui.Eyebrow("The tree it would make"),
                    _preview,
                    Ui.Rule(),
                    Ui.Eyebrow("What moved since the last run"),
                    _moved);

                var publish = Make();

                DrawLevels();
                ShowLast();

                _preview.Children.Add(Ui.Empty("Nothing read yet.",
                    "Say which properties the tree is built from, then read the model."));

                var steps = new[]
                {
                    new Ui.Step("The recipe", () => recipe,
                                () => _plan == null ? "Read the model first." : null),
                    new Ui.Step("The tree", () => tree),
                    new Ui.Step("Publish", () => publish),
                };

                // No finish button of its own: the last step carries Ui.Commit, which is the
                // two-stage "show me, then do it" a write into the set library has always used.
                return Ui.Steps(steps, null, null);
            }

            // -----------------------------------------------------------------------------------
            // The recipe
            // -----------------------------------------------------------------------------------

            /// <summary>
            /// A row per level, with a way to add one and a way to take one away.
            ///
            /// Redrawn rather than kept live, because adding a level changes what every row below it
            /// means and a stale row is worse than a repaint.
            /// </summary>
            private void DrawLevels()
            {
                _levels.Children.Clear();

                for (var i = 0; i < _recipe.Levels.Count; i++) _levels.Children.Add(Row(i));

                // Both are secondary: the press that matters in this window is "Read the model",
                // and a row that styles "Add a level" as the primary action points at the wrong one.
                UIElement? add = _recipe.Levels.Count < RestructureRecipe.MaxDepth
                    ? Ui.Quiet("Add a level", () =>
                    {
                        _recipe.Add("Element", string.Empty);
                        DrawLevels();
                    })
                    : null;

                UIElement? drop = _recipe.Levels.Count > 1
                    ? Ui.Danger("Remove the last", () =>
                    {
                        _recipe.Levels.RemoveAt(_recipe.Levels.Count - 1);
                        DrawLevels();
                    })
                    : null;

                if (add != null || drop != null) _levels.Children.Add(Ui.Across(add, drop));
            }

            private UIElement Row(int index)
            {
                var level = _recipe.Levels[index];

                // The two fields the whole recipe stands on, fed from the model: a level typed
                // as "Levels" where the federation says "Level" is a tree with one branch called
                // "(no value)", found out only after the read.
                var tab = Pickers.Tab(level.Category, text => level.Category = text, 150);
                var name = Pickers.Property(level.Property, text => level.Property = text, 150);

                var holes = Ui.Choice(new[] { GatherThem, LeaveThem },
                    level.Missing == MissingValues.Leave ? LeaveThem : GatherThem,
                    pick =>
                    {
                        level.Missing = pick == LeaveThem ? MissingValues.Leave : MissingValues.Gather;
                        DrawLevels();
                    }, 170);

                return Bench.Group("Level " + (index + 1).ToString(CultureInfo.InvariantCulture),
                    Ui.Pair(Ui.Field("Property tab", tab), Ui.Field("Property", name)),
                    Ui.Field("Elements without it", holes),
                    level.Missing == MissingValues.Gather
                        ? Ui.Field("Called",
                            Ui.Text(level.GatherAs, text => level.GatherAs = text, 150),
                            "The branch they land in.")
                        : null);
            }

            private UIElement Shape()
            {
                var root = Ui.Text(_recipe.Root ?? string.Empty, "left blank for the top of the tree",
                                   text => _recipe.Root = text, 180);

                var pattern = Ui.Text(_recipe.NamePattern, text => _recipe.NamePattern = text, 180);

                return Ui.Stack(
                    Ui.Field("Top folder", root,
                             "Everything the recipe makes hangs under it."),
                    Ui.Field("Set name", pattern, "{value} is the branch, {path} the folders above it."),
                    Ui.Pair(
                        Ui.Field("Smallest set", Number(_recipe.Floor, v => _recipe.Floor = v)),
                        Ui.Field("Refuse past", Number(_recipe.Ceiling, v => _recipe.Ceiling = v))),
                    // _said is this Runner's output and sits on this step. It used to write into
                    // _preview, which is on the next one - so "Fill in at least one level" and
                    // "Nothing in scope" were written behind a gate that only opens once the read
                    // has worked, which is exactly when neither sentence is needed.
                    Ui.Actions(Ui.Runner("Read the model", _said, Read)),
                    _said);
            }

            private TextBox Number(int value, Action<int> set) =>
                Ui.Text(value.ToString(CultureInfo.InvariantCulture), text =>
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                     out var parsed) && parsed >= 0) set(parsed);
                }, 70);

            /// <summary>
            /// Saving the recipe, which is the whole reason this is a tool rather than a one-off.
            ///
            /// Optional, though. Everything here works on a recipe typed in this morning and never
            /// saved; keeping it is what turns the next revision into one press.
            /// </summary>
            private UIElement Keep()
            {
                var name = Ui.Text(_recipe.Name, text => _recipe.Name = text, 180);

                return Ui.Stack(
                    Ui.Field("Called", name, "Kept, the next revision is one press."),
                    Ui.Actions(Ui.Quiet("Keep the recipe", Store)));
            }

            private void Store()
            {
                _saved.Children.Clear();

                Write(_recipe, _last);

                _saved.Children.Add(_session.Store.Save()
                    ? Ui.Done("Kept as \"" + _recipe.Name + "\".")
                    : Ui.Problem(_session.Store.LastSaveProblem ?? "Could not write the project file."));
            }

            private void Write(RestructureRecipe recipe, RestructureSnapshot last)
            {
                var section = _session.Store.Section(ProjectStore.SetsSection);

                section.Set(Key, JsonValue.Object()
                    .Set("recipe", recipe.ToJson())
                    .Set("last", last.ToJson()));
            }

            // -----------------------------------------------------------------------------------
            // Reading the model
            // -----------------------------------------------------------------------------------

            private void Read(Job job)
            {
                _preview.Children.Clear();
                _moved.Children.Clear();
                _output.Children.Clear();
                _plan = null;

                var levels = _recipe.Filled;

                if (levels.Count == 0)
                {
                    _said.Children.Add(Ui.Problem(
                        "Fill in at least one level - a property tab and a property."));
                    ShowLast();
                    return;
                }

                job.Say("Reading the model...");

                var rows = new List<IReadOnlyList<string?>>();

                var traversal = _scope == Selection
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.WholeDocument;

                foreach (var item in _session.Model.Traverse(traversal))
                {
                    if (!job.Step(rows.Count, "elements read")) return;

                    var values = new string?[levels.Count];

                    for (var i = 0; i < levels.Count; i++)
                        values[i] = item.Property(levels[i].Category, levels[i].Property);

                    rows.Add(values);
                }

                if (rows.Count == 0)
                {
                    _said.Children.Add(Ui.Empty("Nothing in scope.",
                        _scope == Selection
                            ? "Select some elements, or read the whole model instead."
                            : "No models are loaded."));
                    ShowLast();
                    return;
                }

                var plan = Restructure.Plan(_recipe, rows);
                _plan = plan;

                Show(plan);
                ShowMoved(plan);
            }

            private void Show(RestructurePlan plan)
            {
                if (plan.Refusal != null)
                {
                    _preview.Children.Add(Ui.Problem(plan.Refusal));
                    return;
                }

                _preview.Children.Add(Ui.Line(plan.Explain(), 1, true));

                foreach (var warning in plan.Warnings) _preview.Children.Add(Ui.Note(warning));

                // What each level cost, at the level it cost it. One number for the whole tree says
                // how much is missing without saying which property to go and fix.
                foreach (var tally in plan.Tallies.Where(t => t.Missing > 0))
                    _preview.Children.Add(Ui.Line(
                        Ui.Count(tally.Missing, "element") + " carry nothing for " + tally.Level.Address
                        + (tally.Level.Missing == MissingValues.Gather
                            ? ", so they are in \"" + tally.Level.Bucket + "\"."
                            : ", and are left out."), 0.75));

                if (plan.Roots.Count == 0) return;

                if (plan.IsWhole)
                    _preview.Children.Add(Ui.Done("Every element in scope is in a set."));

                _preview.Children.Add(Ui.Rule());

                foreach (var branch in plan.Roots) Draw(branch, 0);
            }

            /// <summary>The tree, indented, folders first and counts on the right.</summary>
            private void Draw(SetBranch branch, int depth)
            {
                var row = new WrapPanel { Margin = new Thickness(depth * 16, 2, 0, 2) };

                row.Children.Add(Ui.Line(branch.IsLeaf ? branch.Name : branch.Name + " /",
                                         branch.IsLeaf ? 1 : 0.85, !branch.IsLeaf));

                row.Children.Add(Ui.Pill(Ui.Count(branch.Elements, "element"), Tone.Plain));

                if (!branch.IsLeaf)
                    row.Children.Add(Ui.Pill(Ui.Count(branch.Leaves().Count(), "set"), Tone.Plain));

                foreach (var child in row.Children.OfType<FrameworkElement>())
                    child.Margin = new Thickness(0, 0, 8, 0);

                _preview.Children.Add(row);

                // Deep enough to see the shape without printing five hundred leaf names into a
                // panel nobody will scroll. The table under Make() is where the sets are listed.
                if (depth >= 2) return;

                foreach (var child in branch.Children) Draw(child, depth + 1);
            }

            // -----------------------------------------------------------------------------------
            // What moved
            // -----------------------------------------------------------------------------------

            private void ShowLast()
            {
                _moved.Children.Clear();

                if (_last.IsEmpty)
                {
                    _moved.Children.Add(Ui.Empty("No run recorded yet.",
                        "Publish once and the next run says which sets are new, which values have "
                        + "left the model, and which changed size."));
                    return;
                }

                _moved.Children.Add(Ui.Line("Last run held " + Ui.Count(_last.Count, "set")
                    + Ago(_last.WhenTicks) + ".", 0.8));
            }

            private void ShowMoved(RestructurePlan plan)
            {
                _moved.Children.Clear();

                if (!plan.IsUsable) { ShowLast(); return; }

                if (_last.IsEmpty)
                {
                    _moved.Children.Add(Ui.Note(
                        "Nothing recorded to compare against, so every set here reads as new. "
                        + "Publish once and the next revision is a list of what moved."));
                    return;
                }

                var delta = Restructure.Since(_last, plan);

                _moved.Children.Add(Ui.Line(delta.Explain(), 1, true));

                if (delta.IsQuiet) return;

                var rows = delta.Changes.Take(200)
                    .Select(c => new TableRow(c, c.Name,
                                              c.IsNew ? "new" : c.IsGone ? "gone" : "resized",
                                              c.Before.ToString("N0", CultureInfo.InvariantCulture),
                                              c.After.ToString("N0", CultureInfo.InvariantCulture)))
                    .ToList();

                var moved = Ui.Table(rows,
                    ("Set", 300), ("Change", 90), ("Was", 80), ("Now", 80));

                _moved.Children.Add(Ui.Picking(moved,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(moved, picked), say, "set"))));

                if (delta.Changes.Count > rows.Count)
                    _moved.Children.Add(Ui.Line(
                        Ui.Count(delta.Changes.Count - rows.Count, "more change") + " not listed.", 0.7));
            }

            private static string Ago(long ticks)
            {
                if (ticks <= 0) return string.Empty;

                try
                {
                    return ", on " + new DateTime(ticks).ToLocalTime()
                        .ToString("d MMM yyyy", CultureInfo.CurrentCulture);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // A project file written by a future version, or edited by hand. Not worth a
                    // message: the count is the useful half of the sentence.
                    return string.Empty;
                }
            }

            // -----------------------------------------------------------------------------------
            // Making it
            // -----------------------------------------------------------------------------------

            private UIElement Make()
            {
                var commit = Ui.Commit("Try every set without making anything", "Publish", "set",
                    plan: Rehearse,
                    apply: Publish);

                return Ui.Stack(commit, _output);
            }

            /// <summary>
            /// Run every set's rule against the model and report what it would hold.
            ///
            /// The preview counted from the read; this counts from the search, and the two disagree
            /// exactly where the published set would disappoint - a property spelled differently
            /// from the one the tree was built on, or a rule the host cannot run as one search.
            /// </summary>
            private int Rehearse(Job job, Panel into)
            {
                var plan = _plan;

                if (plan == null || !plan.IsUsable)
                {
                    into.Children.Add(Ui.Note("Read the model first."));
                    return 0;
                }

                var sets = plan.Sets;
                var rows = new List<TableRow>();
                var empty = 0;
                var apart = 0;
                var done = 0;

                foreach (var leaf in sets)
                {
                    if (!job.Step(done++, sets.Count, "rules tried")) return 0;

                    var outcome = _session.Search.Run(DnfCompiler.Compile(leaf.Expression!));

                    if (outcome.Count == 0) empty++;
                    if (outcome.Count != leaf.Elements) apart++;

                    rows.Add(new TableRow(leaf, leaf.FullName,
                                          leaf.Elements.ToString("N0", CultureInfo.InvariantCulture),
                                          outcome.Count.ToString("N0", CultureInfo.InvariantCulture),
                                          outcome.IsLive ? "live search" : "fixed list"));
                }

                var costed = Ui.Table(rows,
                    ("Set", 300), ("Read", 80), ("Search finds", 100), ("As", 110));

                into.Children.Add(Ui.Picking(costed,
                    new Ui.OnPicked("Copy {n}",
                        (picked, say) => Ui.Copy(Ui.Lines(costed, picked), say, "set"))));

                if (empty > 0)
                    into.Children.Add(Ui.Note(Ui.Count(empty, "set")
                        + " would match nothing at all. Usually the property or its tab is spelled "
                        + "differently in the host's search than in the properties panel."));

                if (apart > empty)
                    into.Children.Add(Ui.Note(Ui.Count(apart - empty, "set")
                        + " would hold a different number than the read found. A search runs over "
                        + "the whole document; the read ran over " + _scope + "."));

                return sets.Count;
            }

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

                    // Saved into the project first and pushed to the host second, so a host that
                    // refuses one set leaves a library somebody can publish again rather than a
                    // half-made tree with no record of the other half.
                    var existing = library.Sets.FirstOrDefault(
                        s => string.Equals(s.Name, leaf.FullName, StringComparison.OrdinalIgnoreCase));

                    library.Put(new SavedSet(existing?.Id ?? library.NextId("set"),
                                             leaf.FullName, leaf.Expression!)
                    {
                        Note = "Restructured on "
                             + string.Join(" / ", plan.Recipe.Filled.Select(l => l.Property)) + ".",
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

                // The run is recorded whatever the host did with it, because the delta is about the
                // tree the recipe produced, not about how many sets the host accepted.
                _last = Restructure.Snapshot(plan, DateTime.UtcNow.Ticks);
                Write(_recipe, _last);

                _output.Children.Add(_session.Store.Save()
                    ? Ui.Done("Published " + Ui.Count(published, "set") + " into folders, and kept "
                              + "the run so the next one can be compared against it.")
                    : Ui.Problem(_session.Store.LastSaveProblem ?? "Could not write the project file."));

                if (live < published)
                    _output.Children.Add(Ui.Note(Ui.Count(published - live, "set")
                        + " went in as a fixed list of elements rather than a live search, because "
                        + "the host could not express the rule as one search. Those will not pick up "
                        + "elements added later; the CamelWorks set will."));

                foreach (var problem in refused.Take(6)) _output.Children.Add(Ui.Problem(problem));

                if (refused.Count > 0)
                    _output.Children.Add(Ui.Note("Every set here is in the project whatever the host "
                        + "did with it, so a refusal costs nothing but a second attempt."));

                ShowMoved(plan);

                _session.Record(ActivityKind.Reconcile,
                    "restructured the tree into " + Ui.Count(published, "set") + " on "
                    + string.Join(" / ", plan.Recipe.Filled.Select(l => l.Property)));
            }
        }
    }
}
