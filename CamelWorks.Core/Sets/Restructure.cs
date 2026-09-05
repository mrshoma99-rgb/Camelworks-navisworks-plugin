using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Sets
{
    /// <summary>What becomes of the elements that carry nothing for a level.</summary>
    public enum MissingValues
    {
        /// <summary>Count them and leave them out of the tree.</summary>
        Leave = 0,

        /// <summary>Give them a branch of their own, so the tree still covers the whole model.</summary>
        Gather = 1,
    }

    /// <summary>One level of the tree: a property, and what to do about the elements without it.</summary>
    public sealed class TreeLevel
    {
        /// <summary>What the gathered branch is called when nothing else is said.</summary>
        public const string DefaultGatherAs = "Not set";

        /// <summary>An empty level, for a row somebody is about to fill in.</summary>
        public TreeLevel()
        {
        }

        /// <summary>A level on a named property.</summary>
        public TreeLevel(string? category, string? property)
        {
            Category = category ?? string.Empty;
            Property = property ?? string.Empty;
        }

        /// <summary>
        /// Which property tab it is read from.
        ///
        /// Per level, not per tree, and that is the whole difference between this and generating a
        /// family. On a real federation the level comes from "Element", the discipline from a tab
        /// the exporter wrote, and the system from "Revit Type" — three tabs, one tree. A tool that
        /// insists on one tab for all of them can only build the part of the tree that happens to
        /// live in the same place.
        /// </summary>
        public string Category { get; set; } = "Element";

        /// <summary>Which property inside that tab.</summary>
        public string Property { get; set; } = string.Empty;

        /// <summary>What becomes of the elements that carry nothing here.</summary>
        public MissingValues Missing { get; set; } = MissingValues.Gather;

        /// <summary>What the gathered branch is called.</summary>
        public string GatherAs { get; set; } = DefaultGatherAs;

        /// <summary>True when both halves of the address are filled in.</summary>
        public bool IsUsable =>
            !string.IsNullOrWhiteSpace(Category) && !string.IsNullOrWhiteSpace(Property);

        /// <summary>The address as one string, for a heading or a message.</summary>
        public string Address => IsUsable ? Category.Trim() + " / " + Property.Trim() : string.Empty;

        /// <summary>What the gathered branch is really called, after the blank fallback.</summary>
        public string Bucket
        {
            get
            {
                var named = GatherAs.Trim();
                return named.Length == 0 ? DefaultGatherAs : named;
            }
        }

        /// <summary>A copy, so editing a saved recipe does not edit the one that is running.</summary>
        public TreeLevel Copy() =>
            new TreeLevel(Category, Property) { Missing = Missing, GatherAs = GatherAs };

        /// <summary>Write it for the project file.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("category", Category)
                .Set("property", Property)
                .Set("missing", Missing == MissingValues.Gather ? "gather" : "leave")
                .Set("gatherAs", GatherAs);

        /// <summary>Read one back. An older file missing a key simply takes the default.</summary>
        public static TreeLevel FromJson(JsonValue? json)
        {
            if (json == null) return new TreeLevel();

            var level = new TreeLevel(json["category"].AsString("Element"),
                                      json["property"].AsString(string.Empty));

            level.Missing = string.Equals(json["missing"].AsString("gather"), "leave",
                                          StringComparison.OrdinalIgnoreCase)
                ? MissingValues.Leave
                : MissingValues.Gather;

            level.GatherAs = json["gatherAs"].AsString(DefaultGatherAs) ?? DefaultGatherAs;

            return level;
        }

        /// <inheritdoc />
        public override string ToString() => IsUsable ? Address : "(not set)";
    }

    /// <summary>The whole instruction: the levels, the naming, and where the tree is rooted.</summary>
    public sealed class RestructureRecipe
    {
        /// <summary>How deep a tree may go.</summary>
        public const int MaxDepth = 4;

        /// <summary>The default refusal ceiling, in sets.</summary>
        public const int DefaultCeiling = 500;

        private readonly List<TreeLevel> _levels = new List<TreeLevel>();

        /// <summary>What this recipe is called, so a project can hold more than one.</summary>
        public string Name { get; set; } = "Restructure";

        /// <summary>
        /// The folder the whole tree hangs under, or null for the top.
        ///
        /// It earns its place on the second run. Everything under it belongs to this recipe, so a
        /// re-run knows exactly what it made last time and what to take away — without it, telling
        /// a set this recipe made from a set somebody made by hand means guessing.
        /// </summary>
        public string? Root { get; set; }

        /// <summary>The levels, outermost first.</summary>
        public IList<TreeLevel> Levels => _levels;

        /// <summary>The levels that are actually filled in.</summary>
        public IReadOnlyList<TreeLevel> Filled => _levels.Where(l => l.IsUsable).ToList();

        /// <summary>Below this many elements a branch is not worth a set.</summary>
        public int Floor { get; set; } = 1;

        /// <summary>How many sets it will make before refusing outright.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>What a set is called. <c>{value}</c> and <c>{path}</c> are replaced.</summary>
        public string NamePattern { get; set; } = "{value}";

        /// <summary>Add a level and hand it back for editing.</summary>
        public TreeLevel Add(string? category = null, string? property = null)
        {
            var level = new TreeLevel(category ?? "Element", property);
            _levels.Add(level);
            return level;
        }

        /// <summary>A copy that shares nothing with this one.</summary>
        public RestructureRecipe Copy()
        {
            var copy = new RestructureRecipe
            {
                Name = Name,
                Root = Root,
                Floor = Floor,
                Ceiling = Ceiling,
                NamePattern = NamePattern,
            };

            foreach (var level in _levels) copy._levels.Add(level.Copy());

            return copy;
        }

        /// <summary>Write it for the project file, so the next revision is one press.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("name", Name)
                .Set("root", Root)
                .Set("floor", Floor)
                .Set("ceiling", Ceiling)
                .Set("pattern", NamePattern)
                .Set("levels", JsonValue.Array(_levels.Select(l => l.ToJson())));

        /// <summary>Read one back.</summary>
        public static RestructureRecipe FromJson(JsonValue? json)
        {
            var recipe = new RestructureRecipe();

            if (json == null) return recipe;

            recipe.Name = json["name"].AsString("Restructure") ?? "Restructure";
            recipe.Root = json["root"].AsString();
            recipe.Floor = (int)json["floor"].AsLong(1);
            recipe.Ceiling = (int)json["ceiling"].AsLong(DefaultCeiling);
            recipe.NamePattern = json["pattern"].AsString("{value}") ?? "{value}";

            foreach (var item in json["levels"].Items) recipe._levels.Add(TreeLevel.FromJson(item));

            return recipe;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Filled.Count == 0 ? Name : Name + ": " + string.Join(" > ", Filled.Select(l => l.Property));
    }

    /// <summary>What one level cost: how many elements had nothing to file them under.</summary>
    public sealed class LevelTally
    {
        internal LevelTally(TreeLevel level, int missing)
        {
            Level = level; Missing = missing;
        }

        /// <summary>Which level.</summary>
        public TreeLevel Level { get; }

        /// <summary>How many elements reached this level carrying nothing for it.</summary>
        public int Missing { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Level.Address + ": " + Measure.Count(Missing) + " with no value";
    }

    /// <summary>The tree a recipe comes to, and everything that did not fit in it.</summary>
    public sealed class RestructurePlan
    {
        internal RestructurePlan(RestructureRecipe recipe, IReadOnlyList<SetBranch> roots,
                                 int elements, int placed, int dropped, int small,
                                 IReadOnlyList<LevelTally> tallies, IReadOnlyList<string> warnings,
                                 string? refusal)
        {
            Recipe = recipe;
            Roots = roots;
            Elements = elements;
            Placed = placed;
            Dropped = dropped;
            Small = small;
            Tallies = tallies;
            Warnings = warnings;
            Refusal = refusal;
        }

        /// <summary>The recipe it came from.</summary>
        public RestructureRecipe Recipe { get; }

        /// <summary>The top-level branches.</summary>
        public IReadOnlyList<SetBranch> Roots { get; }

        /// <summary>How many elements were read.</summary>
        public int Elements { get; }

        /// <summary>How many of them landed in a set.</summary>
        public int Placed { get; }

        /// <summary>How many were left out because a level was told to leave them.</summary>
        public int Dropped { get; }

        /// <summary>How many were in a branch too small to be worth a set.</summary>
        public int Small { get; }

        /// <summary>What each level cost.</summary>
        public IReadOnlyList<LevelTally> Tallies { get; }

        /// <summary>Anything the tree did that was not asked for, in a sentence each.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Why nothing is proposed, or null when something is.</summary>
        public string? Refusal { get; }

        /// <summary>Every set the tree would make.</summary>
        public IReadOnlyList<SetBranch> Sets => Roots.SelectMany(r => r.Leaves()).ToList();

        /// <summary>How many folders it would make.</summary>
        public int Folders => Roots.Sum(Folded);

        /// <summary>True when there is something to make.</summary>
        public bool IsUsable => Refusal == null && Roots.Count > 0;

        /// <summary>
        /// True when every element read landed somewhere.
        ///
        /// The thing worth knowing about a restructured tree, and the reason the gathered branch
        /// exists: a tree that covers 94% of the model looks complete and is not, and the 6% is
        /// exactly the part nobody has looked at.
        /// </summary>
        public bool IsWhole => Elements > 0 && Placed == Elements;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;
            if (Elements == 0) return "Nothing in scope.";

            if (Roots.Count == 0)
            {
                var why = new List<string>();

                if (Dropped > 0) why.Add(Measure.Count(Dropped) + " carry nothing for a level");
                if (Small > 0) why.Add(Measure.Count(Small) + " are in branches under the floor");

                return why.Count == 0
                    ? "Nothing in scope carries "
                      + string.Join(" or ", Recipe.Filled.Select(l => "\"" + l.Property + "\"")) + "."
                    : "No sets: " + string.Join(" and ", why) + ".";
            }

            var sets = Sets.Count;

            var line = Measure.Count(sets) + (sets == 1 ? " set" : " sets");

            if (Folders > 0)
                line += " in " + Measure.Count(Folders) + (Folders == 1 ? " folder" : " folders");

            line += ", holding " + Measure.Count(Placed) + " of " + Measure.Count(Elements)
                  + " elements";

            var loose = new List<string>();

            if (Dropped > 0) loose.Add(Measure.Count(Dropped) + " with no value for a level");
            if (Small > 0) loose.Add(Measure.Count(Small) + " under the floor");

            if (loose.Count > 0) line += " - " + string.Join(", ", loose) + " left out";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        private static int Folded(SetBranch branch) =>
            branch.IsLeaf ? 0 : 1 + branch.Children.Sum(Folded);
    }

    /// <summary>One set's fate when a recipe is run again on a newer model.</summary>
    public sealed class RestructureChange
    {
        internal RestructureChange(string name, int before, int after)
        {
            Name = name; Before = before; After = after;
        }

        /// <summary>The set's full name, folders and all.</summary>
        public string Name { get; }

        /// <summary>How many elements it held last time. Zero when it is new.</summary>
        public int Before { get; }

        /// <summary>How many it holds now. Zero when it has gone.</summary>
        public int After { get; }

        /// <summary>True when this run invented it.</summary>
        public bool IsNew => Before == 0 && After > 0;

        /// <summary>True when the value behind it is gone from the model.</summary>
        public bool IsGone => After == 0 && Before > 0;

        /// <summary>The change in count, positive when it grew.</summary>
        public int Moved => After - Before;

        /// <inheritdoc />
        public override string ToString() =>
            IsNew ? Name + " - new, " + Measure.Count(After)
            : IsGone ? Name + " - gone, was " + Measure.Count(Before)
            : Name + " " + (Moved > 0 ? "+" : string.Empty)
              + Moved.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>What a re-run would change, set by set.</summary>
    public sealed class RestructureDelta
    {
        internal RestructureDelta(IReadOnlyList<RestructureChange> changes, int same)
        {
            Changes = changes; Same = same;
        }

        /// <summary>Every set that is new, gone, or a different size.</summary>
        public IReadOnlyList<RestructureChange> Changes { get; }

        /// <summary>How many sets came out exactly as they were.</summary>
        public int Same { get; }

        /// <summary>The sets this run invented.</summary>
        public IReadOnlyList<RestructureChange> Added => Changes.Where(c => c.IsNew).ToList();

        /// <summary>The sets whose value has left the model.</summary>
        public IReadOnlyList<RestructureChange> Removed => Changes.Where(c => c.IsGone).ToList();

        /// <summary>The sets that are still there but a different size.</summary>
        public IReadOnlyList<RestructureChange> Resized =>
            Changes.Where(c => !c.IsNew && !c.IsGone).ToList();

        /// <summary>True when the newer model produces exactly the tree the older one did.</summary>
        public bool IsQuiet => Changes.Count == 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Same == 0 && Changes.Count == 0) return "Nothing to compare against.";
            if (IsQuiet) return "The same " + Measure.Count(Same) + " sets, all the same size.";

            var parts = new List<string>();

            if (Added.Count > 0) parts.Add(Measure.Count(Added.Count) + " new");
            if (Removed.Count > 0) parts.Add(Measure.Count(Removed.Count) + " gone");
            if (Resized.Count > 0) parts.Add(Measure.Count(Resized.Count) + " a different size");

            return string.Join(", ", parts) + "; " + Measure.Count(Same) + " unchanged.";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The sizes a run came to, small enough to keep in the project file.
    ///
    /// The delta is only worth having across sessions. A revision arrives on Tuesday and the run
    /// it is being compared against happened on Friday, in a Navisworks somebody has since closed,
    /// so the tree itself is gone. The names and the counts are all the comparison needs.
    /// </summary>
    public sealed class RestructureSnapshot
    {
        private readonly Dictionary<string, int> _sizes;

        internal RestructureSnapshot(Dictionary<string, int> sizes, long whenTicks)
        {
            _sizes = sizes; WhenTicks = whenTicks;
        }

        /// <summary>An empty snapshot, which is what a first run compares against.</summary>
        public static RestructureSnapshot Empty { get; } =
            new RestructureSnapshot(new Dictionary<string, int>(StringComparer.Ordinal), 0);

        /// <summary>When the run happened, in ticks, or zero when it was never recorded.</summary>
        public long WhenTicks { get; }

        /// <summary>How many sets it held.</summary>
        public int Count => _sizes.Count;

        /// <summary>True when there is nothing here to compare against.</summary>
        public bool IsEmpty => _sizes.Count == 0;

        /// <summary>Every set's full name and the count it held.</summary>
        public IReadOnlyDictionary<string, int> Sizes => _sizes;

        /// <summary>Write it for the project file.</summary>
        public JsonValue ToJson()
        {
            var sets = JsonValue.Array();

            foreach (var pair in _sizes.OrderBy(p => p.Key, StringComparer.Ordinal))
                sets.Add(JsonValue.Object().Set("name", pair.Key).Set("held", pair.Value));

            return JsonValue.Object().Set("when", WhenTicks).Set("sets", sets);
        }

        /// <summary>Read one back.</summary>
        public static RestructureSnapshot FromJson(JsonValue? json)
        {
            if (json == null) return Empty;

            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var item in json["sets"].Items)
            {
                var name = item["name"].AsString();
                if (name == null || name.Length == 0) continue;

                sizes[name] = (int)item["held"].AsLong();
            }

            return new RestructureSnapshot(sizes, json["when"].AsLong());
        }

        /// <inheritdoc />
        public override string ToString() => Measure.Count(Count) + " sets";
    }

    /// <summary>
    /// A selection-set library shaped like the project, from the properties the model already
    /// carries.
    ///
    /// The Selection Tree is the shape the authoring tool exported in: a file, then a level, then
    /// whatever the exporter grouped by. Coordination happens in zones, packages, systems and
    /// trades, and every downstream tool — a clash matrix, a viewpoint run, a takeoff — is easier
    /// once the library matches that.
    ///
    /// So this builds that library from the properties, and the second run is what makes it worth
    /// having: the recipe is saved, a new revision arrives, and one press rebuilds the tree against
    /// the new model with a list of what moved.
    ///
    /// Every level reads its own property tab. The level comes from one place, the discipline from
    /// another, the system from a third; a tree that can only read one tab can only be half built.
    ///
    /// Elements with no value get a branch rather than being omitted. A tree covering 94% of the
    /// model looks complete, and the missing 6% is the part nobody has looked at, so by default it
    /// gets a branch of its own, named, counted and selectable.
    ///
    /// It refuses rather than truncates. Past the ceiling it makes nothing and reports how many it
    /// would have made, because quietly making the first five hundred leaves a subset of the
    /// requested library with no indication which part is missing.
    /// </summary>
    public static class Restructure
    {
        /// <summary>Work out the tree.</summary>
        /// <param name="recipe">The levels, the naming and the limits.</param>
        /// <param name="rows">One row per element: its value for each filled level, in order.</param>
        public static RestructurePlan Plan(RestructureRecipe? recipe,
                                           IEnumerable<IReadOnlyList<string?>>? rows)
        {
            var how = recipe ?? new RestructureRecipe();
            var levels = how.Filled;

            if (levels.Count == 0)
                return Refuse(how, "Fill in at least one level - a property tab and a property.");

            if (levels.Count > RestructureRecipe.MaxDepth)
                return Refuse(how,
                    "Four levels is as deep as this goes, and you have filled in "
                    + levels.Count.ToString(CultureInfo.InvariantCulture)
                    + ". Past four, a real federation is thousands of sets holding one element "
                    + "each, and the fifth thing people want is a filter rather than a folder.");

            var duplicate = levels
                .GroupBy(l => l.Address, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
                return Refuse(how,
                    "\"" + duplicate.Key + "\" is on two levels. The second would split every "
                    + "branch into one child with the same name, which is a deeper tree holding "
                    + "exactly the same sets.");

            var counted = new Dictionary<string, int>(StringComparer.Ordinal);
            var spelling = new Dictionary<string, Cell[]>(StringComparer.Ordinal);
            var missing = new int[levels.Count];

            // What the model really said at each level, so a gathered branch does not end up with
            // the same name as a value somebody actually authored.
            var authored = new HashSet<string>[levels.Count];

            for (var i = 0; i < levels.Count; i++)
                authored[i] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var elements = 0;
            var dropped = 0;

            foreach (var row in rows ?? Array.Empty<IReadOnlyList<string?>>())
            {
                if (row == null) continue;

                elements++;

                var cells = new Cell[levels.Count];
                var leave = false;

                for (var i = 0; i < levels.Count; i++)
                {
                    // Read into a local and test the local. netstandard2.0's reference assembly
                    // predates the nullable annotations, so IsNullOrWhiteSpace narrows nothing.
                    var value = (i < row.Count ? row[i] : null) ?? string.Empty;
                    var text = value.Trim();

                    if (text.Length > 0)
                    {
                        cells[i] = new Cell(text, true);
                        authored[i].Add(text);
                        continue;
                    }

                    // Counted at the level it went missing, which is the level somebody has to go
                    // and fix. Counting it once at the end says a number without saying where.
                    missing[i]++;

                    if (levels[i].Missing == MissingValues.Leave) { leave = true; break; }

                    cells[i] = new Cell(string.Empty, false);
                }

                if (leave) { dropped++; continue; }

                var key = Key(cells);

                counted[key] = counted.TryGetValue(key, out var seen) ? seen + 1 : 1;
                if (!spelling.ContainsKey(key)) spelling[key] = cells;
            }

            var floor = Math.Max(1, how.Floor);

            var keeping = new List<(Cell[] Cells, int Count)>();
            var small = 0;

            foreach (var pair in counted)
            {
                if (pair.Value >= floor) keeping.Add((spelling[pair.Key], pair.Value));
                else small += pair.Value;
            }

            if (keeping.Count > Math.Max(1, how.Ceiling))
                return Refuse(how,
                    "That would make " + Measure.Count(keeping.Count) + " sets, and the ceiling is "
                    + Measure.Count(Math.Max(1, how.Ceiling))
                    + ". A library that size is impractical to navigate and slow to open. Use fewer "
                    + "levels, raise the floor so small branches are left out, or raise the "
                    + "ceiling deliberately.");

            var warnings = new List<string>();
            var buckets = new string[levels.Count];

            for (var i = 0; i < levels.Count; i++)
            {
                var wanted = levels[i].Bucket;

                // A project that genuinely uses "Not set" as a value would otherwise get two
                // branches with one name - one holding the elements somebody marked, one holding
                // the elements nobody has looked at - and the library would keep whichever was
                // published last.
                if (!authored[i].Contains(wanted)) { buckets[i] = wanted; continue; }

                buckets[i] = wanted + " (no value)";

                warnings.Add("\"" + wanted + "\" is a real value of \"" + levels[i].Property
                             + "\", so the elements carrying nothing there are in \"" + buckets[i]
                             + "\" instead.");
            }

            var named = (how.Root ?? string.Empty).Trim();
            var root = named.Length == 0 ? Array.Empty<string>() : new[] { named };

            var branches = Build(levels, keeping, root, 0, how.NamePattern, buckets);

            // The root is a folder like any other, so it is built rather than special-cased: one
            // branch, holding the tree, with the count of everything under it.
            var roots = root.Length == 0
                ? branches
                : branches.Count == 0
                    ? branches
                    : new List<SetBranch>
                    {
                        new SetBranch(string.Empty, root[0], root[0], root, branches.Sum(b => b.Elements),
                                      branches, null),
                    };

            var placed = keeping.Sum(k => k.Count);

            var tallies = levels
                .Select((level, i) => new LevelTally(level, missing[i]))
                .ToList();

            return new RestructurePlan(how, roots, elements, placed, dropped, small, tallies,
                                       warnings, null);
        }

        /// <summary>
        /// What running it again would change.
        ///
        /// Matched on the set's full name, folders and all, because that is what identifies a set in
        /// the library and what somebody looks for in the tree. A set that has moved folder is a set
        /// that has gone and another that is new, which is exactly what it looks like from the
        /// Selection Tree.
        /// </summary>
        /// <param name="before">The tree as it was, or null on the first run.</param>
        /// <param name="after">The tree as it comes out now.</param>
        public static RestructureDelta Compare(RestructurePlan? before, RestructurePlan? after) =>
            Since(Snapshot(before), after);

        /// <summary>
        /// The same comparison against a run recorded in the project file, which is the one that
        /// matters: the run being compared against happened days ago, in a Navisworks somebody has
        /// since closed.
        ///
        /// A separate name rather than another overload of Compare, because a caller writing
        /// <c>Compare(null, plan)</c> for a first run would otherwise have to say which null.
        /// </summary>
        /// <param name="before">The recorded run, or null when there was none.</param>
        /// <param name="after">The tree as it comes out now.</param>
        public static RestructureDelta Since(RestructureSnapshot? before, RestructurePlan? after)
        {
            var was = (before ?? RestructureSnapshot.Empty).Sizes;
            var now = Snapshot(after).Sizes;

            if (was.Count == 0 && now.Count == 0)
                return new RestructureDelta(Array.Empty<RestructureChange>(), 0);

            var changes = new List<RestructureChange>();
            var same = 0;

            foreach (var name in was.Keys.Concat(now.Keys).Distinct(StringComparer.Ordinal)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                was.TryGetValue(name, out var then);
                now.TryGetValue(name, out var here);

                if (then == here) { same++; continue; }

                changes.Add(new RestructureChange(name, then, here));
            }

            return new RestructureDelta(changes, same);
        }

        /// <summary>The sizes of a run, to keep in the project file until the next one.</summary>
        /// <param name="plan">The run, or null for an empty snapshot.</param>
        /// <param name="whenTicks">When it happened. Core has no clock, so the caller supplies it.</param>
        public static RestructureSnapshot Snapshot(RestructurePlan? plan, long whenTicks = 0)
        {
            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);

            if (plan == null) return whenTicks == 0
                ? RestructureSnapshot.Empty
                : new RestructureSnapshot(sizes, whenTicks);

            foreach (var leaf in plan.Sets) sizes[leaf.FullName] = leaf.Elements;

            return new RestructureSnapshot(sizes, whenTicks);
        }

        /// <summary>One level of the tree, recursively.</summary>
        private static IReadOnlyList<SetBranch> Build(
            IReadOnlyList<TreeLevel> levels, IReadOnlyList<(Cell[] Cells, int Count)> rows,
            IReadOnlyList<string> path, int depth, string pattern, IReadOnlyList<string> buckets)
        {
            var branches = new List<SetBranch>();

            foreach (var group in rows
                         .GroupBy(r => r.Cells[depth])
                         .OrderBy(g => g.Key.IsReal ? 0 : 1)
                         .ThenByDescending(g => g.Sum(r => r.Count))
                         .ThenBy(g => g.Key.Value, StringComparer.OrdinalIgnoreCase))
            {
                var shown = group.Key.IsReal ? group.Key.Value : buckets[depth];
                var here = path.Concat(new[] { shown }).ToList();
                var elements = group.Sum(r => r.Count);

                if (depth == levels.Count - 1)
                {
                    // Written out in full — every level in the path, ANDed — rather than leaning on
                    // the folder it sits in. A set has to mean the same thing after somebody drags
                    // it out of its folder, and in Navisworks they will.
                    var tests = new SetExpression[levels.Count];

                    for (var i = 0; i < levels.Count; i++) tests[i] = Test(levels[i], group, i);

                    branches.Add(new SetBranch(levels[depth].Property, shown,
                        SetTree.Name(pattern, shown, path), here, elements,
                        Array.Empty<SetBranch>(),
                        tests.Length == 1 ? tests[0] : SetExpression.And(tests)));

                    continue;
                }

                // A folder keeps the value as it stands. The pattern names sets, not folders: the
                // same prefix repeated at every level is three times the prefix and none of the point.
                branches.Add(new SetBranch(levels[depth].Property, shown, shown, here, elements,
                    Build(levels, group.ToList(), here, depth + 1, pattern, buckets), null));
            }

            return branches;
        }

        /// <summary>The condition for one level of one leaf.</summary>
        private static SetExpression Test(TreeLevel level,
                                          IEnumerable<(Cell[] Cells, int Count)> rows, int depth)
        {
            var cell = rows.First().Cells[depth];

            if (cell.IsReal)
                return SetExpression.Where(level.Category, level.Property, SetOperator.Equals,
                                           cell.Value);

            // "Carries nothing" is two different questions to a search engine: the property may be
            // absent, or it may be there and empty. The preview counted both, so the published set
            // has to find both, or it comes back with a smaller number than the one on screen.
            return SetExpression.Or(
                SetExpression.Not(SetExpression.Where(level.Category, level.Property,
                                                      SetOperator.Defined)),
                SetExpression.Where(level.Category, level.Property, SetOperator.Equals,
                                    string.Empty));
        }

        /// <summary>Length-prefixed, so ("AB","C") and ("A","BC") are different branches.</summary>
        private static string Key(IReadOnlyList<Cell> cells)
        {
            var parts = new string[cells.Count];

            for (var i = 0; i < cells.Count; i++)
                parts[i] = (cells[i].IsReal ? "=" : "~")
                         + cells[i].Value.Length.ToString(CultureInfo.InvariantCulture)
                         + ":" + cells[i].Value;

            return string.Join(string.Empty, parts);
        }

        private static RestructurePlan Refuse(RestructureRecipe recipe, string why) =>
            new RestructurePlan(recipe, Array.Empty<SetBranch>(), 0, 0, 0, 0,
                                Array.Empty<LevelTally>(), Array.Empty<string>(), why);

        /// <summary>
        /// One level's answer for one element: the text, and whether the model really said it.
        ///
        /// A hole carries no text at all. What its branch ends up called is decided after the read,
        /// once every real value at that level is known — because a project that genuinely uses
        /// "Not set" as a value must not have its marked elements merged with the ones nobody has
        /// looked at.
        /// </summary>
        private readonly struct Cell : IEquatable<Cell>
        {
            internal Cell(string value, bool real)
            {
                Value = value; IsReal = real;
            }

            internal string Value { get; }

            internal bool IsReal { get; }

            public bool Equals(Cell other) =>
                IsReal == other.IsReal && string.Equals(Value, other.Value, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is Cell other && Equals(other);

            public override int GetHashCode() =>
                unchecked(StringComparer.Ordinal.GetHashCode(Value) * 31 + (IsReal ? 1 : 0));

            public override string ToString() => Value;
        }
    }
}
