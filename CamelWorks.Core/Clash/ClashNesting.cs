using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Where one bucket of results is meant to sit: the trail of group names down to it, and the
    /// results that belong there.
    ///
    /// A trail rather than a name. The board bands its results in a chain — section, then material,
    /// then type — and the host's clash groups may or may not hold that shape. Handing the seam the
    /// segments rather than a joined string lets the seam decide: nested groups where the document
    /// takes them, one group named for the whole trail where it does not. Either way the trail is
    /// the identity, and <see cref="ClashTrail.Join"/> is what both sides read it back as.
    /// </summary>
    public sealed class ClashGroupPath
    {
        /// <summary>The trail of group names, outermost first.</summary>
        /// <param name="trail">The group names, outermost first.</param>
        /// <param name="resultIds">The results that belong at the end of it.</param>
        public ClashGroupPath(IReadOnlyList<string> trail, IReadOnlyList<string> resultIds)
        {
            Trail = trail ?? Array.Empty<string>();
            ResultIds = resultIds ?? Array.Empty<string>();
        }

        /// <summary>The group names, outermost first.</summary>
        public IReadOnlyList<string> Trail { get; }

        /// <summary>The results that belong at the end of the trail.</summary>
        public IReadOnlyList<string> ResultIds { get; }
    }

    /// <summary>What a nested regroup actually did, which is not always what it was asked for.</summary>
    public sealed class ClashNesting
    {
        /// <summary>Say what a nested regroup did.</summary>
        /// <param name="took">Whether the document took the change at all.</param>
        /// <param name="nested">Whether the groups ended up nested rather than flattened.</param>
        /// <param name="groups">How many groups the document now holds for this test.</param>
        /// <param name="problem">Why it is not nested, when it is not, or null.</param>
        public ClashNesting(bool took, bool nested, int groups, string? problem)
        {
            Took = took;
            Nested = nested;
            Groups = groups;
            Problem = problem;
        }

        /// <summary>Whether the document took the change at all.</summary>
        public bool Took { get; }

        /// <summary>Whether the groups ended up nested rather than flattened.</summary>
        public bool Nested { get; }

        /// <summary>How many groups the document now holds for this test.</summary>
        public int Groups { get; }

        /// <summary>Why it is not nested, when it is not, or null.</summary>
        public string? Problem { get; }
    }

    /// <summary>
    /// The one spelling of a trail of clash-group names, so a reader and a writer agree.
    ///
    /// A middle dot, because a clash group's name is written into the document and read back out of
    /// it and both slashes read as a path separator. A middle dot does not, reads as a separator to
    /// a person, and is what appears on the board.
    /// </summary>
    public static class ClashTrail
    {
        /// <summary>What sits between two names of a trail.</summary>
        public const string Between = " · ";

        /// <summary>One trail as the single name a flat document would carry.</summary>
        /// <param name="trail">The group names, outermost first.</param>
        public static string Join(IEnumerable<string>? trail) =>
            trail == null
                ? string.Empty
                : string.Join(Between, trail.Where(t => !string.IsNullOrEmpty(t)));

        /// <summary>Add one name to a trail, giving the name alone when the trail is empty.</summary>
        /// <param name="trail">The trail so far, or null or empty.</param>
        /// <param name="name">The name to add.</param>
        public static string Under(string? trail, string? name)
        {
            var one = name ?? string.Empty;

            return string.IsNullOrEmpty(trail) ? one
                : one.Length == 0 ? trail!
                : trail + Between + one;
        }
    }
}
