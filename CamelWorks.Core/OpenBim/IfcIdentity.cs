using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>
    /// The IFC GlobalId an element carries, wherever the host filed it.
    ///
    /// <b>Why this is worth hunting for.</b> BCF names elements by their IFC GlobalId and nothing
    /// else. Without one, a topic exported from here reaches Solibri or Revizto as an issue with a
    /// camera and no elements: the reviewer is flown to roughly the right place and shown nothing
    /// highlighted. With one, the duct and the beam light up. That is most of the difference
    /// between a clash file somebody works from and a clash file somebody scrolls past.
    ///
    /// <b>Why the search is narrow.</b> Navisworks files the value under whatever the exporter
    /// called it, so several spellings are tried - but a property merely called "GUID" is not one
    /// of them. Navisworks gives every element an instance GUID of its own, and writing that into
    /// a BCF component produces a legal-looking string that names nothing in the receiving model.
    /// A topic pointing at an empty selection is worse than a topic honestly pointing at none,
    /// because the export counts the second and reports it.
    /// </summary>
    public static class IfcIdentity
    {
        /// <summary>
        /// The property names that hold an IFC GlobalId, in the spellings exporters use.
        ///
        /// Compared with spaces and underscores removed and case ignored, so "Global Id",
        /// "GLOBALID" and "global_id" are one entry rather than three.
        /// </summary>
        private static readonly string[] Names =
        {
            "globalid",
            "ifcguid",
            "ifcglobalid",
            "ifcgloballyuniqueid",
            "globallyuniqueid",
        };

        /// <summary>
        /// The element's IFC GlobalId, compressed, or null when it carries none.
        /// </summary>
        /// <param name="item">The element, or null.</param>
        public static string? Of(IModelItem? item)
        {
            if (item == null) return null;

            IEnumerable<PropertyValue> properties;

            try
            {
                properties = item.Properties();
            }
            catch (Exception)
            {
                // An element the host will not enumerate is an element with no GlobalId as far as
                // this is concerned. Never a reason to fail an export.
                return null;
            }

            string? loose = null;

            foreach (var property in properties)
            {
                if (!Wanted(property.Name)) continue;

                var found = IfcGuid.From(property.Value);

                if (found == null) continue;

                // A value that was already a compressed GlobalId is the real thing and wins
                // immediately. One we had to compress ourselves is kept as a fallback: it is a
                // genuine GUID, but it may be the authoring tool's rather than the IFC file's.
                if (IfcGuid.IsCompressed(property.Value?.Trim())) return found;

                loose ??= found;
            }

            return loose;
        }

        /// <summary>Whether a property name is one of the spellings a GlobalId travels under.</summary>
        /// <param name="name">The property's name.</param>
        public static bool Wanted(string? name)
        {
            var flattened = Flatten(name);

            return flattened.Length != 0 && Names.Contains(flattened, StringComparer.Ordinal);
        }

        private static string Flatten(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var sb = new System.Text.StringBuilder(name!.Length);

            foreach (var c in name)
            {
                if (c == ' ' || c == '_' || c == '-' || c == '.') continue;

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }
}
