using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>One returned topic, and what it would do to the group it came from.</summary>
    public sealed class TopicResponse
    {
        internal TopicResponse(BcfTopic topic, string groupName, FindingStatus current,
                               FindingStatus? incoming, bool isReopen, string? unknownStatus)
        {
            Topic = topic;
            GroupName = groupName;
            Current = current;
            Incoming = incoming;
            IsReopen = isReopen;
            UnknownStatus = unknownStatus;

            Merged = BcfResponse.Merge(current, incoming, isReopen);
        }

        /// <summary>The topic as it arrived.</summary>
        public BcfTopic Topic { get; }

        /// <summary>The board group it belongs to.</summary>
        public string GroupName { get; }

        /// <summary>Where the board has it now.</summary>
        public FindingStatus Current { get; }

        /// <summary>What the file says, or null when the status word was not one CamelWorks knows.</summary>
        public FindingStatus? Incoming { get; }

        /// <summary>Where it would end up.</summary>
        public FindingStatus Merged { get; }

        /// <summary>True when the file says somebody deliberately re-opened this.</summary>
        public bool IsReopen { get; }

        /// <summary>
        /// The status word, when it was not one CamelWorks recognises.
        ///
        /// Reported rather than guessed at. BCF lets a project define its own statuses, and a tool
        /// that silently mapped "Awaiting client" onto Active would be inventing a decision nobody
        /// made.
        /// </summary>
        public string? UnknownStatus { get; }

        /// <summary>Who the file says is responsible, when it says.</summary>
        public string? AssignedTo => string.IsNullOrWhiteSpace(Topic.AssignedTo) ? null : Topic.AssignedTo;

        /// <summary>True when applying this would move the group.</summary>
        public bool Changes => Merged != Current;

        /// <summary>
        /// True when the file asked for a lower status than the board holds and did not get it.
        ///
        /// Worth its own flag and its own line on screen: this is the case where CamelWorks
        /// deliberately did not do what the file said, and the person needs to know that rather
        /// than wonder why the row did not move.
        /// </summary>
        public bool Refused => Incoming != null && !IsReopen && Incoming.Value < Current;

        /// <summary>How the row reads.</summary>
        public string Explain()
        {
            if (UnknownStatus != null)
                return GroupName + " - the file says \"" + UnknownStatus + "\", which is not a status "
                     + "CamelWorks knows, so nothing was changed";

            if (Refused)
                return GroupName + " - the file says " + FindingStatusLattice.ToName(Incoming!.Value)
                     + " and the board says " + FindingStatusLattice.ToName(Current)
                     + ", so it stays where it is";

            if (!Changes) return GroupName + " - already " + FindingStatusLattice.ToName(Current);

            return GroupName + " - " + FindingStatusLattice.ToName(Current) + " becomes "
                 + FindingStatusLattice.ToName(Merged)
                 + (IsReopen ? ", because the file re-opened it" : string.Empty);
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>What a returned BCF file would do to the board.</summary>
    public sealed class BcfResponseReport
    {
        internal BcfResponseReport(IReadOnlyList<TopicResponse> matched, IReadOnlyList<BcfTopic> unmatched)
        {
            Matched = matched;
            Unmatched = unmatched;
        }

        /// <summary>Topics that belong to a group on this board.</summary>
        public IReadOnlyList<TopicResponse> Matched { get; }

        /// <summary>
        /// Topics this project has never exported.
        ///
        /// Not an error and not ignored. Somebody else's coordinator raising issues in their own
        /// tool and sending them over is the normal way a BCF conversation starts, and the count is
        /// how a reader knows whether the file they were sent is a reply or a new list.
        /// </summary>
        public IReadOnlyList<BcfTopic> Unmatched { get; }

        /// <summary>How many groups would move.</summary>
        public int Changes => Matched.Count(m => m.Changes);

        /// <summary>How many the file asked to lower and CamelWorks kept where they are.</summary>
        public int Refused => Matched.Count(m => m.Refused);

        /// <summary>How many carried a status word nobody could map.</summary>
        public int Unknown => Matched.Count(m => m.UnknownStatus != null);

        /// <summary>How many were deliberately re-opened.</summary>
        public int Reopened => Matched.Count(m => m.IsReopen && m.Changes);

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var s = Changes.ToString("N0", CultureInfo.InvariantCulture) + " of "
                  + Matched.Count.ToString("N0", CultureInfo.InvariantCulture)
                  + " returned topics would change the board";

            if (Reopened > 0) s += " - " + Reopened.ToString("N0", CultureInfo.InvariantCulture) + " re-opened";
            if (Refused > 0) s += " - " + Refused.ToString("N0", CultureInfo.InvariantCulture) + " left where they are";
            if (Unknown > 0) s += " - " + Unknown.ToString("N0", CultureInfo.InvariantCulture) + " with a status nobody knows";

            if (Unmatched.Count > 0)
                s += " - " + Unmatched.Count.ToString("N0", CultureInfo.InvariantCulture)
                   + " topics this project never sent";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Reads a BCF file that came back, and works out what it says about the board.
    ///
    /// Exporting BCF and never reading one is half a feature: the format exists so an issue can go
    /// out, be worked on somewhere else, and come back with an answer. What comes back is somebody
    /// else's opinion about statuses this board already holds, and reconciling the two is the whole
    /// difficulty.
    ///
    /// Two rules, and the difference between them is the point:
    ///
    /// <list type="bullet">
    /// <item><b>An ordinary merge never loses progress.</b> Two people edited the same issue while
    /// neither could see the other, so the higher status wins. A stale "Open" in a file somebody
    /// exported on Tuesday must not undo an "Approved" made on Wednesday.</item>
    /// <item><b>An explicit re-open is honoured, even downwards.</b> "ReOpened" is not a stale
    /// edit; it is somebody saying, in front of the evidence, that the thing you closed is not
    /// fixed. Refusing that on the grounds that it lowers a status would make CamelWorks the tool
    /// that argues with the site.</item>
    /// </list>
    ///
    /// Nothing is applied here. This works out what would happen so a person can look at it first.
    /// </summary>
    public static class BcfResponse
    {
        /// <summary>The BCF status words that mean somebody deliberately re-opened an issue.</summary>
        private static readonly string[] Reopens = { "REOPENED", "RE-OPENED", "RE OPENED" };

        /// <summary>
        /// Map a BCF status word onto a board status, or null when it is not one we know.
        ///
        /// BCF lets every project define its own status vocabulary, so the list below is the
        /// conventional one plus the words people actually type. Anything else returns null and is
        /// reported by name — a tool that quietly mapped "Awaiting client" onto Active would be
        /// inventing a decision nobody made.
        /// </summary>
        /// <param name="status">The topic status from the file.</param>
        public static FindingStatus? StatusOf(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return null;

            switch (status!.Trim().ToUpperInvariant())
            {
                case "OPEN":
                case "ACTIVE":
                case "IN PROGRESS":
                case "INPROGRESS":
                case "REOPENED":
                case "RE-OPENED":
                case "RE OPENED":
                    return FindingStatus.Active;

                case "REVIEWED":
                case "FOR REVIEW":
                    return FindingStatus.Reviewed;

                case "APPROVED":
                case "ACCEPTED":
                    return FindingStatus.Approved;

                case "CLOSED":
                case "RESOLVED":
                case "FIXED":
                case "DONE":
                    return FindingStatus.Resolved;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Where a returned status leaves a group.
        ///
        /// The central rule of every inbound leg, in one place because there are two of them — a
        /// BCF file and a returned worksheet — and two copies of it would eventually disagree
        /// about somebody's approval.
        ///
        /// An explicit re-open is a decision somebody made in front of the evidence, so it is
        /// honoured even downwards; refusing it would make CamelWorks the tool that argues with
        /// the site. Everything else is a merge of two edits neither author knew about, and the
        /// lattice's promote-only rule is what stops a stale "Open" undoing a real "Approved".
        /// </summary>
        /// <param name="current">Where the board has it now.</param>
        /// <param name="incoming">What came back, or null when the word was not one we know.</param>
        /// <param name="isReopen">True when the word means a deliberate re-open.</param>
        public static FindingStatus Merge(FindingStatus current, FindingStatus? incoming, bool isReopen) =>
            incoming == null ? current
            : isReopen ? incoming.Value
            : FindingStatusLattice.Merge(current, incoming.Value);

        /// <summary>True when the word means somebody deliberately re-opened the issue.</summary>
        /// <param name="status">The topic status from the file.</param>
        public static bool IsReopen(string? status) =>
            status != null && Reopens.Contains(status.Trim().ToUpperInvariant(), StringComparer.Ordinal);

        /// <summary>Work out what a returned file would do, without doing any of it.</summary>
        /// <param name="topics">The topics read from the file.</param>
        /// <param name="groups">Topic GUID to the board group it was exported from.</param>
        /// <param name="current">Board group to the status it holds now.</param>
        public static BcfResponseReport Read(IEnumerable<BcfTopic> topics,
                                             IReadOnlyDictionary<string, string> groups,
                                             IReadOnlyDictionary<string, FindingStatus> current)
        {
            if (topics == null) throw new ArgumentNullException(nameof(topics));
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (current == null) throw new ArgumentNullException(nameof(current));

            var matched = new List<TopicResponse>();
            var unmatched = new List<BcfTopic>();

            foreach (var topic in topics)
            {
                if (topic == null) continue;

                if (!groups.TryGetValue(topic.Guid, out var group))
                {
                    unmatched.Add(topic);
                    continue;
                }

                var incoming = StatusOf(topic.TopicStatus);
                var reopen = IsReopen(topic.TopicStatus);

                var held = current.TryGetValue(group, out var status) ? status : FindingStatus.Active;

                matched.Add(new TopicResponse(topic, group, held, incoming, reopen,
                                              incoming == null ? topic.TopicStatus : null));
            }

            return new BcfResponseReport(matched, unmatched);
        }
    }
}
