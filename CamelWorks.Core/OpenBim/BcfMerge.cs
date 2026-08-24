using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>What to do when two people answered the same question differently.</summary>
    public enum StatusConflicts
    {
        /// <summary>
        /// The most recently modified answer wins.
        ///
        /// Right when the returns are sequential — a reviewer looked at what somebody else had
        /// already said and changed it on purpose.
        /// </summary>
        Latest,

        /// <summary>
        /// Neither wins. The topic keeps what the baseline said and the disagreement is reported.
        ///
        /// Right when the returns are parallel, which is the usual case: two people were sent the
        /// same pack on Monday and neither saw the other's answer. Picking one silently is how a
        /// closed issue reopens itself a week later with nobody able to say who did it.
        /// </summary>
        Ask,
    }

    /// <summary>One thing two returns disagreed about.</summary>
    public sealed class MergeConflict
    {
        internal MergeConflict(string topicGuid, string title, string field,
                              string baseline, IReadOnlyList<(string Party, string Value)> answers)
        {
            TopicGuid = topicGuid; Title = title; Field = field; Baseline = baseline; Answers = answers;
        }

        /// <summary>Which topic.</summary>
        public string TopicGuid { get; }

        /// <summary>Its title, so the conflict reads without a lookup.</summary>
        public string Title { get; }

        /// <summary>Which field — "status" or "assignment".</summary>
        public string Field { get; }

        /// <summary>What the baseline said.</summary>
        public string Baseline { get; }

        /// <summary>What each return said, by party.</summary>
        public IReadOnlyList<(string Party, string Value)> Answers { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Title + ": " + string.Join(", ", Answers.Select(a => a.Party + " says " + a.Value));
    }

    /// <summary>The merged pack, and everything that needs a person.</summary>
    public sealed class MergeResult
    {
        internal MergeResult(IReadOnlyList<BcfTopic> topics, IReadOnlyList<MergeConflict> conflicts,
                            int changed, int added, int comments)
        {
            Topics = topics; Conflicts = conflicts; Changed = changed; Added = added; Comments = comments;
        }

        /// <summary>Every topic, merged.</summary>
        public IReadOnlyList<BcfTopic> Topics { get; }

        /// <summary>What two returns disagreed about.</summary>
        public IReadOnlyList<MergeConflict> Conflicts { get; }

        /// <summary>How many topics came back with a different status or assignment.</summary>
        public int Changed { get; }

        /// <summary>How many topics a return raised that the baseline never had.</summary>
        public int Added { get; }

        /// <summary>How many comments came back.</summary>
        public int Comments { get; }

        /// <summary>True when nothing has to be decided by hand.</summary>
        public bool IsClean => Conflicts.Count == 0;

        /// <summary>One line saying what came back.</summary>
        public string Explain()
        {
            if (Topics.Count == 0) return "Nothing was returned.";

            var parts = new List<string>();

            if (Changed > 0) parts.Add(Count(Changed, "answer") + " changed");
            if (Comments > 0) parts.Add(Count(Comments, "comment") + " added");
            if (Added > 0) parts.Add(Count(Added, "issue") + " raised");

            var line = parts.Count == 0
                ? "Nothing came back changed"
                : char.ToUpperInvariant(parts[0][0]) + parts[0].Substring(1)
                  + (parts.Count > 1 ? ", " + string.Join(", ", parts.Skip(1)) : string.Empty);

            if (Conflicts.Count > 0)
                line += ", and " + Count(Conflicts.Count, "disagreement") + " to settle";

            return line + ".";
        }

        private static string Count(int n, string noun) =>
            n.ToString("N0", CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? string.Empty : "s");

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>One party's returned pack.</summary>
    public sealed class MergeReturn
    {
        /// <summary>Create one.</summary>
        /// <param name="party">Who sent it back. Used to name them in a conflict.</param>
        /// <param name="topics">What they returned.</param>
        public MergeReturn(string party, IReadOnlyList<BcfTopic> topics)
        {
            Party = string.IsNullOrWhiteSpace(party) ? "someone" : party.Trim();
            Topics = topics ?? Array.Empty<BcfTopic>();
        }

        /// <summary>Who sent it back.</summary>
        public string Party { get; }

        /// <summary>What they returned.</summary>
        public IReadOnlyList<BcfTopic> Topics { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Party + " (" + Topics.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Bringing several reviewers' answers back into one pack.
    ///
    /// This is the whole difference between a coordination tool one person drives and one a team
    /// uses. Issues go out to six parties on Monday; six packs come back on Thursday, each holding
    /// the same forty issues with a handful answered. Without a merge, somebody opens all six by
    /// hand and retypes the answers, which is why the answers stop coming back.
    ///
    /// Two rules, and the second is the one that matters.
    ///
    /// COMMENTS ARE NEVER DROPPED. Every comment from every return survives, matched by its own
    /// GUID so the same pack merged twice does not double them. A comment is somebody's reasoning,
    /// and a merge that discards one to resolve a conflict has thrown away the thing that would
    /// have settled it.
    ///
    /// A DISAGREEMENT IS NOT RESOLVED QUIETLY. Two reviewers who answered the same issue
    /// differently did not see each other's answer - they were sent the same pack at the same time.
    /// Under <see cref="StatusConflicts.Ask"/> the topic keeps what it had and the disagreement is
    /// reported with both names on it, because picking one silently is how an issue somebody closed
    /// reopens itself a week later with nobody able to say who did it.
    /// </summary>
    public static class BcfMerge
    {
        /// <summary>Merge the returns onto the baseline.</summary>
        /// <param name="baseline">The pack as it went out.</param>
        /// <param name="returns">What came back, by party.</param>
        /// <param name="policy">What to do when two returns disagree.</param>
        public static MergeResult Merge(IReadOnlyList<BcfTopic>? baseline,
                                        IReadOnlyList<MergeReturn>? returns,
                                        StatusConflicts policy = StatusConflicts.Ask)
        {
            var conflicts = new List<MergeConflict>();
            var merged = new List<BcfTopic>();

            var sent = baseline ?? Array.Empty<BcfTopic>();
            var back = returns ?? Array.Empty<MergeReturn>();

            var byGuid = new Dictionary<string, BcfTopic>(StringComparer.OrdinalIgnoreCase);

            foreach (var topic in sent)
            {
                if (topic == null) continue;
                byGuid[topic.Guid] = topic;
                merged.Add(topic);
            }

            var changed = 0;
            var added = 0;
            var comments = 0;

            // Every party's answer to every topic, gathered before anything is decided, so a
            // three-way disagreement reads as one conflict naming three people rather than two
            // conflicts that each look like a pair.
            var statuses = new Dictionary<string, List<(string Party, string Value, DateTimeOffset? When)>>(
                StringComparer.OrdinalIgnoreCase);

            var assignments = new Dictionary<string, List<(string Party, string Value, DateTimeOffset? When)>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var party in back)
            {
                if (party == null) continue;

                foreach (var topic in party.Topics)
                {
                    if (topic == null) continue;

                    if (!byGuid.TryGetValue(topic.Guid, out var onto))
                    {
                        // A reviewer raised something the pack did not have. Kept: it is the most
                        // valuable thing in the return, and dropping it teaches people not to.
                        byGuid[topic.Guid] = topic;
                        merged.Add(topic);
                        added++;
                        comments += topic.Comments.Count;
                        continue;
                    }

                    comments += Absorb(onto, topic);

                    Note(statuses, onto, topic.TopicStatus, party.Party, topic.ModifiedDate);

                    if (!string.IsNullOrWhiteSpace(topic.AssignedTo))
                        Note(assignments, onto, topic.AssignedTo!, party.Party, topic.ModifiedDate);
                }
            }

            changed += Settle(byGuid, statuses, "status", policy, conflicts,
                              t => t.TopicStatus, (t, v) => t.TopicStatus = v);

            changed += Settle(byGuid, assignments, "assignment", policy, conflicts,
                              t => t.AssignedTo ?? string.Empty, (t, v) => t.AssignedTo = v);

            return new MergeResult(merged, conflicts, changed, added, comments);
        }

        /// <summary>Take a return's comments, without taking any of them twice.</summary>
        private static int Absorb(BcfTopic onto, BcfTopic from)
        {
            var have = new HashSet<string>(onto.Comments.Select(c => c.Guid), StringComparer.OrdinalIgnoreCase);
            var taken = 0;

            foreach (var comment in from.Comments)
            {
                if (comment == null || !have.Add(comment.Guid)) continue;

                onto.Comments.Add(comment);
                taken++;
            }

            return taken;
        }

        private static void Note(Dictionary<string, List<(string, string, DateTimeOffset?)>> into,
                                 BcfTopic topic, string value, string party, DateTimeOffset? when)
        {
            if (!into.TryGetValue(topic.Guid, out var list))
            {
                list = new List<(string, string, DateTimeOffset?)>();
                into[topic.Guid] = list;
            }

            list.Add((party, value, when));
        }

        private static int Settle(Dictionary<string, BcfTopic> topics,
                                  Dictionary<string, List<(string Party, string Value, DateTimeOffset? When)>> answers,
                                  string field,
                                  StatusConflicts policy,
                                  List<MergeConflict> conflicts,
                                  Func<BcfTopic, string> read,
                                  Action<BcfTopic, string> write)
        {
            var changed = 0;

            foreach (var entry in answers)
            {
                if (!topics.TryGetValue(entry.Key, out var topic)) continue;

                var was = read(topic);

                var different = entry.Value
                    .Where(a => !string.Equals(a.Value, was, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (different.Count == 0) continue;

                var distinct = different
                    .Select(a => a.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (distinct.Count > 1)
                {
                    if (policy == StatusConflicts.Ask)
                    {
                        conflicts.Add(new MergeConflict(topic.Guid, topic.Title, field, was,
                            different.Select(a => (a.Party, a.Value)).ToList()));
                        continue;
                    }

                    // Latest: the most recently modified answer wins. An answer with no modified
                    // date loses to one that has a date, because a reviewer whose tool stamped the
                    // time did more to say when they meant it.
                    var winner = different
                        .OrderByDescending(a => a.When ?? DateTimeOffset.MinValue)
                        .First();

                    write(topic, winner.Value);
                    changed++;
                    continue;
                }

                write(topic, distinct[0]);
                changed++;
            }

            return changed;
        }
    }
}
