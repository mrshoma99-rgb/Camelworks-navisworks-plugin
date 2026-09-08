using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Search
{
    /// <summary>
    /// What values a field can hold, in one place.
    ///
    /// The same vocabularies were derived four ways — the Filter tab's conditions, the Manage
    /// tab's filter and band menus, the check panels, and whatever a search editor's caller
    /// happened to supply — so a status added in one of them appeared in three, and a rule
    /// written against a party the project had stopped listing silently matched nothing.
    ///
    /// Two sources, merged. What the project declares is what a coordinator may choose even
    /// before any result carries it; what the results carry is what is actually on the board,
    /// including the values that arrived from somebody else's markup and were never declared
    /// here. A picker that showed only one of the two is wrong in one direction or the other.
    /// </summary>
    public static class ResultVocabulary
    {
        /// <summary>
        /// The values worth offering for a field.
        /// </summary>
        /// <param name="field">The field, as <see cref="ResultFields"/> names it.</param>
        /// <param name="findings">The results on the board, or null for none.</param>
        /// <param name="profile">The project, for the lists it declares, or null for none.</param>
        /// <returns>The values, once each, in order. Empty when the field is free text.</returns>
        public static IReadOnlyList<string> Of(string? field, IEnumerable<Finding>? findings,
                                               ProjectProfile? profile = null)
        {
            var held = findings?.Where(one => one != null).ToList()
                       ?? new List<Finding>();

            switch (field)
            {
                case ResultFields.Status:
                    // The lattice's own five, in its order rather than alphabetically: a status
                    // list is a progression, and sorting it puts Approved before New.
                    return FindingStatusLattice.Names;

                case ResultFields.Priority:
                    return Both(profile?.Priorities, held.Select(one => one.Priority));

                case ResultFields.Assignee:
                    return Both(profile?.Parties, held.Select(one => one.AssignedTo));

                case ResultFields.Group:
                    return Listed(held.Select(one => one.Group));

                case ResultFields.Stage:
                    return Listed(held.Select(one => one.Stage));

                case ResultFields.Label:
                    return Listed(held.SelectMany(one => one.Labels));

                case ResultFields.Source:
                    return Listed(held.Select(one => one.Source));

                case ResultFields.Rule:
                    return Listed(held.Select(one => one.Rule));

                default:
                    // Free text, or a field whose values only the seat can name — the tests on
                    // the document, the models loaded. Those are the caller's to add, because
                    // Core cannot see them.
                    return Array.Empty<string>();
            }
        }

        /// <summary>
        /// What the project declares, then anything the results carry that it does not.
        ///
        /// The declared order is kept, because a priority list is a ranking and sorting it makes
        /// High follow Critical. What the results add goes after it, in order, so a value nobody
        /// declared is visibly not one of the project's own.
        /// </summary>
        /// <param name="declared">The project's list, or null.</param>
        /// <param name="seen">What the results carry.</param>
        private static IReadOnlyList<string> Both(IEnumerable<string>? declared,
                                                  IEnumerable<string?> seen)
        {
            var held = (declared ?? Array.Empty<string>())
                .Where(one => !string.IsNullOrWhiteSpace(one))
                .Select(one => one.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var one in Listed(seen))
                if (!held.Contains(one, StringComparer.OrdinalIgnoreCase))
                    held.Add(one);

            return held;
        }

        /// <summary>Values with the empties, the repeats and the disorder taken out.</summary>
        /// <param name="values">What was found.</param>
        private static IReadOnlyList<string> Listed(IEnumerable<string?> values) =>
            values.Where(one => !string.IsNullOrWhiteSpace(one))
                  .Select(one => one!.Trim())
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(one => one, StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }
}
