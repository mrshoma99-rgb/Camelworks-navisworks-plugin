using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// A specification as data, for the half of a rule set no IDS file can carry.
    ///
    /// <b>Why this exists at all.</b> The published schema has no extension point anywhere - not
    /// on the root, not on a specification, not on applicability - so a rule that says who it is
    /// about in this model's own terms cannot be written as IDS without making the file invalid
    /// for every other tool. It has to travel some other way, and "some other way" has to be a
    /// real format with a reader, not a blob.
    ///
    /// <b>Why in Core rather than beside the editor.</b> It became a wire format the moment two
    /// people could send each other a rule set. A shape that lives in a window is a shape nobody
    /// can test without a window, and one that quietly changes when somebody rearranges a form.
    /// </summary>
    public static class IdsJson
    {
        /// <summary>The shape's version, so a later change can be told apart.</summary>
        public const int Version = 1;

        /// <summary>Write a specification.</summary>
        /// <param name="specification">The rule.</param>
        public static JsonValue Write(IdsSpecification specification)
        {
            if (specification == null) throw new ArgumentNullException(nameof(specification));

            var json = JsonValue.Object().Set("name", specification.Name);

            Text(json, "identifier", specification.Identifier);
            Text(json, "description", specification.Description);
            Text(json, "instructions", specification.Instructions);

            if (specification.MinOccurs != 0) json.Set("min", specification.MinOccurs);
            if (specification.MaxOccurs != null) json.Set("max", specification.MaxOccurs.Value);

            if (specification.IfcVersions.Count > 0)
                json.Set("versions", JsonValue.Array(specification.IfcVersions.Select(JsonValue.String)));

            json.Set("applicability", JsonValue.Array(specification.Applicability.Select(Facet)));

            if (specification.Requirements.Count > 0)
                json.Set("requirements", JsonValue.Array(specification.Requirements.Select(Facet)));

            return json;
        }

        /// <summary>Write a whole rule set's specifications.</summary>
        /// <param name="specifications">The rules.</param>
        public static JsonValue WriteAll(IEnumerable<IdsSpecification>? specifications) =>
            JsonValue.Array((specifications ?? Array.Empty<IdsSpecification>())
                .Where(s => s != null)
                .Select(Write));

        /// <summary>
        /// Read a specification back, or null when the record does not say enough to rebuild one.
        /// </summary>
        /// <param name="json">One saved rule.</param>
        public static IdsSpecification? Read(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var name = json["name"].AsString();

            if (string.IsNullOrWhiteSpace(name)) return null;

            var specification = new IdsSpecification(name!)
            {
                Identifier = json["identifier"].AsString(),
                Description = json["description"].AsString(),
                Instructions = json["instructions"].AsString(),
                MinOccurs = (int)json["min"].AsLong(),
                MaxOccurs = json.Has("max") ? (int)json["max"].AsLong() : (int?)null,
            };

            foreach (var version in json["versions"].Items)
                if (version.AsString() is string named && named.Length > 0)
                    specification.IfcVersions.Add(named);

            foreach (var facet in json["applicability"].Items)
                if (Facet(facet) is IdsFacet read) specification.Applicability.Add(read);

            foreach (var facet in json["requirements"].Items)
                if (Facet(facet) is IdsFacet read) specification.Requirements.Add(read);

            // A specification with nothing in its applicability applies to every element in the
            // model, which IDS forbids for good reason and which nothing here should invent out
            // of a record that failed to read.
            return specification.Applicability.Count == 0 ? null : specification;
        }

        /// <summary>Read a whole rule set's specifications, skipping any that will not read.</summary>
        /// <param name="json">The saved array.</param>
        public static IReadOnlyList<IdsSpecification> ReadAll(JsonValue? json)
        {
            var specifications = new List<IdsSpecification>();

            foreach (var record in (json ?? JsonValue.Array(Array.Empty<JsonValue>())).Items)
                if (Read(record) is IdsSpecification specification)
                    specifications.Add(specification);

            return specifications;
        }

        // -----------------------------------------------------------------------------------

        /// <summary>What kind of facet a record is. Written by name so a new one cannot renumber.</summary>
        private static string KindOf(IdsFacet facet) => facet switch
        {
            IdsEntityFacet _ => "entity",
            IdsAttributeFacet _ => "attribute",
            IdsPropertyFacet _ => "property",
            IdsClassificationFacet _ => "classification",
            IdsMaterialFacet _ => "material",
            IdsPartOfFacet _ => "partOf",
            IdsScopeFacet _ => "scope",
            _ => "unknown",
        };

        private static JsonValue Facet(IdsFacet facet)
        {
            var json = JsonValue.Object().Set("kind", KindOf(facet));

            if (facet.Cardinality != IdsCardinality.Required)
                json.Set("cardinality", facet.Cardinality.ToString());

            Text(json, "instructions", facet.Instructions);

            switch (facet)
            {
                case IdsEntityFacet entity:
                    json.Set("name", Value(entity.Name));
                    if (entity.PredefinedType != null)
                        json.Set("predefinedType", Value(entity.PredefinedType));
                    break;

                case IdsAttributeFacet attribute:
                    json.Set("name", Value(attribute.Name));
                    if (attribute.Value != null) json.Set("value", Value(attribute.Value));
                    break;

                case IdsPropertyFacet property:
                    json.Set("propertySet", Value(property.PropertySet));
                    json.Set("baseName", Value(property.BaseName));
                    if (property.Value != null) json.Set("value", Value(property.Value));
                    Text(json, "dataType", property.DataType);
                    break;

                case IdsClassificationFacet classification:
                    json.Set("system", Value(classification.System));
                    if (classification.Value != null) json.Set("value", Value(classification.Value));
                    break;

                case IdsMaterialFacet material:
                    if (material.Value != null) json.Set("value", Value(material.Value));
                    break;

                case IdsPartOfFacet part:
                    json.Set("entity", Value(part.EntityName));
                    Text(json, "relation", part.Relation);
                    break;

                case IdsScopeFacet scope:
                    json.Set("scope", scope.Scope.Token);
                    break;
            }

            return json;
        }

        private static IdsFacet? Facet(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            IdsFacet? facet = json["kind"].AsString() switch
            {
                "entity" => Value(json["name"]) is IdsValue name
                    ? new IdsEntityFacet(name, Value(json["predefinedType"]))
                    : null,

                "attribute" => Value(json["name"]) is IdsValue named
                    ? new IdsAttributeFacet(named, Value(json["value"]))
                    : null,

                "property" => Value(json["propertySet"]) is IdsValue set
                              && Value(json["baseName"]) is IdsValue baseName
                    ? new IdsPropertyFacet(set, baseName, Value(json["value"]),
                                           json["dataType"].AsString())
                    : null,

                "classification" => Value(json["system"]) is IdsValue system
                    ? new IdsClassificationFacet(system, Value(json["value"]))
                    : null,

                "material" => new IdsMaterialFacet(Value(json["value"])),

                "partOf" => Value(json["entity"]) is IdsValue entity
                    ? new IdsPartOfFacet(entity, json["relation"].AsString())
                    : null,

                "scope" => IdsScope.FromToken(json["scope"].AsString()) is IdsScope scope
                    ? new IdsScopeFacet(scope)
                    : null,

                _ => null,
            };

            if (facet == null) return null;

            if (Enum.TryParse<IdsCardinality>(json["cardinality"].AsString(), false, out var how))
                facet.Cardinality = how;

            facet.Instructions = json["instructions"].AsString();

            return facet;
        }

        /// <summary>
        /// A value, simple or restricted.
        ///
        /// The pattern is written as the file spelled it rather than as it compiled, for the same
        /// reason the writer does: the compiled form is anchored, and handing somebody
        /// <c>\A(?:...)\z</c> would give them a rule that says something subtly different.
        /// </summary>
        private static JsonValue Value(IdsValue value)
        {
            if (value.Literal != null) return JsonValue.Object().Set("is", value.Literal);

            var json = JsonValue.Object();

            Text(json, "pattern", value.Pattern);

            if (value.Enumeration != null && value.Enumeration.Count > 0)
                json.Set("oneOf", JsonValue.Array(value.Enumeration.Select(JsonValue.String)));

            Number(json, "minInclusive", value.MinInclusive);
            Number(json, "maxInclusive", value.MaxInclusive);
            Number(json, "minExclusive", value.MinExclusive);
            Number(json, "maxExclusive", value.MaxExclusive);

            if (value.Length != null) json.Set("length", value.Length.Value);
            if (value.MinLength != null) json.Set("minLength", value.MinLength.Value);
            if (value.MaxLength != null) json.Set("maxLength", value.MaxLength.Value);

            return json;
        }

        private static IdsValue? Value(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            if (json["is"].AsString() is string literal) return IdsValue.Simple(literal);

            var enumeration = json["oneOf"].Items
                .Select(v => v.AsString())
                .Where(v => v != null)
                .Select(v => v!)
                .ToList();

            return IdsValue.Restriction(
                json["pattern"].AsString(),
                enumeration.Count > 0 ? enumeration : null,
                Number(json, "minInclusive"), Number(json, "maxInclusive"),
                Number(json, "minExclusive"), Number(json, "maxExclusive"),
                Whole(json, "length"), Whole(json, "minLength"), Whole(json, "maxLength"));
        }

        private static void Text(JsonValue json, string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) json.Set(field, value);
        }

        private static void Number(JsonValue json, string field, double? value)
        {
            if (value != null) json.Set(field, value.Value);
        }

        private static double? Number(JsonValue json, string field) =>
            json.Has(field) ? json[field].AsDouble() : (double?)null;

        private static int? Whole(JsonValue json, string field) =>
            json.Has(field) ? (int)json[field].AsLong() : (int?)null;
    }
}
