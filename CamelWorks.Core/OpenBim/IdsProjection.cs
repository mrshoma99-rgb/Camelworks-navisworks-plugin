using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// Where a property lives: the tab it appears under, and its name.
    ///
    /// Comparison ignores case on both halves, because the same property arrives spelled three
    /// ways across a federation and none of them is the wrong one.
    /// </summary>
    public readonly struct PropertyAddress : IEquatable<PropertyAddress>
    {
        /// <summary>How the two halves are written when a path is shown or typed.</summary>
        public const string Separator = " / ";

        /// <summary>Create a path.</summary>
        private readonly string? _category;
        private readonly string? _name;

        public PropertyAddress(string category, string name)
        {
            _category = category;
            _name = name;
        }

        /// <summary>
        /// The tab the property appears under.
        ///
        /// Read through a field so <c>default(PropertyAddress)</c> gives the string this promises,
        /// which is what <see cref="IsEmpty"/> just below assumes.
        /// </summary>
        public string Category => _category ?? string.Empty;

        /// <summary>The property name.</summary>
        public string Name => _name ?? string.Empty;

        /// <summary>True when this path names nothing.</summary>
        public bool IsEmpty => Name.Length == 0;

        /// <summary>Read a path a person typed. Returns null when there is no name in it.</summary>
        public static PropertyAddress? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var cut = text!.LastIndexOf('/');

            if (cut < 0) return new PropertyAddress(string.Empty, text.Trim());

            var category = text.Substring(0, cut).Trim();
            var name = text.Substring(cut + 1).Trim();

            return name.Length == 0 ? (PropertyAddress?)null : new PropertyAddress(category, name);
        }

        /// <summary>
        /// True when <paramref name="value"/> is this property. An empty category matches any tab,
        /// so somebody who only knows the property name still gets an answer.
        /// </summary>
        public bool Holds(PropertyValue value) =>
            string.Equals(Name, value.Name, StringComparison.OrdinalIgnoreCase)
            && (Category.Length == 0 || string.Equals(Category, value.Category, StringComparison.OrdinalIgnoreCase));

        /// <inheritdoc />
        public bool Equals(PropertyAddress other) =>
            string.Equals(Category, other.Category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PropertyAddress other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() =>
            (StringComparer.OrdinalIgnoreCase.GetHashCode(Category) * 397)
            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

        /// <summary>Equality.</summary>
        public static bool operator ==(PropertyAddress left, PropertyAddress right) => left.Equals(right);

        /// <summary>Inequality.</summary>
        public static bool operator !=(PropertyAddress left, PropertyAddress right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Category.Length == 0 ? Name : Category + Separator + Name;
    }

    /// <summary>
    /// Turns elements of a Navisworks federation into the flattened shape <see cref="IdsChecker"/>
    /// reads.
    ///
    /// This is the whole difficulty of running an IDS against a federation rather than against an
    /// IFC file. IDS is written in IFC's vocabulary — entity, attribute, property set — and a
    /// federation is a pile of NWCs whose properties came from Revit, Tekla, Civil 3D and, only
    /// sometimes, IFC. Three decisions follow from that, and all three are visible to the person
    /// running the check rather than buried:
    ///
    /// <list type="number">
    /// <item>The IFC class is looked for in the places the readers actually put it, and a candidate
    /// is only believed if it looks like an IFC class. <c>Item / Type</c> on a Revit export says
    /// "Basic Wall", and treating that as an entity name would make every report meaningless.</item>
    /// <item>Only the data the IDS asks about is carried across. A federation has tens of millions
    /// of property readings and an IDS names a handful; projecting the rest would cost hundreds of
    /// megabytes to be ignored.</item>
    /// <item>An element carrying nothing the IDS mentions is dropped rather than counted. It could
    /// never be applicable to any specification, so keeping it would only inflate the total.</item>
    /// </list>
    ///
    /// Nothing here has to be configured for the check to run. When the class cannot be found the
    /// projection reports which properties looked like they held one, so the answer is a sentence
    /// on screen rather than an afternoon of guessing.
    /// </summary>
    public sealed class IdsProjection
    {
        /// <summary>How many candidate class-bearing properties are remembered for the report.</summary>
        public const int MaxSuggestions = 12;

        /// <summary>The prefix every IFC class name starts with.</summary>
        public const string IfcPrefix = "Ifc";

        /// <summary>The IFC attributes an IDS is written against, as the readers spell them.</summary>
        private static readonly Dictionary<string, string> AttributeNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "GlobalId", "GlobalId" },
                { "IFC GUID", "GlobalId" },
                { "Name", "Name" },
                { "Description", "Description" },
                { "ObjectType", "ObjectType" },
                { "Object Type", "ObjectType" },
                { "Tag", "Tag" },
                { "LongName", "LongName" },
                { "Long Name", "LongName" },
            };

        /// <summary>Where the IFC class is looked for, best first.</summary>
        private static readonly PropertyAddress[] TypeLadder =
        {
            new PropertyAddress("IFC", "Entity"),
            new PropertyAddress("IFC", "Type"),
            new PropertyAddress("IFC", "Class"),
            new PropertyAddress("Element", "IfcType"),
            new PropertyAddress("Element", "IFC Type"),
            new PropertyAddress("Element", "Entity"),
            new PropertyAddress("Element", "Type"),
            new PropertyAddress("Item", "Type"),
            new PropertyAddress("Item", "Class"),
        };

        private static readonly string[] PredefinedNames = { "PredefinedType", "Predefined Type" };
        private static readonly string[] MaterialNames = { "Material", "Materials" };

        private readonly List<IdsPropertyFacet> _properties = new List<IdsPropertyFacet>();
        private readonly List<IdsClassificationFacet> _classifications = new List<IdsClassificationFacet>();
        private readonly Dictionary<PropertyAddress, int> _candidates = new Dictionary<PropertyAddress, int>();
        private readonly PropertyAddress? _typeSource;
        private readonly bool _wantsAttributes;
        private readonly bool _wantsMaterials;

        private IdsProjection(IdsDocument document, PropertyAddress? typeSource)
        {
            _typeSource = typeSource;

            foreach (var facet in Facets(document))
            {
                switch (facet)
                {
                    case IdsPropertyFacet property: _properties.Add(property); break;
                    case IdsClassificationFacet classification: _classifications.Add(classification); break;
                    case IdsAttributeFacet _: _wantsAttributes = true; break;
                    case IdsMaterialFacet _: _wantsMaterials = true; break;
                }
            }
        }

        /// <summary>How many elements were offered.</summary>
        public int Read { get; private set; }

        /// <summary>How many carried something this IDS asks about, and so were checked.</summary>
        public int Kept { get; private set; }

        /// <summary>How many of the kept elements carried an IFC class.</summary>
        public int Classed { get; private set; }

        /// <summary>
        /// Properties whose values looked like IFC class names, most common first.
        ///
        /// Only interesting when <see cref="Classed"/> is zero: it turns "nothing applied and I
        /// don't know why" into "your class is in this property, point me at it".
        /// </summary>
        public IReadOnlyList<PropertyAddress> Candidates =>
            _candidates.OrderByDescending(c => c.Value).Take(MaxSuggestions).Select(c => c.Key).ToList();

        /// <summary>Build a projection for one IDS.</summary>
        /// <param name="document">The IDS about to be checked.</param>
        /// <param name="typeSource">
        /// Where the IFC class lives, when the person running the check knows and CamelWorks does
        /// not. Taken at its word — no prefix test — because they can see their own data.
        /// </param>
        public static IdsProjection For(IdsDocument document, PropertyAddress? typeSource = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            return new IdsProjection(document, typeSource != null && typeSource.Value.IsEmpty ? null : typeSource);
        }

        /// <summary>
        /// The specifications that lean on a facet a federation cannot answer.
        ///
        /// <c>partOf</c> asks what an element is aggregated into, which is an IFC relationship the
        /// host does not carry across import; the selection tree above an element is a model and a
        /// layer, not an <c>IfcBuildingStorey</c>. Rather than fail those quietly against empty
        /// data, the caller names them.
        /// </summary>
        public static IReadOnlyList<string> Unanswerable(IdsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            return document.Specifications
                .Where(s => s != null && s.Applicability.Concat(s.Requirements).Any(f => f is IdsPartOfFacet))
                .Select(s => s.Name)
                .ToList();
        }

        /// <summary>
        /// Project one element, or return null when it carries nothing this IDS mentions.
        /// </summary>
        public IdsElement? From(IModelItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Read++;

            // Read once. Every call crosses into the host, and a federation has enough elements
            // that a second pass over the same properties is a second pass over the whole job.
            var properties = item.Properties().ToList();

            var element = new IdsElement(item.Key.ToString(), ClassOf(item, properties));

            if (element.IfcType != null) Classed++;

            foreach (var property in properties)
            {
                if (element.PredefinedType == null && PredefinedNames.Any(
                        n => string.Equals(n, property.Name, StringComparison.OrdinalIgnoreCase)))
                    element.PredefinedType = Upper(property.Value);

                if (_wantsAttributes
                    && AttributeNames.TryGetValue(property.Name, out var attribute)
                    && !element.Attributes.ContainsKey(attribute))
                    element.Attributes[attribute] = property.Value;

                if (_properties.Any(f => f.PropertySet.Matches(property.Category) && f.BaseName.Matches(property.Name)))
                    element.Properties.Add(new IdsProperty(property.Category, property.Name, property.Value));

                // A classification arrives either as its own tab or as a named property, depending
                // on which reader wrote it. Both are looked for, and the IDS decides which names
                // count — nothing here guesses at what a classification system is called.
                foreach (var facet in _classifications)
                {
                    if (facet.System.Matches(property.Category))
                    {
                        element.Classifications.Add(new IdsClassification(property.Category, property.Value));
                        break;
                    }

                    if (facet.System.Matches(property.Name))
                    {
                        element.Classifications.Add(new IdsClassification(property.Name, property.Value));
                        break;
                    }
                }

                if (_wantsMaterials && !string.IsNullOrEmpty(property.Value)
                    && (MaterialNames.Any(n => string.Equals(n, property.Name, StringComparison.OrdinalIgnoreCase))
                        || MaterialNames.Any(n => string.Equals(n, property.Category, StringComparison.OrdinalIgnoreCase))))
                    element.Materials.Add(property.Value!);
            }

            if (_wantsAttributes && !element.Attributes.ContainsKey("Name"))
                element.Attributes["Name"] = item.DisplayName;

            if (!Carries(element)) return null;

            Kept++;
            return element;
        }

        /// <summary>Project a whole model, dropping the elements this IDS has nothing to say about.</summary>
        public IEnumerable<IdsElement> FromAll(IEnumerable<IModelItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            foreach (var item in items)
            {
                var element = From(item);
                if (element != null) yield return element;
            }
        }

        /// <summary>True when the value reads as an IFC class name rather than a family name.</summary>
        public static bool LooksLikeAClass(string? value) =>
            value != null
            && value.Length > IfcPrefix.Length
            && value.StartsWith(IfcPrefix, StringComparison.OrdinalIgnoreCase)
            && value.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));

        private static IEnumerable<IdsFacet> Facets(IdsDocument document) =>
            document.Specifications
                .Where(s => s != null)
                .SelectMany(s => s.Applicability.Concat(s.Requirements))
                .Where(f => f != null);

        private static string? Upper(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim().ToUpperInvariant();

        private bool Carries(IdsElement element) =>
            element.IfcType != null
            || element.Properties.Count > 0
            || element.Classifications.Count > 0
            || element.Materials.Count > 0
            || (_wantsAttributes && element.Attributes.Count > 0);

        private string? ClassOf(IModelItem item, List<PropertyValue> properties)
        {
            if (_typeSource != null)
            {
                foreach (var property in properties)
                {
                    if (_typeSource.Value.Holds(property)) return Upper(property.Value);
                }

                return null;
            }

            foreach (var path in TypeLadder)
            {
                foreach (var property in properties)
                {
                    if (path.Holds(property) && LooksLikeAClass(property.Value)) return Upper(property.Value);
                }
            }

            if (LooksLikeAClass(item.TypeName)) return Upper(item.TypeName);

            // Nothing on the ladder. Remember where a class name was seen anyway, so a run that
            // finds nothing can say where to look instead of shrugging.
            foreach (var property in properties)
            {
                if (!LooksLikeAClass(property.Value)) continue;

                var path = new PropertyAddress(property.Category, property.Name);

                if (_candidates.TryGetValue(path, out var seen)) _candidates[path] = seen + 1;
                else if (_candidates.Count < MaxSuggestions * 4) _candidates[path] = 1;
            }

            return null;
        }
    }
}
