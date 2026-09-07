using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
        /// <summary>
        /// The five statuses, in lattice order.
        ///
        /// The lattice's own list. This file used to write it out again, so the order and the
        /// membership were stated twice and could drift.
        /// </summary>
        internal static IReadOnlyList<FindingStatus> All => FindingStatusLattice.All;

        /// <summary>The five words, in the same order as <see cref="All"/>.</summary>
        internal static IReadOnlyList<string> Words => FindingStatusLattice.Names;

        /// <summary>How a status reads.</summary>
        /// <param name="status">The status.</param>
        internal static string Of(FindingStatus status) => FindingStatusLattice.ToName(status);

        /// <summary>How a status is drawn, everywhere it is drawn.</summary>
        /// <param name="status">The status.</param>
        internal static Tone Reads(FindingStatus status) => status switch
        {
            FindingStatus.New => Tone.Bad,
            FindingStatus.Reviewed => Tone.Warn,
            FindingStatus.Approved => Tone.Good,
            FindingStatus.Resolved => Tone.Good,
            _ => Tone.Plain,
        };

        /// <summary>
        /// A status as the chip the board draws it as.
        ///
        /// One renderer, because the same five words were three visual languages: filled chips on
        /// the Manage rows, coloured figures in its counter strip, and plain body text in both of
        /// Exchange's tables - so the colour a coordinator learns on the board is worth nothing on
        /// the tab where they hand the work to a trade.
        /// </summary>
        /// <param name="status">The status.</param>
        /// <param name="why">What the chip says when pointed at, or null for the word alone.</param>
        internal static UIElement Chip(FindingStatus status, string? why = null)
        {
            var tone = Reads(status);

            var label = new TextBlock
            {
                Text = Of(status),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Skin.Ink(tone),
            };

            var chip = new Border
            {
                // One width for all five, or a column of them is five ragged edges.
                MinWidth = 76,
                Height = 20,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Skin.Wash(tone),
                BorderBrush = Skin.Edge(tone),
                Child = label,
                ToolTip = why ?? Of(status),
            };

            if (tone != Tone.Plain) return chip;

            chip.Tint(Border.BackgroundProperty, "Nav");
            chip.Tint(Border.BorderBrushProperty, "Bd");
            label.Tint(TextBlock.ForegroundProperty, "Muted");

            return chip;
        }

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
