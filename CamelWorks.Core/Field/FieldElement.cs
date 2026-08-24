using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Field
{
    /// <summary>
    /// One element, as every field tool reads it.
    ///
    /// The setting out, the mass properties and the dimensions all want the same five things off an
    /// element and nothing else, so they share one shape rather than three that drift apart. A
    /// screen builds this list once and hands the same list to all of them.
    ///
    /// <b>The box is always there; the body is not.</b> Every element in a Navisworks model has a
    /// world box, so anything that works off <see cref="Bounds"/> works with no setting up at all.
    /// Triangles are dearer to read and are only pulled when a tool needs them, which is why
    /// <see cref="Positions"/> is allowed to be empty and every tool that reads it says which
    /// answer it gave.
    /// </summary>
    public sealed class FieldElement
    {
        /// <summary>Name one.</summary>
        /// <param name="key">Which element.</param>
        /// <param name="name">What it is called.</param>
        /// <param name="bounds">Its world box, in metres.</param>
        /// <param name="hasGeometry">Whether it has a body at all.</param>
        /// <param name="mark">Its mark, where the model carries one, so what comes off it is named for it.</param>
        /// <param name="positions">Its triangle vertices as x,y,z triples in metres, where they have been read.</param>
        public FieldElement(ElementKey key, string? name, BoundingBox bounds,
                            bool hasGeometry = true, string? mark = null,
                            System.Collections.Generic.IReadOnlyList<float>? positions = null)
        {
            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name!.Trim();
            Bounds = bounds;
            HasGeometry = hasGeometry;
            Mark = string.IsNullOrWhiteSpace(mark) ? null : mark!.Trim();
            Positions = positions ?? System.Array.Empty<float>();
        }

        /// <summary>Which element.</summary>
        public ElementKey Key { get; }

        /// <summary>What it is called.</summary>
        public string Name { get; }

        /// <summary>Its world box, in metres.</summary>
        public BoundingBox Bounds { get; }

        /// <summary>Whether it has a body at all.</summary>
        public bool HasGeometry { get; }

        /// <summary>Its mark, or null.</summary>
        public string? Mark { get; }

        /// <summary>Its triangle vertices as x,y,z triples in metres. Empty when they were not read.</summary>
        public System.Collections.Generic.IReadOnlyList<float> Positions { get; }

        /// <summary>True when there are enough vertices to work out which way the element runs.</summary>
        public bool HasBody => Positions.Count >= 9;

        /// <summary>What the points and rows off it are named after.</summary>
        public string Label => Mark ?? Name;

        /// <inheritdoc />
        public override string ToString() => Label;
    }
}
