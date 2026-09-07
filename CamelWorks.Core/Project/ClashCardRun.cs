using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Project
{
    /// <summary>
    /// What a card run writes outside the finding register.
    ///
    /// Suppressing and flagging are facts on the finding and Core sets them itself. The other four
    /// reach the Navisworks document as well, because a status, an assignee and a group set here
    /// have to be the same ones the native clash window shows — and a delete has nowhere else to
    /// happen. The surface is narrow on purpose: everything on it is a verb the Manage board
    /// already offers over a picked set of rows.
    /// </summary>
    public interface IClashCardWriter
    {
        /// <summary>Set the status of these results.</summary>
        /// <param name="findings">Which results.</param>
        /// <param name="status">The status to set.</param>
        bool Status(IReadOnlyList<Finding> findings, FindingStatus status);

        /// <summary>Assign these results to a party.</summary>
        /// <param name="findings">Which results.</param>
        /// <param name="party">Who they go to.</param>
        bool Assign(IReadOnlyList<Finding> findings, string party);

        /// <summary>Put these results in a named group.</summary>
        /// <param name="findings">Which results.</param>
        /// <param name="name">What the group is called.</param>
        bool Group(IReadOnlyList<Finding> findings, string name);

        /// <summary>Delete these results from the document.</summary>
        /// <param name="findings">Which results.</param>
        bool Remove(IReadOnlyList<Finding> findings);

        /// <summary>Why the last write returned false.</summary>
        string? LastProblem { get; }
    }

    /// <summary>
    /// The card list, run top to bottom.
    ///
    /// Two passes over the same walk. <see cref="Plan"/> says what each card finds and how much of
    /// it the action would change, and writes nothing; <see cref="Apply"/> does the same walk and
    /// writes. They are one method with a switch rather than two implementations, because a
    /// preview that disagreed with the run would be worse than no preview.
    ///
    /// Order matters and is the whole point of a list: a card that takes results off the board
    /// hides them from every card under it, so "suppress the insulation, then assign what is left
    /// to Mechanical" reads down the screen in the order it happens.
    /// </summary>
    public static class ClashCardRun
    {
        /// <summary>What each card would do, changing nothing.</summary>
        /// <param name="cards">The cards, in the order they run.</param>
        /// <param name="items">The results to run them over.</param>
        /// <param name="findingOf">The finding behind one result, or null when it has none.</param>
        public static IReadOnlyList<ClashCardOutcome> Plan(
            IEnumerable<ClashCard>? cards, IEnumerable<ClashItem>? items,
            Func<string?, Finding?> findingOf) =>
            Walk(cards, items, findingOf, null);

        /// <summary>
        /// Run the cards and write what they say.
        /// </summary>
        /// <param name="cards">The cards, in the order they run.</param>
        /// <param name="items">The results to run them over.</param>
        /// <param name="findingOf">The finding behind one result, or null when it has none.</param>
        /// <param name="writer">Where the four document-side actions go.</param>
        public static IReadOnlyList<ClashCardOutcome> Apply(
            IEnumerable<ClashCard>? cards, IEnumerable<ClashItem>? items,
            Func<string?, Finding?> findingOf, IClashCardWriter writer) =>
            Walk(cards, items, findingOf,
                 writer ?? throw new ArgumentNullException(nameof(writer)));

        /// <summary>The walk both passes share.</summary>
        /// <param name="cards">The cards.</param>
        /// <param name="items">The results.</param>
        /// <param name="findingOf">The finding behind one result.</param>
        /// <param name="writer">Where writes go, or null to change nothing.</param>
        private static IReadOnlyList<ClashCardOutcome> Walk(
            IEnumerable<ClashCard>? cards, IEnumerable<ClashItem>? items,
            Func<string?, Finding?> findingOf, IClashCardWriter? writer)
        {
            if (findingOf == null) throw new ArgumentNullException(nameof(findingOf));

            var outcomes = new List<ClashCardOutcome>();
            var standing = (items ?? Array.Empty<ClashItem>()).ToList();

            // What earlier cards took off the board, so a later card does not act on it. Held here
            // rather than read back off the finding, because a plan writes nothing and still has
            // to answer the same question the run does.
            var gone = new HashSet<string>(StringComparer.Ordinal);

            foreach (var card in cards ?? Array.Empty<ClashCard>())
            {
                if (card == null || !card.IsEnabled) continue;

                if (card.NeedsValue)
                {
                    outcomes.Add(new ClashCardOutcome(card, 0, 0,
                        "This card has no " + Asks(card.Action) + " to set."));

                    continue;
                }

                var when = card.When.Build();
                var found = new List<Finding>();
                var matched = 0;

                foreach (var item in standing)
                {
                    if (item.FindingId != null && gone.Contains(item.FindingId)) continue;
                    if (!when.Matches(item)) continue;

                    matched++;

                    var finding = findingOf(item.FindingId);

                    if (finding != null) found.Add(finding);
                }

                outcomes.Add(Did(card, matched, found, writer, gone));
            }

            return outcomes;
        }

        /// <summary>Apply one card to what it found, or count what it would change.</summary>
        /// <param name="card">The card.</param>
        /// <param name="matched">How many results the criteria found.</param>
        /// <param name="found">The findings behind them.</param>
        /// <param name="writer">Where writes go, or null to change nothing.</param>
        /// <param name="gone">Ids no later card should see, added to here.</param>
        private static ClashCardOutcome Did(ClashCard card, int matched, IReadOnlyList<Finding> found,
                                            IClashCardWriter? writer, ISet<string> gone)
        {
            var value = (card.Value ?? string.Empty).Trim();

            // Only what the action would actually change. A card run twice reports what it found
            // and nothing changed, which is the honest way to say it has already been applied.
            var changing = found.Where(one => Changes(one, card.Action, value)).ToList();

            if (writer == null || changing.Count == 0)
                return new ClashCardOutcome(card, matched, changing.Count);

            switch (card.Action)
            {
                case ClashActionKind.Suppress:
                    foreach (var one in changing)
                    {
                        one.Facts[ClashFindings.SuppressedBy] = card.Name;
                        gone.Add(one.Id);
                    }

                    break;

                case ClashActionKind.Flag:
                    foreach (var one in changing) one.Facts[ClashFindings.FlaggedBy] = card.Name;

                    break;

                case ClashActionKind.Group:
                    if (!writer.Group(changing, value)) return Refused(card, matched, writer);

                    foreach (var one in changing)
                    {
                        one.Group = value;
                        one.Facts[ClashFindings.BoardGroup] = value;
                    }

                    break;

                case ClashActionKind.Assign:
                    if (!writer.Assign(changing, value)) return Refused(card, matched, writer);

                    foreach (var one in changing) one.AssignedTo = value;

                    break;

                case ClashActionKind.Status:
                    if (!FindingStatusLattice.TryParse(value, out var status))
                        return new ClashCardOutcome(card, matched, 0,
                            "“" + value + "” is not a status.");

                    if (!writer.Status(changing, status)) return Refused(card, matched, writer);

                    foreach (var one in changing) one.Status = status;

                    break;

                case ClashActionKind.Remove:
                    if (!writer.Remove(changing)) return Refused(card, matched, writer);

                    // Absent rather than deleted from the register: an issue that names one of
                    // these still has to resolve it to say what it was about.
                    foreach (var one in changing)
                    {
                        one.IsPresent = false;
                        gone.Add(one.Id);
                    }

                    break;
            }

            return new ClashCardOutcome(card, matched, changing.Count);
        }

        /// <summary>Whether an action would change this finding at all.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="action">What the card does.</param>
        /// <param name="value">The group, the party or the status.</param>
        private static bool Changes(Finding finding, ClashActionKind action, string value)
        {
            switch (action)
            {
                case ClashActionKind.Suppress:
                    return !finding.Facts.ContainsKey(ClashFindings.SuppressedBy);

                case ClashActionKind.Flag:
                    return !finding.Facts.ContainsKey(ClashFindings.FlaggedBy);

                case ClashActionKind.Group:
                    return !string.Equals(finding.Group, value, StringComparison.Ordinal);

                case ClashActionKind.Assign:
                    return !string.Equals(finding.AssignedTo, value, StringComparison.Ordinal);

                case ClashActionKind.Status:
                    return FindingStatusLattice.TryParse(value, out var status)
                           && finding.Status != status;

                case ClashActionKind.Remove:
                    return finding.IsPresent;

                default:
                    return false;
            }
        }

        /// <summary>What the document said when it would not take the write.</summary>
        /// <param name="card">The card.</param>
        /// <param name="matched">How many results the criteria found.</param>
        /// <param name="writer">The writer that refused.</param>
        private static ClashCardOutcome Refused(ClashCard card, int matched, IClashCardWriter writer) =>
            new ClashCardOutcome(card, matched, 0,
                                 writer.LastProblem ?? "The document would not take it.");

        /// <summary>What an action that needs a value is missing.</summary>
        /// <param name="action">The action.</param>
        private static string Asks(ClashActionKind action) =>
            action == ClashActionKind.Group ? "group name"
            : action == ClashActionKind.Assign ? "party"
            : "status";
    }
}
