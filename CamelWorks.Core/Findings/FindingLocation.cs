using System;
using System.Collections.Generic;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// One place a finding is.
    ///
    /// Carries no decision. The finding it belongs to holds the status, the assignee, the due date
    /// and the sign-off, and this is where the coordinator is sent to look. A location that carried
    /// triage state would be a register entry under another name: its id would then have to be
    /// derived from its own element, and one element moving, splitting or being renamed would
    /// re-identify it and lose whatever had been decided about it. That is the identity problem one
    /// finding with many locations exists to avoid.
    ///
    /// What a location may carry is a note — free text somebody wrote against one place, with an
    /// author and a time. A note records "this one is a false positive, the wall is a void former"
    /// without claiming the location was decided.
    /// </summary>
    public sealed class FindingLocation
    {
        /// <summary>Name one place.</summary>
        /// <param name="id">
        /// How this place is referred to. Made by the producer and stable between runs, so a note
        /// written against it is still against it next week. It must also be unique across every
        /// finding, because the register indexes locations by it alone: every producer in Core
        /// writes its finding's id, a slash and the element key. Three specifications failing the
        /// same wall are three places, and an id that was only the element key made them one.
        /// </param>
        /// <param name="elements">The elements at this place.</param>
        /// <param name="detail">A second line — what failed here, in the producer's own words.</param>
        /// <param name="x">Where to look, in metres, or null when the producer gave no point.</param>
        /// <param name="y">Where to look, in metres, or null when the producer gave no point.</param>
        /// <param name="z">Where to look, in metres, or null when the producer gave no point.</param>
        public FindingLocation(string id, IReadOnlyList<ElementKey> elements,
                               string? detail = null,
                               double? x = null, double? y = null, double? z = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));

            Id = id.Trim();
            Elements = elements ?? Array.Empty<ElementKey>();
            Detail = detail;
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>How this place is referred to.</summary>
        public string Id { get; }

        /// <summary>The elements at this place.</summary>
        public IReadOnlyList<ElementKey> Elements { get; }

        /// <summary>What failed here, in the producer's own words, or null.</summary>
        public string? Detail { get; set; }

        /// <summary>Where to look, in metres, or null.</summary>
        public double? X { get; set; }

        /// <inheritdoc cref="X"/>
        public double? Y { get; set; }

        /// <inheritdoc cref="X"/>
        public double? Z { get; set; }

        /// <summary>The producer's own numbers for this place, by name.</summary>
        public IDictionary<string, string?> Facts { get; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Notes somebody wrote against this one place. Not a decision: see the type remarks.</summary>
        public IList<FindingComment> Notes { get; } = new List<FindingComment>();

        /// <inheritdoc />
        public override string ToString() => Id + (Detail == null ? string.Empty : ": " + Detail);
    }
}
