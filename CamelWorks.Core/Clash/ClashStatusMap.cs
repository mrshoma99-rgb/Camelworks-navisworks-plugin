using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// One status word from a returned pack, and where it lands.
    /// </summary>
    public sealed class ClashStatusWord
    {
        /// <param name="word">The word as the file spelt it. Empty when the topic set none.</param>
        /// <param name="lands">The board status it maps onto.</param>
        /// <param name="isGuess">True when the conventional vocabulary did not hold the word and
        /// it was read for its sense, or fell through to the default.</param>
        /// <param name="count">How many topics in the packs used it.</param>
        internal ClashStatusWord(string word, FindingStatus lands, bool isGuess, int count)
        {
            Word = word; Lands = lands; IsGuess = isGuess; Count = count;
        }

        /// <summary>The word as the file spelt it. Empty when the topic set none.</summary>
        public string Word { get; }

        /// <summary>The board status it maps onto.</summary>
        public FindingStatus Lands { get; }

        /// <summary>True when the word is not one of the conventional ones.</summary>
        public bool IsGuess { get; }

        /// <summary>How many topics used it.</summary>
        public int Count { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            (Word.Length == 0 ? "(no status)" : Word) + " becomes " + Lands
            + (IsGuess ? ", which is a reading rather than a mapping" : string.Empty);

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Where a BCF status word lands on the board, in one place.
    ///
    /// There were three readings of the same word: the conventional vocabulary in
    /// <see cref="BcfResponse.StatusOf"/>, a private substring pass in the clash reader that
    /// disagreed with it, and the exact-name parse in <see cref="FindingStatusLattice.TryParse"/>.
    /// The same file read through two paths gave two statuses. This is the single answer, in two
    /// documented tiers:
    ///
    /// <list type="number">
    /// <item>The conventional vocabulary. Exact, and the only tier a caller that must not guess
    /// uses.</item>
    /// <item>The word read for its sense, for tools that write their own vocabulary.</item>
    /// </list>
    ///
    /// A word neither tier answers becomes <see cref="FindingStatus.Active"/> — the import has to
    /// put it somewhere — but <see cref="Tally"/> reports it so the screen can name it before the
    /// coordinator applies anything.
    /// </summary>
    public static class ClashStatusMap
    {
        /// <summary>Where a topic with no status word at all lands.</summary>
        public const FindingStatus Unsaid = FindingStatus.New;

        /// <summary>Where a word neither tier answers lands.</summary>
        public const FindingStatus Unknown = FindingStatus.Active;

        /// <summary>
        /// The single answer for one word.
        /// </summary>
        /// <param name="word">The topic status from the file.</param>
        public static FindingStatus Read(string? word)
        {
            if (string.IsNullOrWhiteSpace(word)) return Unsaid;

            return BcfResponse.StatusOf(word) ?? BcfResponse.SenseOf(word) ?? Unknown;
        }

        /// <summary>True when the conventional vocabulary holds the word.</summary>
        /// <param name="word">The topic status from the file.</param>
        public static bool IsConventional(string? word) => BcfResponse.StatusOf(word) != null;

        /// <summary>
        /// Every distinct word in a set of packs, where each lands, and how many topics used it.
        ///
        /// Ordered by count, so the forty topics one trade marked "Rejected" lead. Guesses sort
        /// first within a count because they are the ones worth reading.
        /// </summary>
        /// <param name="words">The status word of every topic, however spelt.</param>
        public static IReadOnlyList<ClashStatusWord> Tally(IEnumerable<string?> words)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var word in words ?? Enumerable.Empty<string?>())
            {
                var said = (word ?? string.Empty).Trim();

                counts.TryGetValue(said, out var seen);
                counts[said] = seen + 1;
            }

            return counts
                .Select(pair => new ClashStatusWord(
                    pair.Key, Read(pair.Key),
                    pair.Key.Length > 0 && !IsConventional(pair.Key),
                    pair.Value))
                .OrderByDescending(word => word.Count)
                .ThenByDescending(word => word.IsGuess)
                .ThenBy(word => word.Word, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
