using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// The defaults a comparison runs on when nobody has configured anything.
    ///
    /// Every value here is chosen so that the first run on a raw federation produces a real answer.
    /// The alternative — asking which properties matter, at what tolerance, against which baseline
    /// — is a form somebody fills in once, gets wrong, and then distrusts the results of.
    /// </summary>
    public static class ComparePolicy
    {
        /// <summary>How long a recorded property value may be before it is truncated.</summary>
        public const int MaxValueLength = 160;

        /// <summary>
        /// How many watched properties are kept per element.
        ///
        /// A ceiling, not a target. Under <see cref="PropertyWatch.Standard"/> nothing reaches it;
        /// under <see cref="PropertyWatch.All"/> it stops one pathological element — a Revit
        /// assembly carrying every shared parameter on the job — from being a third of the file.
        /// </summary>
        public const int MaxWatchedPerElement = 96;

        /// <summary>
        /// The default position tolerance, in the document's own length unit.
        ///
        /// One millimetre in a metric model. Below the smallest move anybody models on purpose and
        /// above the noise a re-export puts in the last bits of a coordinate — which is the number
        /// that matters, because a tolerance of zero reports a whole model as moved every week and
        /// is then switched off for good.
        /// </summary>
        public const double DefaultTolerance = 0.001;

        /// <summary>How many example rows a result keeps per change kind for the screen.</summary>
        public const int DefaultExampleCap = 500;

        /// <summary>
        /// Property names worth watching, matched in any category, case-insensitively.
        ///
        /// Names rather than <c>Category / Name</c> pairs on purpose. The same fact is filed under
        /// <c>Element</c> from a Revit NWC, under <c>IFC</c> from an IFC import and under
        /// <c>Entity Handle</c> from a DWG, and a list of pairs would work on exactly one of the
        /// three. A model carrying none of these still compares fully on geometry and identity.
        /// </summary>
        public static IReadOnlyList<string> StandardNames { get; } = new[]
        {
            "Area",
            "Assembly Code",
            "Base Constraint",
            "Base Offset",
            "Comments",
            "Description",
            "Design Option",
            "Diameter",
            "Elevation",
            "Family",
            "Family and Type",
            "Fire Rating",
            "Height",
            "Insulation Thickness",
            "Insulation Type",
            "Length",
            "Level",
            "Manufacturer",
            "Mark",
            "Material",
            "Phase Created",
            "Phase Demolished",
            "Reference Level",
            "Size",
            "Structural Material",
            "System Classification",
            "System Name",
            "System Type",
            "Thickness",
            "Type Mark",
            "Type Name",
            "Volume",
            "Width",
            "Workset",
        };

        /// <summary>
        /// Properties that change on every export whatever anybody did to the model.
        ///
        /// Denied under <see cref="PropertyWatch.Standard"/> and <see cref="PropertyWatch.All"/>
        /// alike, because a comparison that reports two hundred thousand elements as changed
        /// because the source path moved is one nobody reads twice. A name of <c>*</c> denies the
        /// whole category.
        /// </summary>
        public static IReadOnlyList<PropertyRef> NoisyByDefault { get; } = new[]
        {
            new PropertyRef("Item", "Source File"),
            new PropertyRef("Item", "Source File Name"),
            new PropertyRef("Item", "File Name"),
            new PropertyRef("Item", "GUID"),
            new PropertyRef("Item", "Icon"),
            new PropertyRef("Item", "Hidden"),
            new PropertyRef("Item", "Required"),

            // Navisworks' own tessellation counts. They move when the exporter's version moves,
            // which is not a fact about the building.
            new PropertyRef("Geometry", "*"),
        };

        /// <summary>Cut a value down to <see cref="MaxValueLength"/>, marking it when cut.</summary>
        /// <param name="value">The value as read.</param>
        public static string Trim(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value!.Length <= MaxValueLength) return value;

            return value.Substring(0, MaxValueLength - 1) + "…";
        }

        /// <summary>True when a reference is covered by one of the entries, honouring <c>*</c>.</summary>
        /// <param name="entries">The list to test against.</param>
        /// <param name="property">The property in question.</param>
        public static bool Covers(IEnumerable<PropertyRef>? entries, PropertyRef property)
        {
            if (entries == null) return false;

            foreach (var entry in entries)
            {
                var categoryMatches =
                    entry.Category.Length == 0
                    || entry.Category == "*"
                    || string.Equals(entry.Category, property.Category, StringComparison.OrdinalIgnoreCase);

                if (!categoryMatches) continue;

                if (entry.Name.Length == 0 || entry.Name == "*") return true;

                if (string.Equals(entry.Name, property.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The standard names as references matching any category.
        ///
        /// Declared AFTER <see cref="StandardNames"/> and not before it. Static initialisers run in
        /// the order they are written, so a list built here above its own source would read an
        /// empty one and every standard property would silently stop being watched.
        /// </summary>
        public static IReadOnlyList<PropertyRef> StandardWatch { get; } =
            StandardNames.Select(n => new PropertyRef(null, n)).ToList();

        /// <summary>A count as every screen and report in the product prints one.</summary>
        /// <param name="value">The count.</param>
        public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
