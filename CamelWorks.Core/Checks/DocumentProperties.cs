using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// Element properties read straight off the document, for a check whose findings name ordinary
    /// elements.
    ///
    /// The reader every check without a producer of its own uses. A clash result is held by the
    /// clash engine and its properties come back through that engine; a clearance breach or a fall
    /// hazard names two elements of the model and nothing else holds them, so the only way to a
    /// property is to resolve the key and read what the host has against it.
    ///
    /// Resolving a key that is not an instance GUID indexes the federation's geometry leaves once,
    /// which is why this is called from a job with progress rather than from a cell.
    /// </summary>
    public sealed class DocumentProperties : ICheckProperties
    {
        private readonly IModelDocument _document;

        /// <summary>Read properties out of this document.</summary>
        /// <param name="document">The open document.</param>
        public DocumentProperties(IModelDocument document) =>
            _document = document ?? throw new ArgumentNullException(nameof(document));

        /// <summary>
        /// What one element of one finding says under a property.
        ///
        /// An element index outside the finding's own list reads every element it names, which is
        /// what a column set to "either side" asks for.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="elementIndex">Which of its elements, or any other number for all of them.</param>
        /// <param name="category">The property category.</param>
        /// <param name="name">The property name.</param>
        public IReadOnlyList<PropertyValue> ValuesFor(Finding finding, int elementIndex,
                                                      string category, string name)
        {
            var found = new List<PropertyValue>();

            if (finding == null || string.IsNullOrWhiteSpace(name)) return found;

            var elements = finding.Elements;

            for (var at = 0; at < elements.Count; at++)
            {
                if (elementIndex >= 0 && elementIndex < elements.Count && at != elementIndex) continue;

                var key = elements[at];

                if (key.IsEmpty) continue;

                var item = _document.Resolve(key);

                if (item == null) continue;

                foreach (var value in item.Properties())
                {
                    if (!string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

                    // An empty category means "whichever tab holds it", which is what the column
                    // offers when somebody types a property name without picking a tab.
                    if (!string.IsNullOrWhiteSpace(category)
                        && !string.Equals(value.Category, category, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found.Add(value);
                }
            }

            return found;
        }
    }
}
