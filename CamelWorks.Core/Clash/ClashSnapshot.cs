using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Clash
{
    /// <summary>One result as a previous run left it.</summary>
    public sealed class ClashRecord
    {
        /// <summary>Create a record.</summary>
        public ClashRecord(ClashKey key, FindingStatus status, string? groupName = null,
                           bool groupWasPinned = false, string? testName = null)
        {
            Key = key;
            Status = status;
            GroupName = groupName;
            GroupWasPinned = groupWasPinned;
            TestName = testName;
        }

        /// <summary>Identity.</summary>
        public ClashKey Key { get; }

        /// <summary>Where it stood.</summary>
        public FindingStatus Status { get; }

        /// <summary>The group it was in.</summary>
        public string? GroupName { get; }

        /// <summary>True when that group was hand-made rather than derived by the stack.</summary>
        public bool GroupWasPinned { get; }

        /// <summary>The test that produced it.</summary>
        public string? TestName { get; }

        /// <inheritdoc />
        public override string ToString() =>
            FindingStatusLattice.ToName(Status) + " " + Key;
    }

    /// <summary>
    /// A board as it stood at the end of a run — the thing this week is compared against.
    ///
    /// Held separately from the live board because a delta needs a fixed reference. Comparing
    /// against "whatever the sidecar says right now" would make the delta change under a colleague
    /// who saved while you were reading it.
    /// </summary>
    public sealed class ClashSnapshot
    {
        /// <summary>Create a snapshot.</summary>
        public ClashSnapshot(IReadOnlyList<ClashRecord> records,
                             IReadOnlyDictionary<string, GroupDecision>? decisions = null,
                             string? label = null,
                             IReadOnlyDictionary<string, int>? openByParty = null,
                             DateTimeOffset? takenUtc = null)
        {
            Records = records ?? throw new ArgumentNullException(nameof(records));
            Decisions = decisions ?? new Dictionary<string, GroupDecision>(StringComparer.Ordinal);
            Label = label;
            OpenByParty = openByParty ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            TakenUtc = takenUtc;
        }

        /// <summary>Every result the run ended with.</summary>
        public IReadOnlyList<ClashRecord> Records { get; }

        /// <summary>The decisions people had made, keyed by group name.</summary>
        public IReadOnlyDictionary<string, GroupDecision> Decisions { get; }

        /// <summary>How the snapshot reads on screen — a date, a milestone, an issue number.</summary>
        public string? Label { get; }

        /// <summary>
        /// How many findings each party still had open when this was taken.
        ///
        /// Recorded here rather than derived from <see cref="Records"/>, because a record carries
        /// its group and not its party, and the mapping from group to party is a thing people
        /// change. Answering "what did Mechanical have open last week" from this week's assignment
        /// rules would restate history every time somebody re-assigned a group.
        /// </summary>
        public IReadOnlyDictionary<string, int> OpenByParty { get; }

        /// <summary>When it was taken, UTC.</summary>
        public DateTimeOffset? TakenUtc { get; }

        /// <summary>An empty snapshot — what the first run compares against.</summary>
        public static ClashSnapshot Empty { get; } = new ClashSnapshot(Array.Empty<ClashRecord>());

        /// <summary>
        /// Take a snapshot of a finished run, so the next one has something to compare against.
        /// </summary>
        /// <param name="result">The finished run.</param>
        /// <param name="label">How it reads on screen — a date, a milestone, an issue number.</param>
        /// <param name="takenUtc">
        /// When it was taken. Passed in rather than read from the clock, so a snapshot written
        /// twice from the same run is the same snapshot.
        /// </param>
        public static ClashSnapshot Of(ClashPipelineResult result, string? label = null,
                                       DateTimeOffset? takenUtc = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var records = new List<ClashRecord>();
            var decisions = new Dictionary<string, GroupDecision>(StringComparer.Ordinal);
            var open = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in result.Groups)
            {
                var party = string.IsNullOrWhiteSpace(group.AssignedTo)
                    ? PartyLedger.Nobody : group.AssignedTo!;

                var outstanding = group.Items.Count(i => FindingStatusLattice.IsOpen(i.Status));

                if (outstanding > 0)
                    open[party] = open.TryGetValue(party, out var seen) ? seen + outstanding : outstanding;

                // An instruction counts as a decision even where the party came from a rule: it is
                // the only thing on the board that no rule can reproduce, so losing it on a re-run
                // would lose the one part a person actually authored.
                if (group.AssignedByHand || group.Instruction.IsStated)
                    decisions[group.Name] = new GroupDecision(
                        group.AssignedByHand ? group.AssignedTo : null,
                        group.AssignedByHand ? group.Priority : null,
                        group.Instruction);

                foreach (var item in group.Items)
                    records.Add(new ClashRecord(item.Key, item.Status, group.Name, group.IsPinned, item.TestName));
            }

            // Suppressed results are recorded too. Dropping them would make every one of them
            // arrive as New the moment somebody turns the rule off.
            foreach (var item in result.Suppressed)
                records.Add(new ClashRecord(item.Key, item.Status, null, false, item.TestName));

            return new ClashSnapshot(records, decisions, label, open, takenUtc);
        }
    }
}
