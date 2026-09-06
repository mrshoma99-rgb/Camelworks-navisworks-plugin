using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// Findings as BCF topics, and what a returned file says about them.
    ///
    /// Writing a finding as a topic is a field mapping. Reading one back is not: a finding's
    /// identity is derived from its source, its rule, its elements and what makes it distinct, and a
    /// markup edited in another tool carries none of that — it carries a topic guid, a status and a
    /// conversation. So the return leg matches on the topic guid, which is a function of the
    /// identity and travels in the file, and lays the edits onto the finding it names.
    ///
    /// A stranger's topic never becomes a finding. Findings are produced by a check; a topic nothing
    /// here answers to is somebody's own issue and goes to the issue register. Inventing a machine
    /// finding for something no machine found would put a row on a board that no re-run can confirm
    /// or clear.
    /// </summary>
    public static class FindingBcf
    {
        /// <summary>The label carrying which check produced a topic, so a reply can be placed.</summary>
        public const string SourceLabel = "Check: ";

        /// <summary>The label carrying the rule, for a reader sorting somebody else's file.</summary>
        public const string RuleLabel = "Rule: ";

        /// <summary>
        /// The topic a finding travels as.
        ///
        /// The one it already has when it has one - a markup must go home under the name it came
        /// out under, or the seat syncing it cannot place it - and otherwise one derived from the
        /// finding's own identity, so the same problem is the same topic in every export.
        ///
        /// This is the half of one agreement that makes the two BCF writers emit the same topic for
        /// the same clash: <c>ClashFindings.From</c> stamps <c>ClashBcf.TopicGuidFor(key)</c> onto
        /// the finding, and preferring the stamped value here is what keeps it. Deriving from the
        /// id unconditionally would give one clash two topic guids depending on which writer
        /// exported it, and a file coming back would match neither.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public static string TopicGuidFor(Finding finding) =>
            finding == null
                ? BcfGuid.For(string.Empty)
                : string.IsNullOrEmpty(finding.TopicGuid)
                    ? BcfGuid.For(finding.Id)
                    : finding.TopicGuid!;

        /// <summary>
        /// One finding, as a topic.
        ///
        /// Also stamps the topic onto the finding when it had none, because the return leg matches
        /// on it: a file that went out under a name the register does not know is a file that
        /// cannot come home.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="author">Who the file says wrote it.</param>
        /// <param name="ifcGuidOf">The IFC GlobalId for an element, so a receiving tool can select it.</param>
        public static BcfTopic ToTopic(Finding finding, string author = "CamelWorks",
                                       Func<ElementKey, string?>? ifcGuidOf = null)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));

            var guid = TopicGuidFor(finding);

            finding.TopicGuid = guid;

            var when = finding.LastSeenTicks > 0
                ? new DateTimeOffset(finding.LastSeenTicks, TimeSpan.Zero)
                : DateTimeOffset.UtcNow;

            var topic = new BcfTopic(guid, finding.Title, when,
                                     string.IsNullOrWhiteSpace(author) ? "CamelWorks" : author)
            {
                TopicType = finding.Source.ToString(),
                TopicStatus = FindingStatusLattice.ToName(finding.Status),
                Priority = finding.Priority,
                Stage = finding.Stage,
                AssignedTo = string.IsNullOrWhiteSpace(finding.AssignedTo) ? null : finding.AssignedTo,
                Description = Describe(finding),
            };

            if (finding.Due != null)
                topic.DueDate = new DateTimeOffset(finding.Due.Value, TimeSpan.Zero);

            topic.Labels.Add(SourceLabel + finding.Source);
            topic.Labels.Add(RuleLabel + finding.Rule);

            if (!string.IsNullOrWhiteSpace(finding.Group)) topic.Labels.Add(finding.Group!);

            foreach (var label in finding.Labels)
                if (!string.IsNullOrWhiteSpace(label)
                    && !topic.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    topic.Labels.Add(label);

            var n = 0;

            foreach (var comment in finding.Comments)
            {
                if (comment == null || string.IsNullOrWhiteSpace(comment.Body)) continue;

                var said = comment.WhenTicks > 0
                    ? new DateTimeOffset(comment.WhenTicks, TimeSpan.Zero)
                    : when;

                topic.Comments.Add(new BcfComment(
                    BcfGuid.For(guid + "/comment/" + (++n)), said, comment.Author, comment.Body));
            }

            var viewpoint = Viewpoint(finding, guid, ifcGuidOf);

            if (viewpoint != null) topic.Viewpoints.Add(viewpoint);

            return topic;
        }

        /// <summary>
        /// What a returned topic says, laid onto the finding it names.
        ///
        /// Only the fields a reviewer could have meant to set. A status is taken through the
        /// lattice, so a stale file cannot undo a decision made since it left. Priority, stage, due
        /// date, tags and the assignee are latest-wins, because that is what setting one means. The
        /// title is taken, because a reviewer who renamed it meant something by it. The rule, the
        /// source and the elements are not, because they are what the finding is.
        ///
        /// The status word is read through <see cref="ClashStatusMap"/>, which is the one mapping
        /// for both readers. This used to be a bare name parse, so the same pack read here and
        /// through <c>ClashBcf</c> gave two different statuses for every word outside the five.
        /// </summary>
        /// <param name="topic">The topic as it came back.</param>
        /// <param name="finding">The finding it names.</param>
        /// <param name="source">Which file it arrived in.</param>
        /// <param name="guessStatus">
        /// Whether a status word the conventional vocabulary does not hold may be read for its
        /// sense. False leaves the status alone instead, for a caller that must not guess.
        /// </param>
        /// <returns>Whether anything on the finding changed.</returns>
        public static bool Apply(BcfTopic? topic, Finding? finding, string? source = null,
                                 bool guessStatus = true)
        {
            if (topic == null || finding == null) return false;

            var before = Snapshot(finding);

            // A topic that set no status word says nothing about the status, which is not the
            // same as saying New: the map's Unsaid tier answers for a fresh import, and applying
            // it here would be a file with a field left blank overwriting a decision.
            if (!string.IsNullOrWhiteSpace(topic.TopicStatus)
                && (guessStatus || ClashStatusMap.IsConventional(topic.TopicStatus)))
                finding.Status = FindingStatusLattice.Merge(
                    finding.Status, ClashStatusMap.Read(topic.TopicStatus));

            if (!string.IsNullOrWhiteSpace(topic.Title)) finding.Title = topic.Title;
            if (!string.IsNullOrWhiteSpace(topic.Priority)) finding.Priority = topic.Priority;
            if (!string.IsNullOrWhiteSpace(topic.Stage)) finding.Stage = topic.Stage;

            if (!string.IsNullOrWhiteSpace(topic.AssignedTo)) finding.AssignedTo = topic.AssignedTo;

            if (topic.DueDate != null)
                finding.DueDayNumber = Finding.DayNumberFor(topic.DueDate.Value.UtcDateTime);

            // Their own tags only. The two this writer puts on say which check spoke and which
            // rule, and reading them back as somebody's labels would grow a label a cycle.
            foreach (var label in topic.Labels)
            {
                if (string.IsNullOrWhiteSpace(label) || Ours(label)) continue;

                if (!finding.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    finding.Labels.Add(label);
            }

            foreach (var comment in topic.Comments)
            {
                if (comment == null || string.IsNullOrWhiteSpace(comment.Text)) continue;

                var said = new FindingComment(comment.Author, comment.Date.UtcTicks, comment.Text!);

                if (finding.Comments.Any(c => c.Repeats(said))) continue;

                finding.Comments.Add(said);
            }

            if (!string.IsNullOrWhiteSpace(source)) finding.Origin = source;

            return Snapshot(finding) != before;
        }

        /// <summary>Whether a label is one this writer put on rather than one a reviewer set.</summary>
        /// <param name="label">The label.</param>
        public static bool Ours(string? label) =>
            label != null
            && (label.StartsWith(SourceLabel, StringComparison.OrdinalIgnoreCase)
                || label.StartsWith(RuleLabel, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Take a returned file into the register: what it answers, and what it does not.
        /// </summary>
        /// <param name="register">The project's findings.</param>
        /// <param name="topics">What the reader found.</param>
        /// <param name="source">Which file they came in.</param>
        /// <returns>
        /// How many findings the file changed, and the topics nothing here answers to - which the
        /// caller raises as issues, because a finding is only ever something a check produced.
        /// </returns>
        public static (int Answered, IReadOnlyList<BcfTopic> Strangers) Merge(
            FindingRegister? register, IReadOnlyList<BcfTopic>? topics, string? source = null)
        {
            var strangers = new List<BcfTopic>();

            if (register == null || topics == null) return (0, strangers);

            var byTopic = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);

            foreach (var finding in register.All)
            {
                var guid = string.IsNullOrEmpty(finding.TopicGuid)
                    ? BcfGuid.For(finding.Id)
                    : finding.TopicGuid!;

                byTopic[guid] = finding;
            }

            var answered = 0;

            foreach (var topic in topics)
            {
                if (topic == null) continue;

                if (!byTopic.TryGetValue(topic.Guid, out var finding))
                {
                    strangers.Add(topic);
                    continue;
                }

                if (Apply(topic, finding, source)) answered++;
            }

            return (answered, strangers);
        }

        /// <summary>
        /// The topics for a set of findings and the issues that carry them, related both ways.
        ///
        /// Both go in one file. An issue is the conversation and a finding is what it is about, and
        /// sending only one of them gives half the record: the issues without the geometry they
        /// concern, or eleven clashes with no sign that they are one argument with one person. BCF
        /// has a relation for this and every tool that reads BCF understands it.
        /// </summary>
        /// <param name="findings">The findings to send.</param>
        /// <param name="issues">Their issues, as topics - already written by the issue writer.</param>
        /// <param name="attached">Which finding ids each issue topic carries.</param>
        /// <param name="author">Who the file says wrote it.</param>
        /// <param name="ifcGuidOf">The IFC GlobalId for an element.</param>
        public static IReadOnlyList<BcfTopic> Together(
            IEnumerable<Finding>? findings,
            IEnumerable<BcfTopic>? issues,
            Func<BcfTopic, IEnumerable<string>>? attached,
            string author = "CamelWorks",
            Func<ElementKey, string?>? ifcGuidOf = null)
        {
            var byFinding = new Dictionary<string, BcfTopic>(StringComparer.Ordinal);
            var all = new List<BcfTopic>();

            foreach (var finding in findings ?? Array.Empty<Finding>())
            {
                if (finding == null || byFinding.ContainsKey(finding.Id)) continue;

                var topic = ToTopic(finding, author, ifcGuidOf);

                byFinding[finding.Id] = topic;
                all.Add(topic);
            }

            foreach (var issue in issues ?? Array.Empty<BcfTopic>())
            {
                if (issue == null) continue;

                all.Add(issue);

                if (attached == null) continue;

                foreach (var id in attached(issue))
                {
                    if (id == null || !byFinding.TryGetValue(id, out var topic)) continue;

                    // Both ways round. A reader that opened the clash first should be able to get
                    // to the conversation, not only the other way about.
                    if (!issue.RelatedTopics.Contains(topic.Guid, StringComparer.OrdinalIgnoreCase))
                        issue.RelatedTopics.Add(topic.Guid);

                    if (!topic.RelatedTopics.Contains(issue.Guid, StringComparer.OrdinalIgnoreCase))
                        topic.RelatedTopics.Add(issue.Guid);
                }
            }

            return all;
        }

        /// <summary>
        /// The facts a topic does not carry: what the seam writes for itself.
        ///
        /// A topic is read by a trade contractor in somebody else's tool. The host's id for a
        /// result, the runs a finding belongs to, and the host's own remark count and last remark
        /// are handles this product keeps so a read can find its way back to a result; none of
        /// them means anything in a viewer, and the two comment facts would put a stale copy of a
        /// remark beside the remarks the topic already carries.
        ///
        /// Named here rather than flagged on the fact, because the exclusion is about what a BCF
        /// topic is for and not about the producer that wrote the fact.
        /// The names are literals rather than the producers' constants, because this file sits
        /// below both producers. A test pins them against those constants, so a rename cannot let
        /// one back in unnoticed.
        /// </summary>
        private static readonly HashSet<string> Bookkeeping =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Host id", "Host test", "Host comments", "Host last comment", "Runs",
            };

        /// <summary>What the topic says about the finding, for a reader with no board.</summary>
        /// <param name="finding">The finding.</param>
        public static string Describe(Finding finding)
        {
            if (finding == null) return string.Empty;

            var said = new List<string>();

            if (!string.IsNullOrWhiteSpace(finding.Detail)) said.Add(finding.Detail!);

            said.Add("Found by " + finding.Rule + " (" + finding.Source + ").");

            foreach (var fact in finding.Facts.OrderBy(f => f.Key, StringComparer.Ordinal))
                if (!string.IsNullOrWhiteSpace(fact.Value) && !Bookkeeping.Contains(fact.Key))
                    said.Add(fact.Key + ": " + fact.Value);

            if (finding.HasLocations)
                said.Add(finding.Locations.Count.ToString(CultureInfo.InvariantCulture)
                         + (finding.Locations.Count == 1 ? " place is" : " places are")
                         + " named on it.");

            if (!finding.IsPresent)
                said.Add("The latest run no longer reports this.");

            return string.Join(Environment.NewLine, said);
        }

        /// <summary>
        /// A viewpoint naming the elements, so a receiving tool can select them.
        ///
        /// Null when nothing on the finding resolves to an IFC GlobalId: a viewpoint listing
        /// components nothing can find is a button that does nothing in somebody else's tool,
        /// which is worse than no button.
        /// </summary>
        private static BcfViewpoint? Viewpoint(Finding finding, string guid,
                                               Func<ElementKey, string?>? ifcGuidOf)
        {
            var viewpoint = new BcfViewpoint(BcfGuid.For(guid + "/viewpoint"));

            foreach (var element in finding.AllElements())
            {
                // The GlobalId read off the element wins: it is the id the receiving model
                // actually uses. Failing that, the key's own value is tried, which is a GlobalId
                // exactly when the federation was built from IFC and the key was made from one.
                var ifc = IfcGuid.From(ifcGuidOf == null ? null : ifcGuidOf(element))
                          ?? IfcGuid.OrNull(element.UnscopedValue);

                if (ifc == null) continue;

                viewpoint.Selection.Add(new BcfComponent(ifc, element.ToString(), "CamelWorks"));
            }

            return viewpoint.Selection.Count == 0 ? null : viewpoint;
        }

        /// <summary>
        /// Everything a returned file is allowed to change, in one string.
        ///
        /// Compared before and after rather than tracked field by field, because "did this file
        /// say anything" is the only question the caller asks, and a per-field answer would be
        /// six more things to keep in step with the fields.
        /// </summary>
        private static string Snapshot(Finding finding) =>
            string.Join("|",
                FindingStatusLattice.ToName(finding.Status), finding.Title, finding.Priority,
                finding.Stage, finding.AssignedTo,
                finding.DueDayNumber?.ToString(CultureInfo.InvariantCulture),
                string.Join(",", finding.Labels),
                finding.Comments.Count.ToString(CultureInfo.InvariantCulture));
    }
}
