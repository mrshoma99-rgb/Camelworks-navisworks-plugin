using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Sets
{
    /// <summary>One way in which two elements might be alike.</summary>
    public sealed class Facet
    {
        internal Facet(string category, string name, string? value, bool suggested)
        {
            Category = category; Name = name; Value = value; Suggested = suggested;
        }

        /// <summary>The property tab.</summary>
        public string Category { get; }

        /// <summary>The property name.</summary>
        public string Name { get; }

        /// <summary>What the picked element holds for it.</summary>
        public string? Value { get; }

        /// <summary>
        /// Whether this one starts ticked.
        ///
        /// Ticking everything matches only the element you picked, which is not a selection.
        /// Ticking nothing matches the whole model. The default has to be a guess, and the guess
        /// is the handful of properties describing what a thing IS rather than which one it is.
        /// </summary>
        public bool Suggested { get; }

        /// <summary>True when the element carries no value for it.</summary>
        public bool IsBlank => string.IsNullOrWhiteSpace(Value);

        /// <inheritdoc />
        public override string ToString() =>
            Category + "/" + Name + " = " + (Value ?? "(blank)");
    }

    /// <summary>
    /// Find everything like this one.
    ///
    /// The question people actually ask a model, and the one that sends them into the search dialog
    /// to rebuild by hand what they already have selected. They have an element in front of them; it
    /// carries thirty properties; they want the four that matter and everything matching those.
    ///
    /// So the job is not to guess what similar means. It is to lay out every way two things could be
    /// alike, tick the few that usually matter, and let the answer be corrected in one click rather
    /// than rebuilt from nothing.
    /// </summary>
    public static class Similarity
    {
        /// <summary>
        /// Properties describing what a thing is, rather than which one it is.
        ///
        /// Ordered, because the list is shown in this order and the first three are what somebody
        /// checks before they stop reading. Matched case-insensitively and by suffix, so both
        /// "Revit Type" and "Type" count.
        /// </summary>
        public static readonly IReadOnlyList<string> Describing = new[]
        {
            "Category", "Type", "Family", "System Type", "System Classification",
            "Material", "Classification", "Layer", "Discipline",
        };

        /// <summary>Names identifying one element rather than describing a kind of element.</summary>
        private static readonly string[] Identifying =
        {
            "Id", "GUID", "Guid", "Element ID", "ElementId", "Handle", "Mark", "Name",
            "Unique ID", "UniqueId", "Serial", "Tag Number",
        };

        /// <summary>Every way an element could be matched, with the describing ones ticked.</summary>
        /// <param name="properties">The picked element's properties.</param>
        public static IReadOnlyList<Facet> Facets(IEnumerable<PropertyValue>? properties)
        {
            var facets = new List<Facet>();
            if (properties == null) return facets;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in properties)
            {
                var category = property.Category ?? string.Empty;
                var name = property.Name ?? string.Empty;

                if (name.Length == 0) continue;
                if (!seen.Add(category + " " + name)) continue;

                facets.Add(new Facet(category, name, property.Value,
                                     !Identifies(name) && Describes(name)));
            }

            // Describing first, then the rest, each alphabetically. Thirty properties in host order
            // buries the four that matter somewhere in the middle.
            return facets
                .OrderByDescending(f => f.Suggested)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>What matches everything alike in the chosen ways.</summary>
        /// <param name="chosen">The facets to match on.</param>
        public static SetExpression Like(IEnumerable<Facet>? chosen)
        {
            var parts = new List<SetExpression>();

            foreach (var facet in chosen ?? Enumerable.Empty<Facet>())
            {
                if (facet == null) continue;

                // A blank facet asks for "also has nothing here", which the host cannot express as
                // an equality and which nobody means by "like this one". Matching on the property
                // merely existing is the honest reading.
                parts.Add(facet.IsBlank
                    ? SetExpression.Where(facet.Category, facet.Name, SetOperator.Defined)
                    : SetExpression.Where(facet.Category, facet.Name, SetOperator.Equals, facet.Value));
            }

            // Nothing ticked matches the whole model, which is a selection nobody asked for.
            return parts.Count == 0 ? SetExpression.Nothing : SetExpression.And(parts.ToArray());
        }

        /// <summary>One line describing what a set of facets asks for.</summary>
        /// <param name="chosen">The facets.</param>
        public static string Explain(IReadOnlyCollection<Facet>? chosen)
        {
            if (chosen == null || chosen.Count == 0)
                return "Nothing is ticked, so nothing would be found. Tick what has to match.";

            var named = chosen.Take(3).Select(f => f.Name);

            var line = "Everything with the same " + string.Join(", ", named);

            if (chosen.Count > 3)
                line += " and " + (chosen.Count - 3).ToString(CultureInfo.InvariantCulture) + " more";

            return line + ".";
        }

        private static bool Describes(string name) =>
            Describing.Any(d => name.Equals(d, StringComparison.OrdinalIgnoreCase)
                                || name.EndsWith(" " + d, StringComparison.OrdinalIgnoreCase));

        private static bool Identifies(string name) =>
            Identifying.Any(i => name.Equals(i, StringComparison.OrdinalIgnoreCase));
    }
}
