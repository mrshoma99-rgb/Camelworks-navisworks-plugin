using System;
using System.Collections.Generic;
using CamelWorks.Core.Findings;

namespace CamelWorks.UI.Views
{
    /// <summary>
    /// The one place a <see cref="FindingStatus"/> becomes text in CamelWorks.UI.
    ///
    /// The screens used to carry the five words as string literals — in tallies, filters, sort
    /// keys, chip option lists, column headings and clipboard cells — and a row's status was a
    /// string all the way from the host read to the board. Two of those literal sets disagreed
    /// about case and about what an unrecognised word ranked as, so the same status could be
    /// tallied one way and ordered another.
    ///
    /// The status itself is now the enum, and this class is the only conversion to text. It
    /// delegates to <see cref="FindingStatusLattice.ToName"/> so the board, the files and the
    /// host all spell a status the same way; nothing in this project should call
    /// <c>ToString()</c> on a <see cref="FindingStatus"/>.
    /// </summary>
    internal static class FindingStatusWords
    {
        /// <summary>The five statuses, in lattice order.</summary>
        internal static IReadOnlyList<FindingStatus> All { get; } = new[]
        {
            FindingStatus.New,
            FindingStatus.Active,
            FindingStatus.Reviewed,
            FindingStatus.Approved,
            FindingStatus.Resolved,
        };

        /// <summary>
        /// The five words, in the same order as <see cref="All"/>.
        ///
        /// For the widgets that take a list of options as strings.
        /// </summary>
        internal static IReadOnlyList<string> Words { get; } = Named(All);

        /// <summary>How a status reads.</summary>
        /// <param name="status">The status.</param>
        internal static string Of(FindingStatus status) => FindingStatusLattice.ToName(status);

        /// <summary>
        /// The status one of this class's own words stands for.
        ///
        /// The round trip of <see cref="Of"/>, and nothing more: it is for reading back what a
        /// widget this code populated from <see cref="Words"/> hands to its callback. A word from
        /// a host, a file or a person goes through <c>ClashStatusMap.Read</c> instead, which
        /// knows the vocabularies this does not.
        /// </summary>
        /// <param name="word">A word this class produced.</param>
        /// <exception cref="ArgumentOutOfRangeException">The word is not one of the five.</exception>
        internal static FindingStatus Read(string? word) =>
            FindingStatusLattice.TryParse(word, out var status)
                ? status
                : throw new ArgumentOutOfRangeException(nameof(word), word, "Not a status word.");

        /// <summary>The words for a set of statuses, in the order given.</summary>
        /// <param name="statuses">The statuses.</param>
        internal static IReadOnlyList<string> Named(IReadOnlyList<FindingStatus> statuses)
        {
            var words = new string[statuses.Count];

            for (var i = 0; i < statuses.Count; i++) words[i] = Of(statuses[i]);

            return words;
        }
    }
}
