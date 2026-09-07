using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Checks;

namespace CamelWorks.Core.Search
{
    /// <summary>One thing a search can ask about, as the field picker offers it.</summary>
    public sealed class QueryField
    {
        /// <summary>Declare a field.</summary>
        /// <param name="id">Its id, which is what a saved clause holds.</param>
        /// <param name="title">What the picker calls it.</param>
        /// <param name="value">What kind of value it holds.</param>
        /// <param name="onElement">Whether it is read from an element rather than from the result.</param>
        public QueryField(string id, string title, QueryValue value, bool onElement)
        {
            Id = id;
            Title = title;
            Value = value;
            OnElement = onElement;
        }

        /// <summary>The id a saved clause holds.</summary>
        public string Id { get; }

        /// <summary>What the picker calls it.</summary>
        public string Title { get; }

        /// <summary>Text or number, which decides the operators offered.</summary>
        public QueryValue Value { get; }

        /// <summary>
        /// Whether it is read from an element.
        ///
        /// Only these can be asked of one side, of every side, or across the two, so the scope
        /// picker offers those choices for them and nothing else.
        /// </summary>
        public bool OnElement { get; }

        /// <summary>The unit its numbers are in, where saying so helps. Null when it does not.</summary>
        public string? Unit { get; set; }

        /// <summary>
        /// Whether the field is a measurement across the two elements rather than a reading off
        /// one.
        ///
        /// It has no value on a single element, so it is asked in one scope only — between them —
        /// and it takes the number operators there rather than "is the same on both".
        /// </summary>
        public bool Across { get; set; }

        /// <summary>One line under the picker, where the field needs one. Null when it does not.</summary>
        public string? Hint { get; set; }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// What a search can ask about, and how a clause names it.
    ///
    /// Three families, and only the first is written out here. The other two are read off the check
    /// that produced the result and off the model itself, so a check that starts measuring
    /// something new is searchable on it with nothing added to this file, and a property nobody
    /// anticipated is searchable because it is named rather than enumerated.
    /// </summary>
    public static class ResultFields
    {
        /// <summary>What the result is called.</summary>
        public const string Title = "title";

        /// <summary>Its status, as the lattice words it.</summary>
        public const string Status = "status";

        /// <summary>Who it is with.</summary>
        public const string Assignee = "assignee";

        /// <summary>The group it is in on the board.</summary>
        public const string Group = "group";

        /// <summary>Its priority.</summary>
        public const string Priority = "priority";

        /// <summary>The stage it belongs to.</summary>
        public const string Stage = "stage";

        /// <summary>A label it carries.</summary>
        public const string Label = "label";

        /// <summary>Which check produced it.</summary>
        public const string Source = "source";

        /// <summary>Which run of that check reported it.</summary>
        public const string Run = "run";

        /// <summary>How many remarks it carries.</summary>
        public const string Comments = "comments";

        /// <summary>Which rule of the check it broke.</summary>
        public const string Rule = "rule";

        /// <summary>The issue it is attached to, empty when nothing has been raised for it.</summary>
        public const string Issue = "issue";

        /// <summary>An element's category.</summary>
        public const string Category = "category";

        /// <summary>An element's type or family name.</summary>
        public const string TypeName = "type";

        /// <summary>The model an element came from.</summary>
        public const string Model = "model";

        /// <summary>What an element is called in the tree.</summary>
        public const string ElementName = "name";

        /// <summary>Anywhere in an element's tree path.</summary>
        public const string TreePath = "path";

        /// <summary>Whether an element runs up and down or along the plan.</summary>
        public const string Orientation = "orientation";

        /// <summary>How far from horizontal an element runs, in degrees.</summary>
        public const string Tilt = "tilt";

        /// <summary>The clear distance between the two elements' boxes.</summary>
        public const string Gap = "between:gap";

        /// <summary>The distance between the two elements' centres.</summary>
        public const string Centres = "between:centres";

        /// <summary>The angle between the ways the two elements run.</summary>
        public const string Angle = "between:angle";

        /// <summary>The prefix of a field read from a check's declared column.</summary>
        public const string ColumnPrefix = "column:";

        /// <summary>The prefix of a field read from an element's properties.</summary>
        public const string PropertyPrefix = "property:";

        /// <summary>Name the field that reads one of a check's declared columns.</summary>
        /// <param name="key">The column's key.</param>
        public static string Column(string key) => ColumnPrefix + (key ?? string.Empty);

        /// <summary>Name the field that reads one of an element's properties.</summary>
        /// <param name="category">The property's category, or empty for any category.</param>
        /// <param name="name">The property's name.</param>
        public static string Property(string? category, string name) =>
            PropertyPrefix + (category ?? string.Empty) + "/" + (name ?? string.Empty);

        /// <summary>Take a property field back apart.</summary>
        /// <param name="field">The field id.</param>
        /// <param name="category">Its category, empty for any.</param>
        /// <param name="name">Its name.</param>
        /// <returns>False when the field is not a property field.</returns>
        public static bool ReadProperty(string? field, out string category, out string name)
        {
            category = string.Empty;
            name = string.Empty;

            if (field == null || !field.StartsWith(PropertyPrefix, StringComparison.Ordinal))
                return false;

            var rest = field.Substring(PropertyPrefix.Length);
            var at = rest.IndexOf('/');

            if (at < 0)
            {
                name = rest;
                return name.Length > 0;
            }

            category = rest.Substring(0, at);
            name = rest.Substring(at + 1);

            return name.Length > 0;
        }

        /// <summary>The column key a field names, or null when it names no column.</summary>
        /// <param name="field">The field id.</param>
        public static string? ReadColumn(string? field) =>
            field != null && field.StartsWith(ColumnPrefix, StringComparison.Ordinal)
                ? field.Substring(ColumnPrefix.Length)
                : null;

        /// <summary>The fields every result has, whichever check produced it.</summary>
        public static IReadOnlyList<QueryField> Standing { get; } = new[]
        {
            new QueryField(Title, "Name", QueryValue.Text, false),
            new QueryField(Status, "Status", QueryValue.Text, false),
            new QueryField(Assignee, "Assigned to", QueryValue.Text, false),
            new QueryField(Group, "Group", QueryValue.Text, false),
            new QueryField(Priority, "Priority", QueryValue.Text, false),
            new QueryField(Stage, "Stage", QueryValue.Text, false),
            new QueryField(Label, "Label", QueryValue.Text, false),
            new QueryField(Source, "Check", QueryValue.Text, false),
            new QueryField(Run, "Test", QueryValue.Text, false),
            new QueryField(Rule, "Rule broken", QueryValue.Text, false),
            new QueryField(Issue, "Issue", QueryValue.Text, false),
            new QueryField(Comments, "Remarks", QueryValue.Number, false),
        };

        /// <summary>The fields read from an element, which a scope can be asked of.</summary>
        public static IReadOnlyList<QueryField> OnElements { get; } = new[]
        {
            new QueryField(ElementName, "Element name", QueryValue.Text, true),
            new QueryField(Category, "Category", QueryValue.Text, true),
            new QueryField(TypeName, "Type", QueryValue.Text, true),
            new QueryField(Model, "Model", QueryValue.Text, true),
            new QueryField(TreePath, "Anywhere in the tree path", QueryValue.Text, true),

            new QueryField(Orientation, "Orientation", QueryValue.Text, true)
            {
                Hint = "Vertical or Horizontal, from the element's box. An element that is not "
                       + "clearly longer one way than the other two has no orientation and no "
                       + "clause about it holds.",
            },

            new QueryField(Tilt, "Angle from horizontal", QueryValue.Number, true)
            {
                Unit = "\u00b0",
                Hint = "Zero along the plan, ninety up and down.",
            },
        };

        /// <summary>
        /// What is measured across the two elements rather than read off either.
        ///
        /// From the elements themselves, not from a column: no check declares an angle, and the
        /// question a coordinator asks about a pair — how far apart, at what angle — is about the
        /// pair rather than about what the check happened to measure. The elements are on the
        /// result, so the search measures them.
        /// </summary>
        public static IReadOnlyList<QueryField> Measured { get; } = new[]
        {
            new QueryField(Gap, "Distance between them", QueryValue.Number, true)
            {
                Across = true,
                Unit = "m",
                Hint = "The clear distance between the two elements' boxes, zero when they touch "
                       + "or overlap. A check that measures the solids declares a column for it, "
                       + "and that column is the closer answer.",
            },

            new QueryField(Centres, "Distance between their centres", QueryValue.Number, true)
            {
                Across = true,
                Unit = "m",
            },

            new QueryField(Angle, "Angle between them", QueryValue.Number, true)
            {
                Across = true,
                Unit = "\u00b0",
                Hint = "Zero when they run the same way, ninety when they cross square. Either "
                       + "element that is not clearly longer one way than the other two has no "
                       + "direction, and no clause about the angle holds for it.",
            },
        };

        /// <summary>
        /// Everything a search can ask about the results of one check.
        ///
        /// The check's own columns come between the standing fields and the element ones, because
        /// they are what makes a search about this check rather than about results in general: a
        /// clash offers its overlap and its distance, a clearance its headroom.
        /// </summary>
        /// <param name="check">The check whose results are being searched, or null for none.</param>
        public static IReadOnlyList<QueryField> For(ICheck? check)
        {
            var found = new List<QueryField>(Standing);

            foreach (var column in check?.Columns ?? Array.Empty<ResultColumn>())
            {
                // An element column names one of the finding's elements. Searching it is what the
                // element fields below already do, per side and by name, so it would be the same
                // question asked twice under two headings.
                if (column.Kind == ColumnKind.Element) continue;

                found.Add(new QueryField(
                    Column(column.Key), column.Title,
                    column.Kind == ColumnKind.Number ? QueryValue.Number : QueryValue.Text,
                    false)
                {
                    Unit = column.Unit,
                });
            }

            found.AddRange(OnElements);
            found.AddRange(Measured);

            return found;
        }

        /// <summary>The operators that make sense for a kind of value.</summary>
        /// <param name="value">Text or number.</param>
        /// <param name="between">True for a clause comparing the two sides with each other.</param>
        public static IReadOnlyList<QueryOp> Operators(QueryValue value, bool between)
        {
            if (between) return new[] { QueryOp.Same, QueryOp.Different };

            return value == QueryValue.Number
                ? new[]
                {
                    QueryOp.Equals, QueryOp.NotEquals, QueryOp.LessThan, QueryOp.AtMost,
                    QueryOp.GreaterThan, QueryOp.AtLeast, QueryOp.Between,
                    QueryOp.IsEmpty, QueryOp.IsNotEmpty,
                }
                : new[]
                {
                    QueryOp.Is, QueryOp.IsNot, QueryOp.Contains, QueryOp.DoesNotContain,
                    QueryOp.StartsWith, QueryOp.EndsWith, QueryOp.IsEmpty, QueryOp.IsNotEmpty,
                };
        }

        /// <summary>
        /// The operators one field takes in one scope.
        ///
        /// A measurement across the pair is compared with a number even in the scope that
        /// otherwise offers "is the same on both": there is one reading, not two to compare.
        /// </summary>
        /// <param name="field">The field, or null when it is not one this file knows.</param>
        /// <param name="value">Text or number, for a field this file does not know.</param>
        /// <param name="where">The scope the clause is asked in.</param>
        public static IReadOnlyList<QueryOp> Operators(QueryField? field, QueryValue value,
                                                       QueryWhere where) =>
            Operators(field?.Value ?? value,
                      where == QueryWhere.BetweenElements && field?.Across != true);

        /// <summary>The scopes one field can be asked of.</summary>
        /// <param name="field">The field, or null when it is not one this file knows.</param>
        public static IReadOnlyList<QueryWhere> ScopesFor(QueryField? field) =>
            field?.Across == true
                ? new[] { QueryWhere.BetweenElements }
                : Scopes;

        /// <summary>How an operator reads in a sentence.</summary>
        /// <param name="op">The operator.</param>
        public static string Worded(QueryOp op) =>
            op switch
            {
                QueryOp.Is => "is",
                QueryOp.IsNot => "is not",
                QueryOp.Contains => "contains",
                QueryOp.DoesNotContain => "does not contain",
                QueryOp.StartsWith => "starts with",
                QueryOp.EndsWith => "ends with",
                QueryOp.IsEmpty => "is empty",
                QueryOp.IsNotEmpty => "has a value",
                QueryOp.Equals => "is",
                QueryOp.NotEquals => "is not",
                QueryOp.LessThan => "is under",
                QueryOp.AtMost => "is at most",
                QueryOp.GreaterThan => "is over",
                QueryOp.AtLeast => "is at least",
                QueryOp.Between => "is between",
                QueryOp.Same => "is the same on both",
                QueryOp.Different => "differs between them",
                _ => op.ToString(),
            };

        /// <summary>How a scope reads in a sentence.</summary>
        /// <param name="where">The scope.</param>
        public static string Worded(QueryWhere where) =>
            where switch
            {
                QueryWhere.Result => "the result",
                QueryWhere.AnyElement => "either element",
                QueryWhere.EveryElement => "both elements",
                QueryWhere.FirstElement => "the first element",
                QueryWhere.SecondElement => "the second element",
                QueryWhere.BetweenElements => "between the elements",
                _ => where.ToString(),
            };

        /// <summary>The scopes an element field can be asked of.</summary>
        public static IReadOnlyList<QueryWhere> Scopes { get; } = new[]
        {
            QueryWhere.AnyElement,
            QueryWhere.EveryElement,
            QueryWhere.FirstElement,
            QueryWhere.SecondElement,
            QueryWhere.BetweenElements,
        };
    }
}
