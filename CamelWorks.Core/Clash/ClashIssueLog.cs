using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>When one clash last went out, to whom, and where it stood at the time.</summary>
    public sealed class ClashIssued
    {
        /// <param name="whenTicks">When the pack carrying it was written, UTC ticks.</param>
        /// <param name="party">Who it went to.</param>
        /// <param name="status">Where it stood when it went.</param>
        /// <param name="assigned">Who it was assigned to when it went.</param>
        internal ClashIssued(long whenTicks, string? party, FindingStatus status,
                             string? assigned = null)
        {
            WhenTicks = whenTicks;
            Party = party;
            Status = status;
            Assigned = assigned;
        }

        /// <summary>When the pack carrying it was written, UTC ticks.</summary>
        public long WhenTicks { get; }

        /// <summary>Who it went to.</summary>
        public string? Party { get; }

        /// <summary>Where it stood when it went.</summary>
        public FindingStatus Status { get; }

        /// <summary>Who it was assigned to when it went, or null when it was nobody's.</summary>
        public string? Assigned { get; }

        /// <inheritdoc />
        public override string ToString() =>
            new DateTime(WhenTicks, DateTimeKind.Utc).ToLocalTime()
                .ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            + (Party == null ? string.Empty : " to " + Party);
    }

    /// <summary>
    /// Which clashes have been issued, when, and to whom.
    ///
    /// The profile stamps each test with the last time it went out, which answers "have I sent
    /// this test" and nothing else. A weekly issue is a delta on the results: what is new since
    /// the last pack, what has changed since it went, and what is in progress. A per-test stamp
    /// expresses none of those.
    ///
    /// Kept in the project file rather than on the results, because it outlives a run: the point
    /// is to compare this week's board against what actually went out last week.
    /// </summary>
    public sealed class ClashIssueLog
    {
        /// <summary>
        /// Where the project file keeps it.
        ///
        /// Not "issued": the report writer keeps its revision log under that name.
        /// </summary>
        public const string Key = "issuedClashes";

        private readonly Dictionary<ClashKey, ClashIssued> _sent =
            new Dictionary<ClashKey, ClashIssued>();

        /// <summary>How many clashes the log knows about.</summary>
        public int Count => _sent.Count;

        /// <summary>
        /// How long an entry is kept once nothing has re-stamped it.
        ///
        /// The log is written into the project file, which is parsed whole on load and written
        /// whole on save, and nothing else takes an entry out: a clash that is resolved and gone
        /// from the board would keep its entry for the life of the project. A year is longer than
        /// any issue cycle the log is asked about and it bounds the file.
        /// </summary>
        public static readonly TimeSpan Keep = TimeSpan.FromDays(365);

        /// <summary>
        /// When one clash last went out, or null when it never has.
        ///
        /// The cell the key quantises to moves when the model is re-exported and the intersection
        /// point crosses a boundary, so an exact lookup answers "never sent" for a clash that went
        /// out last week. The neighbouring cells are searched for the same element pair, which is
        /// what <c>ClashCarryOver</c> does to keep a decision attached across the same drift.
        /// </summary>
        /// <param name="key">The clash.</param>
        public ClashIssued? Sent(ClashKey key)
        {
            if (_sent.TryGetValue(key, out var had)) return had;

            foreach (var near in key.NeighbourCells())
                if (_sent.TryGetValue(near, out var moved))
                    return moved;

            return null;
        }

        /// <summary>
        /// Record that these clashes have gone out.
        ///
        /// An entry the clash has drifted away from is taken out as its replacement is written,
        /// so a re-exported model leaves one entry per clash rather than one per cell it has ever
        /// quantised into. Entries nothing has re-stamped inside <see cref="Keep"/> go with it.
        /// </summary>
        /// <param name="items">What the pack carried.</param>
        /// <param name="party">Who it went to, or null when the pack was not split by party.</param>
        /// <param name="whenTicks">When it was written, UTC ticks.</param>
        /// <returns>How many were stamped.</returns>
        public int Stamp(IEnumerable<ClashItem>? items, string? party, long whenTicks)
        {
            if (items == null || whenTicks <= 0) return 0;

            var stamped = 0;

            foreach (var item in items)
            {
                if (item == null) continue;

                if (!_sent.ContainsKey(item.Key))
                    foreach (var near in item.Key.NeighbourCells())
                        if (_sent.Remove(near)) break;

                _sent[item.Key] = new ClashIssued(whenTicks, party, item.Status, item.AssignedTo);
                stamped++;
            }

            Forget(whenTicks - Keep.Ticks);

            return stamped;
        }

        /// <summary>Drop every entry older than a moment.</summary>
        /// <param name="beforeTicks">The moment, UTC ticks.</param>
        /// <returns>How many were dropped.</returns>
        public int Forget(long beforeTicks)
        {
            if (beforeTicks <= 0) return 0;

            var stale = _sent.Where(pair => pair.Value.WhenTicks < beforeTicks)
                             .Select(pair => pair.Key)
                             .ToList();

            foreach (var key in stale) _sent.Remove(key);

            return stale.Count;
        }

        /// <summary>
        /// Where one clash stands against the last pack that carried it.
        ///
        /// The three answers a weekly issue is built from, in the words the screen uses.
        /// </summary>
        /// <param name="item">The clash.</param>
        public string Standing(ClashItem? item)
        {
            if (item == null) return NotSent;

            var sent = Sent(item.Key);

            if (sent == null) return NotSent;

            // Status and assignee both. A clash reassigned from the structural engineer to the
            // mechanical contractor has changed for everyone who reads the pack, and comparing
            // only the status reported it as one the trade already has.
            var moved = sent.Status != item.Status
                     || !string.Equals(sent.Assigned ?? string.Empty,
                                       item.AssignedTo ?? string.Empty, StringComparison.Ordinal);

            return moved ? Changed : Unchanged;
        }

        /// <summary>A clash no pack has carried.</summary>
        public const string NotSent = "Not sent";

        /// <summary>A clash that has gone out and moved since.</summary>
        public const string Changed = "Changed since sent";

        /// <summary>A clash that has gone out and is where it was.</summary>
        public const string Unchanged = "Sent, unchanged";

        /// <summary>The three answers, in the order a person reads them.</summary>
        public static IReadOnlyList<string> Standings { get; } =
            new[] { NotSent, Changed, Unchanged };

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object();

            foreach (var pair in _sent)
            {
                var one = JsonValue.Object().Set("when", pair.Value.WhenTicks)
                                            .Set("status", pair.Value.Status.ToString());

                if (pair.Value.Party != null) one.Set("party", pair.Value.Party);
                if (pair.Value.Assigned != null) one.Set("assigned", pair.Value.Assigned);

                json.Set(pair.Key.ToString(), one);
            }

            return json;
        }

        /// <summary>Read it back. An unreadable entry is skipped rather than failing the load.</summary>
        /// <param name="json">The stored log.</param>
        public static ClashIssueLog FromJson(JsonValue? json)
        {
            var log = new ClashIssueLog();

            if (json == null || json.Kind != JsonKind.Object) return log;

            foreach (var said in json.Keys)
            {
                if (!ClashKey.TryParse(said, out var key)) continue;

                var one = json[said];
                var when = one["when"].AsLong();

                if (when <= 0) continue;

                Enum.TryParse<FindingStatus>(one["status"].AsString(), out var status);

                log._sent[key] = new ClashIssued(when, one["party"].AsString(), status,
                                                 one["assigned"].AsString());
            }

            return log;
        }

        /// <summary>
        /// The facets a filter can name once there is a log to read.
        ///
        /// Added to the standing list rather than built into it, because a profile written before
        /// anything went out has no log and these three would all answer the same way for every
        /// result.
        /// </summary>
        public IReadOnlyList<ClashFacet> Facets() => new[]
        {
            new ClashFacet("issued", "Since last sent", item => Standing(item)),
            new ClashFacet("issuedTo", "Last sent to", item => Sent(item.Key)?.Party),
            new ClashFacet("issuedOn", "Last sent on", item => Sent(item.Key)?.ToString(),
                           item => (double?)Sent(item.Key)?.WhenTicks),
        };
    }
}
