using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Issues
{
    /// <summary>What an import did, in the numbers somebody checks it by.</summary>
    public sealed class IssueImportResult
    {
        internal IssueImportResult(int added, int updated, int comments, IReadOnlyList<string> notes)
        {
            Added = added; Updated = updated; Comments = comments; Notes = notes;
        }

        /// <summary>Topics that became new issues.</summary>
        public int Added { get; }

        /// <summary>Topics that matched an issue already here, and refreshed it.</summary>
        public int Updated { get; }

        /// <summary>Comments brought in on top of what was already said.</summary>
        public int Comments { get; }

        /// <summary>Anything worth saying about what came in.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Added == 0 && Updated == 0
                ? "Nothing came in."
                : Added + (Added == 1 ? " issue added" : " issues added") + ", "
                  + Updated + " updated, " + Comments
                  + (Comments == 1 ? " comment" : " comments") + ".";

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Issues in and out as BCF, which is the format the rest of the industry agrees on.
    ///
    /// Import is a merge rather than a load. A reviewer sends the same file back several times over
    /// a fortnight, and each one carries the whole conversation including the half already held. So
    /// a topic that matches an issue already here updates it in place and adds only the comments not
    /// yet recorded; an import that duplicated every topic would make the register unusable by the
    /// second exchange.
    /// </summary>
    public static class IssueBcf
    {
        /// <summary>One issue, as a topic.</summary>
        /// <param name="issue">The issue.</param>
        public static BcfTopic ToTopic(Issue issue)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue));

            var created = issue.CreatedTicks > 0
                ? new DateTimeOffset(issue.CreatedTicks, TimeSpan.Zero)
                : DateTimeOffset.UtcNow;

            var topic = new BcfTopic(issue.BcfGuid ?? BcfGuid.For(issue.Id), issue.Title, created,
                                     issue.Author ?? "CamelWorks")
            {
                TopicType = string.IsNullOrWhiteSpace(issue.Type) ? "Issue" : issue.Type!,
                TopicStatus = FindingStatusLattice.ToName(issue.Status),
                Priority = issue.Priority,
                AssignedTo = issue.AssignedTo,
                Description = issue.Description,
            };

            if (issue.ModifiedTicks > 0)
                topic.ModifiedDate = new DateTimeOffset(issue.ModifiedTicks, TimeSpan.Zero);

            if (issue.DueDayNumber != null)
                topic.DueDate = new DateTimeOffset(Finding.DateOf(issue.DueDayNumber.Value),
                                                   TimeSpan.Zero);

            // The register's own number travels as a label: BCF has no field for it, and a
            // coordinator reading a reply needs to know which of their issues it answers.
            topic.Labels.Add(issue.Id);

            foreach (var label in issue.Labels)
                if (!topic.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    topic.Labels.Add(label);

            if (issue.Facts.TryGetValue(Issue.LevelFact, out var level)
                && !string.IsNullOrWhiteSpace(level))
                topic.Labels.Add(level!);

            var n = 0;

            foreach (var comment in issue.Comments)
            {
                var when = comment.WhenTicks > 0
                    ? new DateTimeOffset(comment.WhenTicks, TimeSpan.Zero)
                    : created;

                topic.Comments.Add(new BcfComment(
                    BcfGuid.For(issue.Id + "/comment/" + (++n)), when, comment.Author, comment.Body));
            }

            return topic;
        }

        /// <summary>One topic, as an issue nobody has yet.</summary>
        /// <param name="topic">The topic.</param>
        /// <param name="id">The id to give it in the register.</param>
        /// <param name="source">Where it came from, for the register's Source column.</param>
        public static Issue FromTopic(BcfTopic topic, string id, string? source = null)
        {
            if (topic == null) throw new ArgumentNullException(nameof(topic));

            var issue = new Issue(id, topic.Title, Issue.ByImport)
            {
                Type = string.IsNullOrWhiteSpace(topic.TopicType) ? "Coordination" : topic.TopicType,
                Status = Status(topic.TopicStatus),
                Priority = topic.Priority,
                Description = topic.Description,
                AssignedTo = topic.AssignedTo,
                Author = topic.CreationAuthor,
                BcfGuid = topic.Guid,
                CreatedTicks = topic.CreationDate.UtcTicks,
                ModifiedTicks = (topic.ModifiedDate ?? topic.CreationDate).UtcTicks,
                DueDayNumber = topic.DueDate == null
                    ? (int?)null
                    : Finding.DayNumberFor(topic.DueDate.Value.UtcDateTime),
            };

            if (!string.IsNullOrWhiteSpace(source)) issue.Facts[Issue.SourceFact] = source;

            foreach (var label in topic.Labels)
                if (!string.IsNullOrWhiteSpace(label)) issue.Labels.Add(label);

            foreach (var comment in topic.Comments)
                issue.Comments.Add(new FindingComment(comment.Author, comment.Date.UtcTicks,
                                                      comment.Text ?? string.Empty));

            Place(issue, topic);

            return issue;
        }

        /// <summary>
        /// Read a file's topics into a register, merging what is already there.
        /// </summary>
        /// <param name="register">The project's register.</param>
        /// <param name="topics">What the reader found.</param>
        /// <param name="source">Where they came from, for the Source column.</param>
        public static IssueImportResult Merge(IssueRegister register, IReadOnlyList<BcfTopic>? topics,
                                              string? source = null)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            var added = 0;
            var updated = 0;
            var comments = 0;
            var notes = new List<string>();

            foreach (var topic in topics ?? Array.Empty<BcfTopic>())
            {
                if (topic == null) continue;

                var existing = register.ByGuid(topic.Guid);

                if (existing == null)
                {
                    register.Put(FromTopic(topic, register.NextId(), source));
                    added++;
                    continue;
                }

                // Their fields win on a re-import: they are the ones who changed the file, and the
                // reason to read it again is to see what they changed.
                existing.Title = topic.Title;
                existing.Status = Status(topic.TopicStatus);
                existing.Type = string.IsNullOrWhiteSpace(topic.TopicType) ? existing.Type : topic.TopicType;
                existing.Description = topic.Description ?? existing.Description;
                existing.AssignedTo = topic.AssignedTo ?? existing.AssignedTo;
                existing.Priority = string.IsNullOrWhiteSpace(topic.Priority) ? existing.Priority : topic.Priority!;
                existing.ModifiedTicks = (topic.ModifiedDate ?? topic.CreationDate).UtcTicks;

                if (topic.DueDate.HasValue)
                    existing.DueDayNumber = Finding.DayNumberFor(topic.DueDate.Value.UtcDateTime);

                foreach (var comment in topic.Comments)
                {
                    var body = comment.Text ?? string.Empty;

                    // Same person, same words: their file carries the whole trail every time, and
                    // a comment repeated on every exchange is the noise that hides the new one.
                    if (existing.Comments.Any(c =>
                            string.Equals(c.Author, comment.Author, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(c.Body, body, StringComparison.Ordinal)))
                        continue;

                    existing.Comments.Add(new FindingComment(comment.Author, comment.Date.UtcTicks, body));
                    comments++;
                }

                if (!existing.HasPoint) Place(existing, topic);

                updated++;
            }

            var unplaced = (topics ?? Array.Empty<BcfTopic>()).Count(t => t != null && t.Viewpoints.Count == 0);

            if (unplaced > 0)
                notes.Add(unplaced + (unplaced == 1 ? " topic carries" : " topics carry")
                          + " no viewpoint, so they have no place in the model — they read in the "
                          + "list, but nothing can be framed from them.");

            return new IssueImportResult(added, updated, comments, notes);
        }

        /// <summary>
        /// Where a topic is, taken from its first viewpoint's camera.
        ///
        /// The camera is where the author stood to look at the problem, which is the only position
        /// a BCF topic actually carries. It is not the problem's centroid and is not claimed to be.
        /// </summary>
        private static void Place(Issue issue, BcfTopic topic)
        {
            var camera = topic.Viewpoints.FirstOrDefault(v => v?.Camera != null)?.Camera;

            if (camera == null) return;

            issue.HasPoint = true;
            issue.X = camera.X;
            issue.Y = camera.Y;
            issue.Z = camera.Z;
        }

        /// <summary>
        /// A topic's status, on this register's lattice.
        ///
        /// Every tool spells these differently — "Closed", "Resolved", "Done" all mean the issue is
        /// over — and a register that kept each sender's spelling would show six statuses that are
        /// two. A word with no rung here is read as Active, which keeps the issue on somebody's
        /// list rather than closing it on a spelling nobody agreed.
        /// </summary>
        private static FindingStatus Status(string? status)
        {
            if (FindingStatusLattice.TryParse(status, out var known)) return known;

            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "closed":
                case "done":
                case "completed":
                    return FindingStatus.Resolved;

                case "approved":
                case "accepted":
                    return FindingStatus.Approved;

                case "answered":
                case "readyforreview":
                case "ready for review":
                case "in review":
                    return FindingStatus.Reviewed;

                default:
                    return FindingStatus.Active;
            }
        }
    }
}
