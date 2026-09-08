using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Search;
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

        /// <summary>Write a remark on these results.</summary>
        /// <param name="findings">Which results.</param>
        /// <param name="body">What it says.</param>
        bool Comment(IReadOnlyList<Finding> findings, string body);

        /// <summary>
        /// Attach these results to an issue, raising one under that name if it is not there yet.
        ///
        /// The only action that writes outside the finding register and the document: an issue is
        /// the project's own record, and a rule that raises one is how a week's coordination turns
        /// into something with an owner and a date.
        /// </summary>
        /// <param name="findings">Which results.</param>
        /// <param name="title">What the issue is called.</param>
        bool Raise(IReadOnlyList<Finding> findings, string title);

        /// <summary>
        /// Write a marker sphere per result to an OBJ file.
        ///
        /// The one action that changes nothing about the results. It is here because it is what a
        /// coordinator does with a set of results once they have found them — hand the points to
        /// somebody without this product — and doing it by hand meant filtering the board to the
        /// same set again on the export screen.
        /// </summary>
        /// <param name="findings">Which results.</param>
        /// <param name="path">Where the file goes.</param>
        bool Mark(IReadOnlyList<Finding> findings, string path);

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
        /// <summary>What each rule would do, changing nothing.</summary>
        /// <param name="cards">The rules, in the order they run.</param>
        /// <param name="findings">The results to run them over.</param>
        /// <param name="world">Where the search reads elements, columns and issues.</param>
        public static IReadOnlyList<ClashCardOutcome> Plan(
            IEnumerable<ClashCard>? cards, IEnumerable<Finding>? findings, IResultWorld world) =>
            Walk(cards, findings, world, null);

        /// <summary>
        /// Run the cards and write what they say.
        /// </summary>
        /// <param name="cards">The rules, in the order they run.</param>
        /// <param name="findings">The results to run them over.</param>
        /// <param name="world">Where the search reads elements, columns and issues.</param>
        /// <param name="writer">Where the four document-side actions go.</param>
        public static IReadOnlyList<ClashCardOutcome> Apply(
            IEnumerable<ClashCard>? cards, IEnumerable<Finding>? findings, IResultWorld world,
            IClashCardWriter writer) =>
            Walk(cards, findings, world,
                 writer ?? throw new ArgumentNullException(nameof(writer)));

        /// <summary>The walk both passes share.</summary>
        /// <param name="cards">The rules.</param>
        /// <param name="findings">The results.</param>
        /// <param name="world">Where the search reads elements, columns and issues.</param>
        /// <param name="writer">Where writes go, or null to change nothing.</param>
        private static IReadOnlyList<ClashCardOutcome> Walk(
            IEnumerable<ClashCard>? cards, IEnumerable<Finding>? findings, IResultWorld world,
            IClashCardWriter? writer)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var outcomes = new List<ClashCardOutcome>();
            var standing = (findings ?? Array.Empty<Finding>()).Where(one => one != null).ToList();

            // What earlier rules took off the board, so a later rule does not act on it. Held here
            // rather than read back off the finding, because a plan writes nothing and still has
            // to answer the same question the run does.
            var gone = new HashSet<string>(StringComparer.Ordinal);

            foreach (var card in cards ?? Array.Empty<ClashCard>())
            {
                if (card == null || !card.IsEnabled) continue;

                if (card.NeedsValue)
                {
                    outcomes.Add(new ClashCardOutcome(card, 0, 0,
                        "This rule has no " + Asks(card.Action) + " to set."));

                    continue;
                }

                var found = new List<Finding>();

                foreach (var finding in standing)
                {
                    if (gone.Contains(finding.Id)) continue;

                    // Already off the board, because a previous run took it off or the result is
                    // no longer in the document. A re-run of the same list has to see what the
                    // list left, or the second rule would act on results the first one removed
                    // the first time and not the second.
                    if (Off(finding)) continue;

                    if (!ResultSearch.Matches(card.When, finding, world)) continue;

                    found.Add(finding);
                }

                outcomes.Add(Did(card, found.Count, found, world, writer, gone));
            }

            return outcomes;
        }

        /// <summary>
        /// Take the marks a past run left off these findings.
        ///
        /// Suppressing and flagging used to be derived on every board build, so switching a rule
        /// off un-suppressed what it had suppressed. A card is run on a press and what it writes
        /// stands until something takes it off, so this is that something.
        /// </summary>
        /// <param name="findings">The findings to unmark.</param>
        /// <returns>How many carried a mark.</returns>
        public static int Clear(IEnumerable<Finding>? findings)
        {
            var cleared = 0;

            foreach (var finding in findings ?? Array.Empty<Finding>())
            {
                if (finding == null || !Marked(finding)) continue;

                finding.Facts.Remove(ClashFindings.SuppressedBy);
                finding.Facts.Remove(ClashFindings.FlaggedBy);

                cleared++;
            }

            return cleared;
        }

        /// <summary>Whether a past run marked this finding.</summary>
        /// <param name="finding">The finding.</param>
        public static bool Marked(Finding? finding) =>
            finding != null
            && (finding.Facts.ContainsKey(ClashFindings.SuppressedBy)
                || finding.Facts.ContainsKey(ClashFindings.FlaggedBy));

        /// <summary>Whether a result is already off the board, whatever this run does.</summary>
        /// <param name="finding">The finding behind it, or null when it has none.</param>
        private static bool Off(Finding? finding) =>
            finding != null
            && (!finding.IsPresent || finding.Facts.ContainsKey(ClashFindings.SuppressedBy));

        /// <summary>Apply one card to what it found, or count what it would change.</summary>
        /// <param name="card">The card.</param>
        /// <param name="matched">How many results the criteria found.</param>
        /// <param name="found">The findings behind them.</param>
        /// <param name="world">Where the issue attachment is read from.</param>
        /// <param name="writer">Where writes go, or null to change nothing.</param>
        /// <param name="gone">Ids no later card should see, added to here.</param>
        private static ClashCardOutcome Did(ClashCard card, int matched, IReadOnlyList<Finding> found,
                                            IResultWorld world, IClashCardWriter? writer,
                                            ISet<string> gone)
        {
            var value = (card.Value ?? string.Empty).Trim();

            // Only what the action would actually change. A rule run twice reports what it found
            // and nothing changed, which is the honest way to say it has already been applied.
            var changing = found.Where(one => Changes(one, card.Action, value, world)).ToList();

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

                case ClashActionKind.Comment:
                    if (!writer.Comment(changing, value)) return Refused(card, matched, writer);

                    foreach (var one in changing)
                        one.Comments.Add(new FindingComment(card.Name, DateTime.UtcNow.Ticks, value));

                    break;

                case ClashActionKind.Label:
                    foreach (var one in changing)
                        if (!one.Labels.Contains(value, StringComparer.OrdinalIgnoreCase))
                            one.Labels.Add(value);

                    break;

                case ClashActionKind.Priority:
                    foreach (var one in changing) one.Priority = value;

                    break;

                case ClashActionKind.Issue:
                    if (!writer.Raise(changing, value)) return Refused(card, matched, writer);

                    break;

                case ClashActionKind.Markers:
                    if (!writer.Mark(changing, value)) return Refused(card, matched, writer);

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
        /// <param name="value">What the action was told to set.</param>
        /// <param name="world">Where the issue attachment is read from.</param>
        private static bool Changes(Finding finding, ClashActionKind action, string value,
                                    IResultWorld world)
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

                case ClashActionKind.Comment:
                    // The same remark twice is not a change. A rule run every week would otherwise
                    // leave fifty copies of one sentence on every result it finds.
                    return !finding.Comments.Any(one =>
                        string.Equals(one.Body, value, StringComparison.OrdinalIgnoreCase));

                case ClashActionKind.Label:
                    return !finding.Labels.Contains(value, StringComparer.OrdinalIgnoreCase);

                case ClashActionKind.Priority:
                    return !string.Equals(finding.Priority, value, StringComparison.Ordinal);

                case ClashActionKind.Issue:
                    return !string.Equals(world.IssueOf(finding), value, StringComparison.Ordinal);

                case ClashActionKind.Markers:
                    // Every result the rule found, every run. Writing a file is not a change to a
                    // result, so there is nothing on one to say it has already been written, and
                    // a file written from half the matches would be the wrong file.
                    return true;

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
            action switch
            {
                ClashActionKind.Group => "group name",
                ClashActionKind.Assign => "party",
                ClashActionKind.Status => "status",
                ClashActionKind.Comment => "remark to write",
                ClashActionKind.Label => "label",
                ClashActionKind.Priority => "priority",
                ClashActionKind.Issue => "issue to attach them to",
                ClashActionKind.Markers => "file to write them to",
                _ => "value",
            };
    }
}
