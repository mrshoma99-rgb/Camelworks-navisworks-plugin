using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Sets
{
    /// <summary>
    /// One line of the schedule: a single condition belonging to a named set.
    ///
    /// Rows sharing a set name are ANDed together, so a set needing three conditions is three rows
    /// rather than a nested expression somebody has to build in a tree. That is the shape a
    /// coordinator already has - it is what the spreadsheet they were sent looks like - and the
    /// tree is still there underneath for anything this cannot say.
    /// </summary>
    public sealed class SetRow
    {
        /// <summary>Create a row.</summary>
        /// <param name="name">The set this condition belongs to.</param>
        /// <param name="category">The property tab.</param>
        /// <param name="property">The property name.</param>
        /// <param name="op">How to compare.</param>
        /// <param name="value">What to compare against.</param>
        /// <param name="folder">Which folder to file the set in, or null for the root.</param>
        public SetRow(string name, string category, string? property,
                      SetOperator op = SetOperator.Equals, string? value = null, string? folder = null)
        {
            Name = (name ?? string.Empty).Trim();
            Category = (category ?? string.Empty).Trim();
            Property = (property ?? string.Empty).Trim();
            Operator = op;
            Value = (value ?? string.Empty).Trim();
            Folder = (folder ?? string.Empty).Trim();
        }

        /// <summary>The set this condition belongs to.</summary>
        public string Name { get; }

        /// <summary>The property tab.</summary>
        public string Category { get; }

        /// <summary>The property name.</summary>
        public string Property { get; }

        /// <summary>How to compare.</summary>
        public SetOperator Operator { get; }

        /// <summary>What to compare against.</summary>
        public string Value { get; }

        /// <summary>Which folder to file the set in, or empty for the root.</summary>
        public string Folder { get; }

        /// <summary>True when the row says nothing worth reading.</summary>
        public bool IsBlank =>
            Name.Length == 0 && Category.Length == 0 && Property.Length == 0 && Value.Length == 0;

        /// <inheritdoc />
        public override string ToString() =>
            Name + ": " + Category + "/" + Property + " " + Operator + " " + Value;
    }

    /// <summary>One set the schedule asks for.</summary>
    public sealed class ScheduledSet
    {
        internal ScheduledSet(string name, string? folder, SetExpression expression, int conditions)
        {
            Name = name; Folder = folder; Expression = expression; Conditions = conditions;
        }

        /// <summary>What it will be called.</summary>
        public string Name { get; }

        /// <summary>Which folder it goes in, or null for the root.</summary>
        public string? Folder { get; }

        /// <summary>What it matches.</summary>
        public SetExpression Expression { get; }

        /// <summary>How many rows went into it.</summary>
        public int Conditions { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Folder == null ? Name : Folder + " / " + Name;
    }

    /// <summary>Every set a schedule asks for, and every row that asked for nothing.</summary>
    public sealed class SetPlan
    {
        internal SetPlan(IReadOnlyList<ScheduledSet> sets, IReadOnlyList<string> problems, int rows)
        {
            Sets = sets; Problems = problems; Rows = rows;
        }

        /// <summary>The sets, in the order their first row appeared.</summary>
        public IReadOnlyList<ScheduledSet> Sets { get; }

        /// <summary>Why each row that contributed nothing contributed nothing.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>How many rows were read.</summary>
        public int Rows { get; }

        /// <summary>True when there is nothing to make.</summary>
        public bool IsEmpty => Sets.Count == 0;

        /// <summary>One line saying what the schedule comes to.</summary>
        public string Explain()
        {
            if (Rows == 0) return "The schedule is empty.";

            var line = Sets.Count.ToString(CultureInfo.InvariantCulture)
                       + (Sets.Count == 1 ? " set" : " sets") + " from "
                       + Rows.ToString(CultureInfo.InvariantCulture)
                       + (Rows == 1 ? " row" : " rows");

            var folders = Sets.Select(s => s.Folder).Where(f => f != null)
                              .Distinct(StringComparer.OrdinalIgnoreCase).Count();

            if (folders > 0)
                line += ", in " + folders.ToString(CultureInfo.InvariantCulture)
                        + (folders == 1 ? " folder" : " folders");

            if (Problems.Count > 0)
                line += ", " + Problems.Count.ToString(CultureInfo.InvariantCulture)
                        + (Problems.Count == 1 ? " row" : " rows") + " unusable";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Turns a table of conditions into the sets it describes.
    ///
    /// The set builder could make one set at a time, or a family splitting one property into its
    /// distinct values. Neither is what a project actually needs on day one, which is forty sets
    /// spanning six disciplines, each defined by two or three conditions, filed into folders — a
    /// thing that exists as a spreadsheet and gets turned into search sets by hand, once, badly,
    /// and then again from scratch when the next revision lands.
    ///
    /// Which is the other half: a schedule is data, so it can be saved and run again. The same
    /// table against a new federation rebuilds every set without anybody re-deciding anything.
    /// </summary>
    public static class SetSchedule
    {
        /// <summary>Plan the sets.</summary>
        /// <param name="rows">The table.</param>
        public static SetPlan Plan(IEnumerable<SetRow>? rows)
        {
            var problems = new List<string>();

            if (rows == null) return new SetPlan(Array.Empty<ScheduledSet>(), problems, 0);

            var read = 0;
            var order = new List<string>();
            var grouped = new Dictionary<string, List<SetRow>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (row == null || row.IsBlank) continue;

                read++;

                if (row.Name.Length == 0)
                {
                    problems.Add("A row with conditions but no set name was left out: "
                                 + row.Category + "/" + row.Property + ".");
                    continue;
                }

                if (row.Category.Length == 0 && row.Property.Length == 0)
                {
                    problems.Add("\"" + row.Name + "\" has a row naming neither a category nor a property.");
                    continue;
                }

                // A comparison with nothing to compare against matches everything, which is the one
                // outcome nobody means by an empty cell. Defined and HasCategory are the exceptions:
                // they are about the property existing, so they have no value by design.
                if (row.Value.Length == 0
                    && row.Operator != SetOperator.Defined
                    && row.Operator != SetOperator.HasCategory)
                {
                    problems.Add("\"" + row.Name + "\" has a " + row.Operator
                                 + " row with no value, which would match everything.");
                    continue;
                }

                if (!grouped.TryGetValue(row.Name, out var list))
                {
                    list = new List<SetRow>();
                    grouped[row.Name] = list;
                    order.Add(row.Name);
                }

                list.Add(row);
            }

            var sets = new List<ScheduledSet>();

            foreach (var name in order)
            {
                var list = grouped[name];

                var conditions = list
                    .Select(r => SetExpression.Where(r.Category, r.Property.Length == 0 ? null : r.Property,
                                                     r.Operator, r.Value.Length == 0 ? null : r.Value))
                    .ToArray();

                // The folder comes from the first row that names one. A set whose rows disagree
                // about where it is filed is a typo, not a feature, and picking the first is the
                // answer that does not silently move somebody's set on the next run.
                var folder = list.Select(r => r.Folder).FirstOrDefault(f => f.Length > 0);

                var disagree = list.Select(r => r.Folder).Where(f => f.Length > 0)
                                   .Distinct(StringComparer.OrdinalIgnoreCase).Count();

                if (disagree > 1)
                    problems.Add("\"" + name + "\" is filed in " + disagree.ToString(CultureInfo.InvariantCulture)
                                 + " different folders by its rows. The first was used.");

                sets.Add(new ScheduledSet(name, folder, SetExpression.And(conditions), conditions.Length));
            }

            return new SetPlan(sets, problems, read);
        }
    }
}
