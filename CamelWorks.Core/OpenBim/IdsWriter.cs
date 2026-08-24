using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>An IDS file, written, and everything the writing had to decide.</summary>
    public sealed class IdsWriteResult
    {
        internal IdsWriteResult(string text, int specifications, int facets,
                               IReadOnlyList<string> warnings, string? problem)
        {
            Text = text; Specifications = specifications; Facets = facets;
            Warnings = warnings; Problem = problem;
        }

        /// <summary>The file.</summary>
        public string Text { get; }

        /// <summary>How many specifications went into it.</summary>
        public int Specifications { get; }

        /// <summary>How many facets across all of them.</summary>
        public int Facets { get; }

        /// <summary>Anything the writing had to decide, in a sentence each.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Why nothing was written, or null.</summary>
        public string? Problem { get; }

        /// <summary>True when there is a file.</summary>
        public bool IsUsable => Problem == null && Text.Length > 0;

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Problem
            ?? Measure.Count(Specifications)
               + (Specifications == 1 ? " specification, " : " specifications, ")
               + Measure.Count(Facets) + (Facets == 1 ? " facet." : " facets.");

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// An IDS file, written.
    ///
    /// We could read one and could not write one, which made this half a tool: a client sends an
    /// IDS and we check against it, but the requirements a project sets for itself - the ones a
    /// coordinator argues about all week and then enforces by eye - had nowhere to live. Writing
    /// one turns those into a file the client's tool, the architect's tool and ours all agree on.
    ///
    /// <b>It refuses a file nobody could load.</b> A specification with no applicability applies to
    /// every element in the model, which IDS forbids and which would mark a whole federation
    /// failing. An IDS that will not open is worse than no IDS, because the person who receives it
    /// has to work out whose fault it is.
    ///
    /// <b>What it had to decide is said.</b> An IFC version nobody named, an applicability facet
    /// carrying a cardinality the schema has nowhere to put, a restriction that restricts nothing -
    /// each is written the only way it can be and reported, rather than silently normalised.
    /// </summary>
    public static class IdsWriter
    {
        /// <summary>The IDS namespace.</summary>
        public const string Namespace = "http://standards.buildingsmart.org/IDS";

        /// <summary>The XML Schema namespace, which restrictions are written in.</summary>
        public const string Schema = "http://www.w3.org/2001/XMLSchema";

        /// <summary>What the file is called when nothing is said.</summary>
        public const string DefaultIfcVersion = "IFC4";

        /// <summary>The IFC versions the schema's enumeration allows, and no others.</summary>
        public static IReadOnlyList<string> IfcVersions { get; } =
            new[] { "IFC2X3", "IFC4", "IFC4X3_ADD2" };

        /// <summary>The relationships a partOf facet may name, and no others.</summary>
        public static IReadOnlyList<string> Relations { get; } = new[]
        {
            "IFCRELAGGREGATES",
            "IFCRELASSIGNSTOGROUP",
            "IFCRELCONTAINEDINSPATIALSTRUCTURE",
            "IFCRELNESTS",
            "IFCRELVOIDSELEMENT IFCRELFILLSELEMENT",
        };

        /// <summary>
        /// The order the schema's sequences put facets in.
        ///
        /// Both applicability and requirements are sequences, not choices, so a file whose facets
        /// come out in the order somebody typed them is a file that does not load. Sorting by this
        /// is the whole fix, and it is stable, so two properties keep the order they were written.
        /// </summary>
        private static int Order(IdsFacet facet) => facet switch
        {
            IdsEntityFacet _ => 0,
            IdsPartOfFacet _ => 1,
            IdsClassificationFacet _ => 2,
            IdsAttributeFacet _ => 3,
            IdsPropertyFacet _ => 4,
            IdsMaterialFacet _ => 5,
            _ => 6,
        };

        /// <summary>Write the file.</summary>
        /// <param name="document">What to write.</param>
        /// <param name="whenTicks">When it is being written. Zero leaves the date out.</param>
        public static IdsWriteResult Write(IdsDocument? document, long whenTicks = 0)
        {
            if (document == null) return Refuse("There is nothing to write.");

            if (document.Specifications.Count == 0)
                return Refuse("An IDS with no specifications in it checks nothing. Add one - a set "
                    + "of elements it applies to, and what they must have.");

            var warnings = new List<string>();

            for (var i = 0; i < document.Specifications.Count; i++)
            {
                var spec = document.Specifications[i];
                var where = "\"" + spec.Name + "\"";

                if (spec.Applicability.Count == 0)
                    return Refuse(where + " says which elements it applies to nowhere, so it applies "
                        + "to every element in the model. IDS does not allow that, and a file that "
                        + "does not open is worse than no file. Say what it is about - an IFC class "
                        + "is usually enough.");

                if (spec.Applicability.OfType<IdsEntityFacet>().Count() > 1)
                    return Refuse(where + " names two IFC classes in what it applies to, and the "
                        + "schema allows one. Two classes go in one facet, as a pattern or a list "
                        + "of permitted values.");

                // Each of these would have to be written as something it does not mean. An IDS is
                // a document somebody enforces a contract with; quietly inverting one of its rules
                // is worse than refusing to write it.
                foreach (var entity in spec.Requirements.OfType<IdsEntityFacet>()
                             .Where(f => f.Cardinality != IdsCardinality.Required))
                    return Refuse(where + ": \"" + entity.Describe() + "\" is required to be "
                        + Word(entity.Cardinality) + ", and the schema has no cardinality on an "
                        + "entity requirement - the class an element is is not optional. Say which "
                        + "classes are acceptable instead, as a pattern or a list.");

                foreach (var part in spec.Requirements.OfType<IdsPartOfFacet>()
                             .Where(f => f.Cardinality == IdsCardinality.Optional))
                    return Refuse(where + ": \"" + part.Describe() + "\" is optional, and a partOf "
                        + "requirement may only be required or prohibited. An optional one asks "
                        + "for nothing, so leave it out.");

                foreach (var part in spec.Requirements.OfType<IdsPartOfFacet>()
                             .Concat(spec.Applicability.OfType<IdsPartOfFacet>())
                             .Where(f => f.Relation != null && !Relations.Contains(f.Relation)))
                    return Refuse(where + ": \"" + part.Relation + "\" is not a relationship IDS "
                        + "knows. Leaving it out would widen the rule to any relationship, which is "
                        + "not what it says. The five are: " + string.Join(", ", Relations) + ".");

                if (spec.Requirements.Count == 0 && spec.MinOccurs <= 0)
                    warnings.Add(where + " asks for nothing and expects no particular number of "
                        + "elements, so it passes whatever the model holds.");

                foreach (var facet in spec.Applicability.Where(f => f.Cardinality != IdsCardinality.Required))
                    warnings.Add(where + ": \"" + facet.Describe() + "\" is what the specification "
                        + "applies to, and the schema has nowhere to put a cardinality there. It is "
                        + "written as a plain filter.");

                foreach (var version in spec.IfcVersions.Where(
                             v => !string.IsNullOrWhiteSpace(v) && !IfcVersions.Contains(v.Trim())))
                    warnings.Add(where + ": \"" + version + "\" is not an IFC version the schema "
                        + "allows, so it is left out. The three are: "
                        + string.Join(", ", IfcVersions) + ".");

                if (Versions(spec).Count == 0)
                    warnings.Add(where + " names no IFC version this schema allows, so it is written "
                        + "as " + DefaultIfcVersion + ". Every specification must name one.");
            }

            var s = new StringBuilder();

            s.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            s.Append("<ids xmlns=\"").Append(Namespace).Append("\" xmlns:xs=\"").Append(Schema)
             .Append("\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"")
             .Append(" xsi:schemaLocation=\"").Append(Namespace)
             .Append(" http://standards.buildingsmart.org/IDS/1.0/ids.xsd\">\n");

            Info(s, document, whenTicks);

            s.Append("  <specifications>\n");

            var facets = 0;

            foreach (var spec in document.Specifications) facets += Specification(s, spec, warnings);

            s.Append("  </specifications>\n");
            s.Append("</ids>\n");

            return new IdsWriteResult(s.ToString(), document.Specifications.Count, facets,
                                      warnings, null);
        }

        // -------------------------------------------------------------------------------------

        /// <summary>The versions this specification names that the schema will accept.</summary>
        private static IReadOnlyList<string> Versions(IdsSpecification spec) =>
            spec.IfcVersions.Where(v => !string.IsNullOrWhiteSpace(v))
                            .Select(v => v.Trim())
                            .Where(v => IfcVersions.Contains(v))
                            .ToList();

        /// <summary>The schema's own pattern for the author: something@something.something.</summary>
        private static bool IsAddress(string? text)
        {
            var value = (text ?? string.Empty).Trim();

            var at = value.IndexOf('@');
            if (at <= 0 || at == value.Length - 1) return false;

            var host = value.Substring(at + 1);
            var dot = host.IndexOf('.');

            return dot > 0 && dot < host.Length - 1 && host.IndexOf('@') < 0;
        }

        private static void Info(StringBuilder s, IdsDocument document, long whenTicks)
        {
            s.Append("  <info>\n");
            s.Append("    <title>").Append(Xml.Escape(document.Title)).Append("</title>\n");

            Line(s, "version", document.Version);
            Line(s, "description", document.Description);

            // The schema wants an email address and nothing else. Leaving a name out costs the
            // file nothing; writing one where an address belongs costs it loading at all.
            if (IsAddress(document.Author)) Line(s, "author", document.Author);

            var date = Date(whenTicks);
            if (date != null) s.Append("    <date>").Append(date).Append("</date>\n");

            s.Append("  </info>\n");
        }

        private static int Specification(StringBuilder s, IdsSpecification spec,
                                         ICollection<string> warnings)
        {
            var versions = Versions(spec);

            s.Append("    <specification name=\"").Append(Xml.Escape(spec.Name))
             .Append("\" ifcVersion=\"")
             .Append(Xml.Escape(versions.Count == 0 ? DefaultIfcVersion : string.Join(" ", versions)))
             .Append('"');

            Attribute(s, "identifier", spec.Identifier);
            Attribute(s, "description", spec.Description);
            Attribute(s, "instructions", spec.Instructions);

            s.Append(">\n");

            // The occurs live on applicability, which is IDS's own answer to the vacuous pass:
            // "at least one wall exists, and every wall carries a fire rating" is one specification.
            s.Append("      <applicability minOccurs=\"")
             .Append(Math.Max(0, spec.MinOccurs).ToString(CultureInfo.InvariantCulture))
             .Append("\" maxOccurs=\"")
             .Append(spec.MaxOccurs.HasValue
                 ? spec.MaxOccurs.Value.ToString(CultureInfo.InvariantCulture)
                 : "unbounded")
             .Append("\">\n");

            var facets = 0;

            foreach (var facet in spec.Applicability.OrderBy(Order))
            {
                Facet(s, facet, "        ", requirement: false, warnings);
                facets++;
            }

            s.Append("      </applicability>\n");

            if (spec.Requirements.Count > 0)
            {
                s.Append("      <requirements>\n");

                foreach (var facet in spec.Requirements.OrderBy(Order))
                {
                    Facet(s, facet, "        ", requirement: true, warnings);
                    facets++;
                }

                s.Append("      </requirements>\n");
            }

            s.Append("    </specification>\n");

            return facets;
        }

        private static void Facet(StringBuilder s, IdsFacet facet, string pad, bool requirement,
                                  ICollection<string> warnings)
        {
            switch (facet)
            {
                case IdsEntityFacet entity:
                    // Alone among the requirement facets, an entity carries no cardinality: what
                    // class an element is is not something that can be optional.
                    Open(s, pad, "entity", facet, cardinality: false, instructions: requirement);
                    Value(s, pad + "  ", "name", entity.Name, warnings);
                    Value(s, pad + "  ", "predefinedType", entity.PredefinedType, warnings);
                    s.Append(pad).Append("</entity>\n");
                    return;

                case IdsAttributeFacet attribute:
                    Open(s, pad, "attribute", facet, requirement, requirement);
                    Value(s, pad + "  ", "name", attribute.Name, warnings);
                    Value(s, pad + "  ", "value", attribute.Value, warnings);
                    s.Append(pad).Append("</attribute>\n");
                    return;

                case IdsPropertyFacet property:
                    Open(s, pad, "property", facet, requirement, requirement,
                         DataType(property, warnings));
                    Value(s, pad + "  ", "propertySet", property.PropertySet, warnings);
                    Value(s, pad + "  ", "baseName", property.BaseName, warnings);
                    Value(s, pad + "  ", "value", property.Value, warnings);
                    s.Append(pad).Append("</property>\n");
                    return;

                case IdsClassificationFacet classification:
                    Open(s, pad, "classification", facet, requirement, requirement);

                    // The value before the system, which is the order the schema's sequence puts
                    // them in and the opposite of the order they read in.
                    Value(s, pad + "  ", "value", classification.Value, warnings);
                    Value(s, pad + "  ", "system", classification.System, warnings);

                    s.Append(pad).Append("</classification>\n");
                    return;

                case IdsMaterialFacet material:
                    Open(s, pad, "material", facet, requirement, requirement);
                    Value(s, pad + "  ", "value", material.Value, warnings);
                    s.Append(pad).Append("</material>\n");
                    return;

                case IdsPartOfFacet partOf:
                    Open(s, pad, "partOf", facet, requirement, requirement,
                         partOf.Relation == null ? null : " relation=\""
                             + Xml.Escape(partOf.Relation) + "\"");

                    // partOf wraps a whole entity facet rather than a bare name, which is the one
                    // place the file's shape and this model's differ.
                    s.Append(pad).Append("  <entity>\n");
                    Value(s, pad + "    ", "name", partOf.EntityName, warnings);
                    s.Append(pad).Append("  </entity>\n");

                    s.Append(pad).Append("</partOf>\n");
                    return;

                default:
                    warnings.Add("\"" + facet.Describe() + "\" is a kind of facet this writer does "
                        + "not know, so it is not in the file.");
                    return;
            }
        }

        private static void Open(StringBuilder s, string pad, string name, IdsFacet facet,
                                 bool cardinality, bool instructions, string? extra = null)
        {
            s.Append(pad).Append('<').Append(name);

            if (extra != null) s.Append(extra);

            if (cardinality) s.Append(" cardinality=\"").Append(Word(facet.Cardinality)).Append('"');

            // Instructions are for the author of the model, and only requirements have one to give.
            if (instructions && facet.Instructions != null)
                s.Append(" instructions=\"").Append(Xml.Escape(facet.Instructions)).Append('"');

            s.Append(">\n");
        }

        /// <summary>
        /// The dataType attribute, when the schema would accept it.
        ///
        /// It has to be an IFC defined type, all capitals. Leaving a malformed one out costs the
        /// file nothing - it is a hint about units, not part of what is required - where writing
        /// it costs the file loading at all.
        /// </summary>
        private static string? DataType(IdsPropertyFacet property, ICollection<string> warnings)
        {
            var value = (property.DataType ?? string.Empty).Trim();

            if (value.Length == 0) return null;

            if (value.All(c => c >= 'A' && c <= 'Z'))
                return " dataType=\"" + value + "\"";

            warnings.Add("\"" + value + "\" is not an IFC type name - those are all capitals - so "
                + "it is left out of \"" + property.Describe() + "\". It says what unit the value "
                + "is in, not what is required, so nothing is lost but the hint.");

            return null;
        }

        /// <summary>One of the value elements, as a literal or an XSD restriction.</summary>
        private static void Value(StringBuilder s, string pad, string name, IdsValue? value,
                                  ICollection<string> warnings)
        {
            if (value == null) return;

            if (value.Literal != null)
            {
                s.Append(pad).Append('<').Append(name).Append("><simpleValue>")
                 .Append(Xml.Escape(value.Literal)).Append("</simpleValue></").Append(name)
                 .Append(">\n");
                return;
            }

            if (value.IsEmpty)
            {
                // A restriction with nothing in it accepts everything, which is what leaving the
                // element out says - and says it in a form every reader already handles.
                warnings.Add("A \"" + name + "\" restricts nothing, so it is left out rather than "
                    + "written as a restriction that accepts anything.");
                return;
            }

            s.Append(pad).Append('<').Append(name).Append(">\n");
            s.Append(pad).Append("  <xs:restriction base=\"xs:string\">\n");

            if (value.Pattern != null)
                s.Append(pad).Append("    <xs:pattern value=\"").Append(Xml.Escape(value.Pattern))
                 .Append("\" />\n");

            if (value.Enumeration != null)
                foreach (var one in value.Enumeration)
                    s.Append(pad).Append("    <xs:enumeration value=\"").Append(Xml.Escape(one))
                     .Append("\" />\n");

            Bound(s, pad, "minInclusive", value.MinInclusive);
            Bound(s, pad, "maxInclusive", value.MaxInclusive);
            Bound(s, pad, "minExclusive", value.MinExclusive);
            Bound(s, pad, "maxExclusive", value.MaxExclusive);

            Count(s, pad, "length", value.Length);
            Count(s, pad, "minLength", value.MinLength);
            Count(s, pad, "maxLength", value.MaxLength);

            s.Append(pad).Append("  </xs:restriction>\n");
            s.Append(pad).Append("</").Append(name).Append(">\n");
        }

        private static void Bound(StringBuilder s, string pad, string name, double? value)
        {
            if (value == null) return;

            s.Append(pad).Append("    <xs:").Append(name).Append(" value=\"")
             .Append(value.Value.ToString("0.##########", CultureInfo.InvariantCulture))
             .Append("\" />\n");
        }

        private static void Count(StringBuilder s, string pad, string name, int? value)
        {
            if (value == null) return;

            s.Append(pad).Append("    <xs:").Append(name).Append(" value=\"")
             .Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append("\" />\n");
        }

        private static void Line(StringBuilder s, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            s.Append("    <").Append(name).Append('>').Append(Xml.Escape(value))
             .Append("</").Append(name).Append(">\n");
        }

        private static void Attribute(StringBuilder s, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            s.Append(' ').Append(name).Append("=\"").Append(Xml.Escape(value)).Append('"');
        }

        private static string Word(IdsCardinality cardinality) => cardinality switch
        {
            IdsCardinality.Prohibited => "prohibited",
            IdsCardinality.Optional => "optional",
            _ => "required",
        };

        private static string? Date(long ticks)
        {
            if (ticks <= 0) return null;

            try
            {
                return new DateTime(ticks, DateTimeKind.Utc)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static IdsWriteResult Refuse(string why) =>
            new IdsWriteResult(string.Empty, 0, 0, Array.Empty<string>(), why);
    }
}
