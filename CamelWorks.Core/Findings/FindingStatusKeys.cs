using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// The digit that stands for each status when a board is worked from the keyboard.
    ///
    /// Derived from <see cref="FindingStatusLattice.All"/> rather than written out, so a status
    /// added to the lattice gets a digit and the sheet that lists them gets a line, in the same
    /// order, without either being edited. A hand-written map is how "1 is Reviewed" and the help
    /// page saying "1 is Approved" end up in the same product.
    ///
    /// This lives in Core and not beside the board because it is data: which digit means what is
    /// testable, and the board's key handling is not. The board translates a WPF key into a digit
    /// and asks here what it means.
    /// </summary>
    public static class FindingStatusKeys
    {
        /// <summary>The digit for a status: its place in the lattice, counting from one.</summary>
        /// <param name="status">The status.</param>
        public static int Digit(FindingStatus status)
        {
            var at = FindingStatusLattice.All.ToList().IndexOf(status);

            if (at < 0) throw new ArgumentOutOfRangeException(nameof(status));

            return at + 1;
        }

        /// <summary>
        /// What a digit sets, or null when it sets nothing.
        ///
        /// Null rather than a throw: a keystroke the board has no meaning for is an ordinary
        /// thing, and the board passes it on rather than failing on it.
        /// </summary>
        /// <param name="digit">The digit pressed, counting from one.</param>
        public static FindingStatus? Of(int digit) =>
            digit >= 1 && digit <= FindingStatusLattice.All.Count
                ? FindingStatusLattice.All[digit - 1]
                : (FindingStatus?)null;

        /// <summary>Every digit and the status it sets, in the lattice's own order.</summary>
        public static IReadOnlyList<KeyValuePair<int, FindingStatus>> All { get; } =
            FindingStatusLattice.All
                .Select((status, at) => new KeyValuePair<int, FindingStatus>(at + 1, status))
                .ToList();
    }
}
