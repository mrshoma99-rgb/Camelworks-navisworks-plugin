using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Sets
{
    /// <summary>One level of a generated family: a folder, or a set at the bottom.</summary>
    public sealed class SetBranch
    {
        internal SetBranch(string property, string value, string name, IReadOnlyList<string> path,
                           int elements, IReadOnlyList<SetBranch> children, SetExpression? expression)
        {
            Property = property;
            Value = value;
            Name = name;
            Path = path;
            Elements = elements;
            Children = children;
            Expression = expression;
        }

        /// <summary>Which property this level splits on.</summary>
        public string Property { get; }

        /// <summary>The value this branch is for.</summary>
        public string Value { get; }

        /// <summary>What the set or folder is called, after the name pattern.</summary>
        public string Name { get; }

        /// <summary>The folder path from the root, this branch's own value last.</summary>
        public IReadOnlyList<string> Path { get; }

        /// <summary>How many elements fall under it.</summary>
        public int Elements { get; }

        /// <summary>The branches below, empty at a leaf.</summary>
        public IReadOnlyList<SetBranch> Children { get; }

        /// <summary>What the set searches for, at a leaf. Null on a folder.</summary>
        public SetExpression? Expression { get; }

        /// <summary>True when this is a set rather than a folder.</summary>
        public bool IsLeaf => Expression != null;

        /// <summary>The folder this set goes in, or null at the top.</summary>
        public string? Folder =>
            Path.Count < 2 ? null : string.Join("/", Path.Take(Path.Count - 1));

        /// <summary>
        /// The name with its folders in front of it.
        ///
        /// The host has real folders; the CamelWorks library is one flat list. A set called
        /// "Ducts" under Level 01 and another called "Ducts" under Level 02 are the same name in
        /// that list, and the second would overwrite the first. So the library keeps the long name
        /// and the host folder keeps the short one.
        /// </summary>
        public string FullName =>
            Path.Count < 2
                ? Name
                : string.Join(" / ", Path.Take(Path.Count - 1)) + " / " + Name;

        /// <summary>Every leaf under this branch, including itself when it is one.</summary>
        public IEnumerable<SetBranch> Leaves()
        {
            if (IsLeaf) { yield return this; yield break; }

            foreach (var child in Children)
                foreach (var leaf in child.Leaves())
                    yield return leaf;
        }

        /// <inheritdoc />
        public override string ToString() =>
            string.Join("/", Path) + " (" + Elements.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>What to do with an ordered list of properties.</summary>
    public sealed class SetTreeOptions
    {
        /// <summary>Sets with fewer elements than this are not proposed.</summary>
        public int Floor { get; set; } = 1;

        /// <summary>
        /// The most sets this will propose before refusing outright.
        ///
        /// A refusal, not a cap: generating four thousand search sets does not produce a useful
        /// library, it produces a Selection Tree nobody can scroll and a document that takes a
        /// minute to open. Silently making the first two hundred would be worse — the person would
        /// think it had worked.
        /// </summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>The default refusal ceiling.</summary>
        public const int DefaultCeiling = 500;

        /// <summary>What a set is called. <c>{value}</c> and <c>{path}</c> are replaced.</summary>
        public string NamePattern { get; set; } = "{value}";
    }

    /// <summary>A whole family of sets, arranged in folders.</summary>
    public sealed class SetTreePlan
    {
        internal SetTreePlan(string category, IReadOnlyList<string> properties,
                             IReadOnlyList<SetBranch> roots, int elements, int blanks, string? refusal)
        {
            Category = category;
            Properties = properties;
            Roots = roots;
            Elements = elements;
            Blanks = blanks;
            Refusal = refusal;
        }

        /// <summary>The property category everything is read from.</summary>
        public string Category { get; }

        /// <summary>The properties, outermost folder first.</summary>
        public IReadOnlyList<string> Properties { get; }

        /// <summary>The top-level branches.</summary>
        public IReadOnlyList<SetBranch> Roots { get; }

        /// <summary>How many elements went into it.</summary>
        public int Elements { get; }

        /// <summary>How many elements carried nothing for one of the properties.</summary>
        public int Blanks { get; }

        /// <summary>Why nothing is proposed, or null when something is.</summary>
        public string? Refusal { get; }

        /// <summary>Every set that would be made.</summary>
        public IReadOnlyList<SetBranch> Sets =>
            Roots.SelectMany(r => r.Leaves()).ToList();

        /// <summary>How many folders would be made.</summary>
        public int Folders => Roots.Sum(Count);

        /// <summary>True when there is something to make.</summary>
        public bool IsUsable => Refusal == null && Roots.Count > 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            if (Roots.Count == 0)
                return "nothing in scope carries "
                     + string.Join(" and ", Properties.Select(p => "\"" + p + "\""));

            var sets = Sets.Count;

            var s = Measure.Count(sets) + (sets == 1 ? " set" : " sets");

            if (Folders > 0) s += " in " + Measure.Count(Folders)
                                 + (Folders == 1 ? " folder" : " folders");

            s += ", covering " + Measure.Count(Sets.Sum(l => l.Elements))
               + " of " + Measure.Count(Elements) + " elements";

            if (Blanks > 0)
                s += " - " + Measure.Count(Blanks)
                   + " carry nothing for one of the properties and are in none of them";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        private static int Count(SetBranch branch) =>
            branch.IsLeaf ? 0 : 1 + branch.Children.Sum(Count);
    }

    /// <summary>
    /// A whole set library from an ordered list of properties.
    ///
    /// Level, then discipline, then category, and out comes a folder per level holding a folder per
    /// discipline holding a set per category — which is what every project's Selection Tree looks
    /// like and what somebody otherwise builds by hand over an afternoon, once per project, badly.
    ///
    /// <b>Three deep, and no further.</b> Not an arbitrary limit: four properties on a real
    /// federation is thousands of sets, most of them holding one element, in a tree nobody can
    /// navigate. The fourth level people actually want is a filter, not a folder.
    ///
    /// <b>It refuses rather than truncates.</b> Past the ceiling it makes nothing and says how many
    /// it would have made. Quietly making the first two hundred would leave somebody thinking it
    /// had worked, with a library that is a subset of the one they asked for and no indication
    /// which part is missing.
    /// </summary>
    public static class SetTree
    {
        /// <summary>How many properties deep a family may go.</summary>
        public const int MaxDepth = 3;

        /// <summary>
        /// Work out the family.
        /// </summary>
        /// <param name="category">The property category everything is read from.</param>
        /// <param name="properties">The properties, outermost folder first.</param>
        /// <param name="rows">One row per element: its value for each property, in the same order.</param>
        /// <param name="options">Floor, ceiling and naming.</param>
        public static SetTreePlan Of(string category, IReadOnlyList<string>? properties,
                                     IEnumerable<IReadOnlyList<string?>>? rows,
                                     SetTreeOptions? options = null)
        {
            options ??= new SetTreeOptions();

            var levels = (properties ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (levels.Count == 0)
                return Refuse(category, levels, "Pick at least one property to split on.");

            if (levels.Count > MaxDepth)
                return Refuse(category, levels,
                    "Three properties is as deep as this goes, and you have picked "
                    + levels.Count.ToString(CultureInfo.InvariantCulture)
                    + ". A fourth level on a real federation produces thousands of sets, most "
                    + "holding one element. Use a filter rather than a further folder level.");

            var counted = new Dictionary<string, int>(StringComparer.Ordinal);
            var spelling = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var elements = 0;
            var blanks = 0;

            foreach (var row in rows ?? Array.Empty<IReadOnlyList<string?>>())
            {
                if (row == null) continue;

                elements++;

                var values = new List<string>(levels.Count);
                var missing = false;

                for (var i = 0; i < levels.Count; i++)
                {
                    var value = i < row.Count ? row[i] : null;

                    if (string.IsNullOrWhiteSpace(value)) { missing = true; break; }

                    values.Add(value!.Trim());
                }

                // Counted and left out, not filed under "(none)". Two reasons, and the second is
                // the one that matters. A set of elements with no level cannot be written as a
                // search the host will run - "the property is absent" and "the property is there
                // and empty" are different questions, and the preview would promise a count the
                // published set could not find. And an element with no level is one somebody has
                // to go and look at; collecting them into a tidy set is how that gap stops looking
                // like a gap.
                if (missing) { blanks++; continue; }

                // Length-prefixed, so ("AB","C") and ("A","BC") are different branches.
                var key = string.Join(string.Empty,
                    values.Select(v => v.Length.ToString(CultureInfo.InvariantCulture) + ":" + v));

                counted[key] = counted.TryGetValue(key, out var seen) ? seen + 1 : 1;
                if (!spelling.ContainsKey(key)) spelling[key] = values;
            }

            var leaves = counted
                .Where(c => c.Value >= Math.Max(1, options.Floor))
                .Select(c => (Values: spelling[c.Key], Count: c.Value))
                .ToList();

            if (leaves.Count > Math.Max(1, options.Ceiling))
                return Refuse(category, levels,
                    "That would make " + Measure.Count(leaves.Count) + " sets, and the ceiling is "
                    + Measure.Count(options.Ceiling) + ". A library that size is impractical to "
                    + "navigate and slow to open. Split on fewer properties, raise the floor so "
                    + "small sets are left out, or raise the ceiling deliberately.");

            var roots = Build(category, levels, leaves, Array.Empty<string>(), 0, options.NamePattern);

            return new SetTreePlan(category, levels, roots, elements, blanks, null);
        }

        /// <summary>One level of the tree, recursively.</summary>
        private static IReadOnlyList<SetBranch> Build(
            string category, IReadOnlyList<string> levels,
            IReadOnlyList<(IReadOnlyList<string> Values, int Count)> rows,
            IReadOnlyList<string> path, int depth, string pattern)
        {
            var branches = new List<SetBranch>();

            foreach (var group in rows
                         .GroupBy(r => r.Values[depth], StringComparer.Ordinal)
                         .OrderByDescending(g => g.Sum(r => r.Count))
                         .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var here = path.Concat(new[] { group.Key }).ToList();
                var elements = group.Sum(r => r.Count);

                if (depth == levels.Count - 1)
                {
                    // A leaf: every property in the path, ANDed. Written out in full rather than
                    // relying on the folder, because a set has to mean the same thing when somebody
                    // drags it out of the folder.
                    var conditions = levels
                        .Select((property, i) => SetExpression.Where(
                            category, property, SetOperator.Equals, here[i]))
                        .ToArray();

                    branches.Add(new SetBranch(levels[depth], group.Key,
                        Name(pattern, group.Key, path), here, elements,
                        Array.Empty<SetBranch>(),
                        conditions.Length == 1 ? conditions[0] : SetExpression.And(conditions)));

                    continue;
                }

                // A folder keeps the value as it stands. The pattern names sets, not folders:
                // a pattern of "MEP - {value}" on every level would read "MEP - Level 01 / MEP -
                // Mechanical / MEP - Ducts", which is three times the prefix and none of the point.
                branches.Add(new SetBranch(levels[depth], group.Key, group.Key, here, elements,
                    Build(category, levels, group.ToList(), here, depth + 1, pattern), null));
            }

            return branches;
        }

        /// <summary>
        /// What one set is called.
        /// </summary>
        /// <param name="pattern">The template. <c>{value}</c> and <c>{path}</c> are replaced.</param>
        /// <param name="value">The leaf's value.</param>
        /// <param name="folders">The folders above it, outermost first.</param>
        public static string Name(string? pattern, string? value, IReadOnlyList<string>? folders)
        {
            var text = string.IsNullOrWhiteSpace(pattern) ? "{value}" : pattern!;

            var named = text
                .Replace("{value}", value ?? string.Empty)
                .Replace("{path}", folders == null || folders.Count == 0
                    ? string.Empty
                    : string.Join(" / ", folders))
                .Trim();

            // A pattern of "{path} - {value}" at the top level leaves a dangling separator, and a
            // set called "- Ducts" looks like a mistake because it is one.
            named = named.Trim('-', '/', ' ').Trim();

            return named.Length == 0 ? (value ?? string.Empty) : named;
        }

        private static SetTreePlan Refuse(string category, IReadOnlyList<string> levels, string why) =>
            new SetTreePlan(category, levels, Array.Empty<SetBranch>(), 0, 0, why);

        /// <summary>
        /// The formula written into the set's own comment.
        ///
        /// So a colleague without CamelWorks can read what the set is, in Navisworks, from the
        /// properties panel. A generated set that nobody can explain a fortnight later is a set
        /// nobody trusts.
        /// </summary>
        /// <param name="leaf">The set.</param>
        public static string Comment(SetBranch? leaf) =>
            leaf?.Expression == null
                ? string.Empty
                : "CamelWorks: " + leaf.Expression.Describe();
    }
}
