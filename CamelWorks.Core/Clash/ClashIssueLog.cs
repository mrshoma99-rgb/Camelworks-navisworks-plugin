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
        internal ClashIssued(long whenTicks, string? party, FindingStatus status)
        {
            WhenTicks = whenTicks;
            Party = party;
            Status = status;
        }

        /// <summary>When the pack carrying it was written, UTC ticks.</summary>
        public long WhenTicks { get; }

        /// <summary>Who it went to.</summary>
        public string? Party { get; }

        /// <summary>Where it stood when it went.</summary>
        public FindingStatus Status { get; }

        /// <inheritdoc />
        public override string ToString() =>
            new DateTime(WhenTicks, DateTimeKind.Utc).ToLocalTime()
                .ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            + (Party == null ? string.Empty : " to " + Party);
    }

    /// <summary>
    /// Which clashes have been issued, when, and to whom.
    ///
    /// The profile already stamps each test with the last time it went out, which answers "have I
    /// sent this test" and nothing else. A weekly issue is a delta on the results: what is new
    /// since the last pack to this party, what has changed since it went, and what is in progress
    /// and should be left alone. A per-test stamp cannot express any of those, so a pack either
    /// repeated four hundred clashes the trade already had or left out the ones that moved.
    ///
    /// Kept in the project file rather than on the results, because it outlives a run: the point
    /// is to compare this week's board against what actually went out last week.
    /// </summary>
    public sealed class ClashIssueLog
    {
        /// <summary>Where the project file keeps it.</summary>
        public const string Key = "issued";

        private readonly Dictionary<ClashKey, ClashIssued> _sent =
            new Dictionary<ClashKey, ClashIssued>();

        /// <summary>How many clashes the log knows about.</summary>
        public int Count => _sent.Count;

        /// <summary>When one clash last went out, or null when it never has.</summary>
        /// <param name="key">The clash.</param>
        public ClashIssued? Sent(ClashKey key) => _sent.TryGetValue(key, out var had) ? had : null;

        /// <summary>
        /// Record that these clashes have gone out.
        /// </summary>
        /// <param name="items">What the pack carried.</param>
        /// <param name="party">Who it went to.</param>
        /// <param name="whenTicks">When it was written, UTC ticks.</param>
        /// <returns>How many were stamped.</returns>
        public int Stamp(IEnumerable<ClashItem>? items, string? party, long whenTicks)
        {
            if (items == null || whenTicks <= 0) return 0;

            var stamped = 0;

            foreach (var item in items)
            {
                if (item == null) continue;

                _sent[item.Key] = new ClashIssued(whenTicks, party, item.Status);
                stamped++;
            }

            return stamped;
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

            return sent == null ? NotSent
                 : sent.Status != item.Status ? Changed
                 : Unchanged;
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

                log._sent[key] = new ClashIssued(when, one["party"].AsString(), status);
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
