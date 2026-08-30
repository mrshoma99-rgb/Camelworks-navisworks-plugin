using System;
using System.Collections.Generic;
using CamelWorks.Core.Clash;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// Puts the IFC GlobalIds on clash items on the way out of the door.
    ///
    /// <b>Why an export does this at all.</b> BCF names elements by their IFC GlobalId and by
    /// nothing else. A clash file written without them reaches the architect's tool as an issue
    /// with a camera and an empty selection: they are flown to roughly the right place and shown
    /// nothing highlighted, which is the difference between a file somebody works from and a file
    /// somebody scrolls past.
    ///
    /// <b>Why it is not free.</b> The GlobalId is a property on the element, so finding it means
    /// resolving the element and reading its properties. The document caches per element, so the
    /// cost is one read per distinct element rather than one per clash - a federation with four
    /// hundred ducts in eighteen thousand clashes pays four hundred times, not thirty-six thousand.
    /// It still reports progress, because on a first export of a large board it is the slow part.
    ///
    /// A federation with no IFC in it pays the cost once and finds nothing, which is the honest
    /// outcome: the export then says how many components a receiving tool will not be able to
    /// select, rather than writing an id that names nothing.
    /// </summary>
    internal static class ClashIdentity
    {
        /// <summary>
        /// Stamp every item with the GlobalIds of its two elements.
        /// </summary>
        /// <param name="session">The open document.</param>
        /// <param name="items">The items, edited in place.</param>
        /// <param name="job">Progress, and the way to stop.</param>
        /// <returns>How many of the two-per-item ids were found.</returns>
        internal static int Stamp(Session? session, IReadOnlyList<ClashItem>? items, Job? job)
        {
            if (session == null || items == null || items.Count == 0) return 0;

            var found = 0;
            var seen = 0;

            job?.Say("Reading IFC ids, so the receiving tool can select the elements...");

            foreach (var item in items)
            {
                if (job != null && !job.Step(++seen, items.Count, "elements read")) break;

                if (item == null) continue;

                try
                {
                    item.IfcGuidA ??= session.Model.IfcGuidFor(item.Key.A);
                    item.IfcGuidB ??= session.Model.IfcGuidFor(item.Key.B);
                }
                catch (Exception)
                {
                    // An element the host will not read is an element with no GlobalId. Never a
                    // reason to fail the export it was only trying to improve.
                    continue;
                }

                if (item.IfcGuidA != null) found++;
                if (item.IfcGuidB != null) found++;
            }

            return found;
        }

        /// <summary>What to say about it, or null when there is nothing worth saying.</summary>
        /// <param name="items">How many items were written.</param>
        /// <param name="found">What <see cref="Stamp"/> returned.</param>
        internal static string? Explain(int items, int found)
        {
            if (items <= 0) return null;

            var wanted = items * 2;

            if (found >= wanted)
                return "Every element carries an IFC id, so the receiving tool can select both "
                       + "sides of every clash.";

            if (found == 0)
                return "No element in this model carries an IFC id. The file still opens in any "
                       + "coordination tool, with the camera on each clash - but the two clashing "
                       + "elements will not be selectable there. Elements from IFC sources carry "
                       + "one; those from Revit, DWG and native formats generally do not.";

            return Ui.Count(wanted - found, "element")
                   + " of " + wanted.ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + " carry no IFC id, so the receiving tool will not be able to select "
                   + (wanted - found == 1 ? "it." : "them.");
        }
    }
}
