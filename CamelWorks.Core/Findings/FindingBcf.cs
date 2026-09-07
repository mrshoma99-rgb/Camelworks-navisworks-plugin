using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// The one BCF writer: findings and their issues out, and what a returned file says about them.
    ///
    /// There were three writers over one format — this one, a clash writer and an issue writer —
    /// with two label vocabularies, two sidecars and two readings of a status word. A file written
    /// by one and read by another lost the group, grew six tags a cycle and could disagree with
    /// itself about whether a topic was approved. Exchange is offered on every check, so there is
    /// one writer for every check.
    ///
    /// A finding's identity is derived from its source, its rule, its elements and what makes it
    /// distinct. A markup edited in another tool carries none of that: it carries a topic guid, a
    /// status and a conversation. So the return leg matches on the topic guid, which is a function
    /// of the identity and travels in the file, and lays the edits onto the finding it names.
    ///
    /// A stranger's topic never becomes a finding. Findings are produced by a check; a topic
    /// nothing here answers to is somebody's own issue and goes to the issue register. Inventing a
    /// machine finding for something no machine found would put a row on a board that no re-run can
    /// confirm or clear.
    ///
    /// What BCF has no field for rides in <see cref="Sidecar"/>, inside the same archive: the
    /// finding's identity, its point in metres and its facts, whatever check wrote them. The BCF
    /// specification requires a reader to ignore entries it does not recognise, so every other tool
    /// reads the topics and never sees the sidecar, and we read both. On the way back the topics
    /// win for everything BCF carries — status, assignee, comments — because that is what the other
    /// tool edited and the reason for sending it.
    /// </summary>
    public static class FindingBcf
    {
        /// <summary>
        /// Where the facts BCF has no field for ride, inside the archive.
        ///
        /// A file at the root, deliberately not a folder. A .bcfzip's top-level directories are its
        /// topics — importers enumerate them and read a <c>markup.bcf</c> out of each — so an extra
        /// directory that is not a topic is a shape some importer will meet for the first time in
        /// our file, and a folder name that will not parse as a GUID is likely to throw rather than
        /// be skipped.
        ///
        /// A root-level file is a shape every importer already handles, because the format puts
        /// three of them there: <c>bcf.version</c>, <c>project.bcfp</c> and <c>extensions.xsd</c>.
        /// This sits beside those.
        /// </summary>
        public const string Sidecar = "camelworks-findings.json";

        /// <summary>What the sidecar's shape is called, so a future change can be told apart.</summary>
        public const int SidecarVersion = 1;

        /// <summary>
        /// The topic a finding travels as.
        ///
        /// The one it already has when it has one - a markup must go home under the name it came
        /// out under, or the seat syncing it cannot place it - and otherwise one derived from the
        /// finding's own identity, so the same problem is the same topic in every export.
        ///
        /// This is the half of one agreement that keeps a clash's topic stable:
        /// <c>ClashFindings.From</c> stamps <c>ClashBcf.TopicGuidFor(key)</c> onto the finding, and
        /// preferring the stamped value here is what keeps it. Deriving from the id
        /// unconditionally would give one clash two topic guids depending on which seat exported
        /// it, and a file coming back would match neither.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public static string TopicGuidFor(Finding finding) =>
            finding == null
                ? BcfGuid.For(string.Empty)
                : string.IsNullOrEmpty(finding.TopicGuid)
                    ? BcfGuid.For(finding.Id)
                    : finding.TopicGuid!;

        /// <summary>
        /// A topic guid for the second finding to claim one, derived from the guid it collided
        /// with and its own id.
        ///
        /// A clash's topic guid is a function of the element pair and the cell they meet in and
        /// not of the test, so two tests reporting one physical conflict are two findings with one
        /// guid. Both have to travel: BCF identifies a topic by its guid and names the archive
        /// folder with it, so two topics at one guid is a malformed archive whose second topic
        /// most readers drop.
        ///
        /// The test is deliberately not folded into <see cref="ClashBcf.TopicGuidFor"/> instead.
        /// A file carries no host test id, so the file leg and the document leg name the same test
        /// differently; the topic guid being independent of the test is what lets a markup read on
        /// a file seat fold back onto the finding the document leg holds.
        /// </summary>
        /// <param name="guid">The guid already claimed.</param>
        /// <param name="finding">The finding that could not have it.</param>
        public static string Apart(string guid, Finding finding) =>
            BcfGuid.For(guid + "/" + (finding?.Id ?? string.Empty));

        /// <summary>
        /// One topic guid per finding, claimed once.
        ///
        /// Ordered by finding id so the same set answers the same way on every seat and in every
        /// run: without it the winner is whatever order the caller happened to hold them in. The
        /// second claimant is written under <see cref="Apart"/> and that guid is stamped on it, so
        /// it keeps that identity in later packs whether or not the finding it collided with is in
        /// them.
        /// </summary>
        /// <param name="findings">The findings going out.</param>
        /// <returns>The findings that had to be moved off their derived guid.</returns>
        private static IReadOnlyList<Finding> Claimed(IReadOnlyList<Finding> findings)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var moved = new List<Finding>();

            foreach (var finding in findings.OrderBy(one => one.Id, StringComparer.Ordinal))
            {
                var guid = TopicGuidFor(finding);

                if (taken.Add(guid)) continue;

                var apart = Apart(guid, finding);

                if (!taken.Add(apart)) continue;

                finding.TopicGuid = apart;
                moved.Add(finding);
            }

            return moved;
        }

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
                                       Func<ElementKey, string?>? ifcGuidOf = null) =>
            ToTopic(finding, new FindingBcfOptions
            {
                Author = string.IsNullOrWhiteSpace(author) ? "CamelWorks" : author,
                IfcGuidOf = ifcGuidOf,
            });

        /// <summary>
        /// One finding, as a topic, written the way the pack is written.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="how">How the pack is being written.</param>
        public static BcfTopic ToTopic(Finding finding, FindingBcfOptions? how)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));

            var options = how ?? new FindingBcfOptions();
            var guid = TopicGuidFor(finding);

            finding.TopicGuid = guid;

            var when = finding.LastSeenTicks > 0
                ? new DateTimeOffset(finding.LastSeenTicks, TimeSpan.Zero)
                : options.WhenTicks > 0
                    ? new DateTimeOffset(options.WhenTicks, TimeSpan.Zero)
                    : DateTimeOffset.UtcNow;

            var topic = new BcfTopic(guid, finding.Title, when,
                                     string.IsNullOrWhiteSpace(options.Author)
                                         ? "CamelWorks" : options.Author)
            {
                TopicType = finding.Source.ToString(),
                TopicStatus = FindingStatusLattice.ToName(finding.Status),
                Priority = finding.Priority,
                Stage = finding.Stage,
                AssignedTo = string.IsNullOrWhiteSpace(finding.AssignedTo) ? null : finding.AssignedTo,
                Description = Describe(finding, Facts(finding, options)),
            };

            if (finding.Due != null)
                topic.DueDate = new DateTimeOffset(finding.Due.Value, TimeSpan.Zero);

            Label(topic, FindingLabels.Check, finding.Source);
            Label(topic, FindingLabels.Rule, finding.Rule);

            if (options.KeepGroups)
                Label(topic, FindingLabels.Group,
                      (options.GroupOf == null ? null : options.GroupOf(finding)) ?? finding.Group);

            // The facts that travel as tags, by the declared mapping and by nothing else. A
            // prefix invented here rather than declared is one the reader cannot take off again.
            var facts = Facts(finding, options);

            foreach (var pair in FindingLabels.FromFacts)
                if (facts.TryGetValue(pair.Key, out var value))
                    Label(topic, pair.Value, value);

            // The reviewer's tags only. Anything in this product's own vocabulary is written above
            // from the finding's own fields, and copying a second one out of the label list would
            // put two values under one prefix on the topic.
            foreach (var label in finding.Labels)
                if (!string.IsNullOrWhiteSpace(label)
                    && !FindingLabels.Ours(label)
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

            var viewpoint = Viewpoint(finding, guid, options);

            if (viewpoint != null)
            {
                if (options.Snapshot != null)
                {
                    try
                    {
                        viewpoint.Snapshot = options.Snapshot(finding);
                    }
                    catch (Exception)
                    {
                        // A picture that would not render is a topic without one, never a failed
                        // export. The count of what was taken is reported either way.
                    }
                }

                topic.Viewpoints.Add(viewpoint);
            }

            return topic;
        }

        /// <summary>
        /// Write findings, and the issues that carry them, as a .bcfzip.
        /// </summary>
        /// <param name="findings">The findings to send.</param>
        /// <param name="issues">Their issues, related both ways to the findings they name.</param>
        /// <param name="options">How to write it.</param>
        public static FindingBcfDocument Write(IEnumerable<Finding>? findings,
                                               IEnumerable<Issue>? issues = null,
                                               FindingBcfOptions? options = null)
        {
            var how = options ?? new FindingBcfOptions();
            var warnings = new List<string>();

            var all = (findings ?? Array.Empty<Finding>()).Where(one => one != null).ToList();
            var raised = (issues ?? Array.Empty<Issue>()).Where(one => one != null).ToList();

            var when = how.WhenTicks > 0
                ? new DateTimeOffset(how.WhenTicks, TimeSpan.Zero)
                : DateTimeOffset.UtcNow;

            // Before the first topic is built, because two findings on one guid would otherwise
            // write two entries at one path in the archive. Findings are written in the order the
            // caller gave them; only the guids are decided here.
            var moved = Claimed(all);

            if (moved.Count > 0)
                warnings.Add(Measure.Count(moved.Count)
                    + (moved.Count == 1 ? " finding shares" : " findings share")
                    + " a topic with another in this pack - two checks reporting one physical "
                    + "problem - so " + (moved.Count == 1 ? "it was" : "they were")
                    + " written under a topic of "
                    + (moved.Count == 1 ? "its" : "their") + " own. BCF names a topic's folder "
                    + "with its guid, so two topics at one guid is a file the receiving tool "
                    + "reads only half of.");

            var byFinding = new Dictionary<string, BcfTopic>(StringComparer.Ordinal);
            var topics = new List<BcfTopic>();
            var records = new List<JsonValue>();

            // How many topics the receiving tool will be able to point at something with. See
            // FindingBcfDocument.Blind: this is the number that decides whether the file is usable
            // in Revizto or Solibri, and it is not visible from anything else in the readout.
            var blind = 0;
            var partial = 0;

            foreach (var finding in all)
            {
                if (byFinding.ContainsKey(finding.Id)) continue;

                var topic = ToTopic(finding, how);

                byFinding[finding.Id] = topic;
                topics.Add(topic);
                records.Add(Record(finding, topic.Guid, how));

                var elements = finding.AllElements().Count;

                if (elements == 0) continue;

                var found = topic.Viewpoints
                                 .SelectMany(view => view.Selection)
                                 .Count(one => one != null && one.IsResolvable);

                if (found == 0) blind++;
                else if (found < elements) partial++;
            }

            foreach (var issue in raised)
            {
                var topic = ToTopic(issue);

                topics.Add(topic);

                foreach (var id in issue.Findings)
                    if (id != null && byFinding.TryGetValue(id, out var about))
                        Relate(topic, about);
            }

            // The one thing a coordinator cannot see by opening the file they just wrote, and the
            // thing they get told about by the person they sent it to. An IFC GlobalId is the only
            // id another tool can look up; without one the topic still carries a camera and a
            // picture, so it is worth sending - it just will not select anything.
            if (blind > 0 || partial > 0)
                warnings.Add(Reach(blind, partial, all.Count));

            var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Sidecar] = Sheet(records, how, when).ToJson(),
            };

            byte[] bytes;
            BcfWriteResult written;

            using (var buffer = new MemoryStream())
            {
                var project = how.ProjectName == null && how.SourceFile == null
                    ? null
                    : new BcfProject(how.SourceFile ?? how.ProjectName!, how.ProjectName);

                written = BcfWriter.Write(buffer, topics, how.Version, project, extras);
                bytes = buffer.ToArray();
            }

            foreach (var note in written.Notes) warnings.Add(note);

            return new FindingBcfDocument(bytes, byFinding.Count, raised.Count, written.Snapshots,
                                          blind, partial, warnings);
        }

        /// <summary>
        /// Read a pack back, whoever wrote it.
        /// </summary>
        /// <param name="input">The archive. Left open.</param>
        public static FindingPackRead Read(Stream? input)
        {
            if (input == null) return Refuse("There is nothing to read.");

            BcfReadResult read;

            try
            {
                read = BcfReader.Read(input);
            }
            catch (Exception error)
            {
                return Refuse("That file could not be read as BCF: " + error.Message);
            }

            var warnings = new List<string>(read.Warnings);

            if (read.Topics.Count == 0)
                return Refuse("That BCF holds no topics, so there is nothing to mark up.");

            var records = Sidecars(read, warnings);
            var strangers = read.Topics.Count(topic => topic != null
                                                       && !records.ContainsKey(topic.Guid));

            if (records.Count == 0)
                warnings.Add("This BCF carries no CamelWorks detail, so it has been through "
                    + "another tool - or was written by one. Statuses, assignments and comments "
                    + "all read normally; the measured numbers were not in the file to read. "
                    + "Topics still land back on their findings where the topic kept its guid.");
            else if (strangers > 0)
                warnings.Add(Measure.Count(strangers)
                    + (strangers == 1 ? " topic was" : " topics were")
                    + " added after this file left here - by a reviewer's own tool, most likely. "
                    + (strangers == 1 ? "It is" : "They are") + " read as far as BCF describes "
                    + (strangers == 1 ? "it." : "them."));

            return new FindingPackRead(read.Version, read.Topics, records, warnings, null);
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
        /// for every path in and out. This used to be a bare name parse, so the same pack read here
        /// and through the clash reader gave two different statuses for every word outside the
        /// five.
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

            // Their own tags only. Everything this writer puts on carries a declared prefix, and
            // reading one back as somebody's label would grow the tag list a cycle.
            foreach (var label in topic.Labels)
            {
                if (string.IsNullOrWhiteSpace(label) || FindingLabels.Ours(label)) continue;

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

            // The index the answers are claimed from, built under the rule the writer wrote by.
            // A topic guid is not unique across findings - two clash tests reporting one conflict
            // derive the same one - so the second claimant is indexed under Apart, which is the
            // guid it went out under, and the order is the writer's so both seats agree which of
            // the two that is.
            foreach (var finding in register.All.OrderBy(one => one.Id, StringComparer.Ordinal))
            {
                var guid = TopicGuidFor(finding);

                if (byTopic.ContainsKey(guid)) guid = Apart(guid, finding);

                if (!byTopic.ContainsKey(guid)) byTopic[guid] = finding;
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
            var going = (findings ?? Array.Empty<Finding>()).Where(one => one != null).ToList();

            // See Write: one topic guid per finding, or the archive carries two entries at one
            // path.
            Claimed(going);

            foreach (var finding in going)
            {
                if (byFinding.ContainsKey(finding.Id)) continue;

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
                    if (id != null && byFinding.TryGetValue(id, out var topic))
                        Relate(issue, topic);
            }

            return all;
        }

        // -------------------------------------------------------------------------------------
        // Issues, on the same writer
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// One issue, as a topic.
        ///
        /// The issue register's own status is on the finding lattice, so an issue is a topic in
        /// the same way a finding is: the same status names out, the same status reading back, the
        /// same label vocabulary. It kept a writer of its own only while the two statuses were
        /// different types.
        /// </summary>
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
            // coordinator reading a reply needs to know which of their issues it answers. Under
            // the vocabulary's prefix, so the return leg takes it off again instead of keeping it
            // as a tag somebody set.
            Label(topic, FindingLabels.Issue, issue.Id);

            if (issue.Facts.TryGetValue(Issue.LevelFact, out var level))
                Label(topic, FindingLabels.Level, level);

            if (issue.Facts.TryGetValue(Issue.ZoneFact, out var zone))
                Label(topic, FindingLabels.Zone, zone);

            // See ToTopic(Finding): the vocabulary is written from the issue's own fields.
            foreach (var label in issue.Labels)
                if (!string.IsNullOrWhiteSpace(label)
                    && !FindingLabels.Ours(label)
                    && !topic.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    topic.Labels.Add(label);

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
        /// <param name="guessStatus">
        /// Whether a status word the conventional vocabulary does not hold may be read for its
        /// sense. False leaves the issue at the map's Unsaid tier instead, for a caller that must
        /// not guess. The same choice <see cref="Apply"/> offers on the finding leg, so one pack
        /// read by both legs answers the same way.
        /// </param>
        public static Issue ToIssue(BcfTopic topic, string id, string? source = null,
                                    bool guessStatus = true)
        {
            if (topic == null) throw new ArgumentNullException(nameof(topic));

            var issue = new Issue(id, topic.Title, Issue.ByImport)
            {
                Type = string.IsNullOrWhiteSpace(topic.TopicType) ? "Coordination" : topic.TopicType,
                Status = Status(topic.TopicStatus, guessStatus),

                // Left blank when the topic gave none, rather than defaulted to "Normal". The
                // priority vocabulary is the project's, and a project that has overridden the list
                // may not have that word in it; an import that invents one would be the importer
                // choosing a word the project does not use.
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

            Located(issue, topic);

            foreach (var label in topic.Labels)
                if (!string.IsNullOrWhiteSpace(label) && !FindingLabels.Ours(label))
                    issue.Labels.Add(label);

            foreach (var comment in topic.Comments)
                issue.Comments.Add(new FindingComment(comment.Author, comment.Date.UtcTicks,
                                                      comment.Text ?? string.Empty));

            Place(issue, topic);

            return issue;
        }

        /// <summary>
        /// Read a file's topics into an issue register, merging what is already there.
        ///
        /// Import is a merge rather than a load. A reviewer sends the same file back several times
        /// over a fortnight, and each one carries the whole conversation including the half already
        /// held. So a topic that matches an issue already here updates it in place and adds only
        /// the comments not yet recorded; an import that duplicated every topic would make the
        /// register unusable by the second exchange.
        /// </summary>
        /// <param name="register">The project's register.</param>
        /// <param name="topics">What the reader found.</param>
        /// <param name="source">Where they came from, for the Source column.</param>
        /// <param name="guessStatus">
        /// Whether a status word the conventional vocabulary does not hold may be read for its
        /// sense. Either way the words that were outside the vocabulary are named in the result's
        /// notes.
        /// </param>
        public static IssueImportResult MergeIssues(IssueRegister register,
                                                    IReadOnlyList<BcfTopic>? topics,
                                                    string? source = null,
                                                    bool guessStatus = true)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            var added = 0;
            var updated = 0;
            var comments = 0;
            var notes = new List<string>();
            var strange = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var topic in topics ?? Array.Empty<BcfTopic>())
            {
                if (topic == null) continue;

                if (!string.IsNullOrWhiteSpace(topic.TopicStatus)
                    && !ClashStatusMap.IsConventional(topic.TopicStatus))
                    strange.Add(topic.TopicStatus!.Trim());

                var existing = register.ByGuid(topic.Guid);

                if (existing == null)
                {
                    register.Put(ToIssue(topic, register.NextId(), source, guessStatus));
                    added++;
                    continue;
                }

                // Their fields win on a re-import: they are the ones who changed the file, and the
                // reason to read it again is to see what they changed. Two exceptions, both of
                // them a field the file left blank overwriting something somebody set:
                //
                //   * A topic that set no status word says nothing about the status, and a word it
                //     did set goes through the lattice. A month-old pack saying Open would
                //     otherwise put an Approved issue back to Active, and IssueCascade pushes that
                //     down onto every finding attached to it. This is the reading Apply already
                //     uses on the finding leg.
                //   * An empty title is not a retitle.
                if (!string.IsNullOrWhiteSpace(topic.Title)) existing.Title = topic.Title;

                if (!string.IsNullOrWhiteSpace(topic.TopicStatus)
                    && (guessStatus || ClashStatusMap.IsConventional(topic.TopicStatus)))
                    existing.Status = FindingStatusLattice.Merge(
                        existing.Status, ClashStatusMap.Read(topic.TopicStatus));

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

                // Their tags only, added rather than replaced: a re-import carries the whole tag
                // list every time, and the register's own vocabulary is written from its fields.
                foreach (var label in topic.Labels)
                    if (!string.IsNullOrWhiteSpace(label)
                        && !FindingLabels.Ours(label)
                        && !existing.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                        existing.Labels.Add(label);

                Located(existing, topic);

                if (!existing.HasPoint) Place(existing, topic);

                updated++;
            }

            var unplaced = (topics ?? Array.Empty<BcfTopic>())
                .Count(t => t != null && t.Viewpoints.Count == 0);

            if (unplaced > 0)
                notes.Add(unplaced + (unplaced == 1 ? " topic carries" : " topics carry")
                          + " no viewpoint, so they have no place in the model — they read in the "
                          + "list, but nothing can be framed from them.");

            // Named rather than mapped in silence: these are the words this product's vocabulary
            // does not hold, and what happened to them depends on guessStatus.
            if (strange.Count > 0)
                notes.Add((strange.Count == 1 ? "One status word is" : "Status words are")
                          + " outside the conventional vocabulary: " + string.Join(", ", strange)
                          + ". " + (guessStatus
                              ? "Each was read for its sense."
                              : "Each was left at the status the register already held."));

            return new IssueImportResult(added, updated, comments, notes);
        }

        /// <summary>A status word, read the one way, with or without the guess tier.</summary>
        /// <param name="word">What the topic said.</param>
        /// <param name="guess">Whether a word outside the vocabulary may be read for its sense.</param>
        private static FindingStatus Status(string? word, bool guess) =>
            guess || ClashStatusMap.IsConventional(word)
                ? ClashStatusMap.Read(word)
                : ClashStatusMap.Unknown;

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

        /// <summary>The level and zone the vocabulary carried, back onto the issue's facts.</summary>
        private static void Located(Issue issue, BcfTopic topic)
        {
            var level = FindingLabels.Value(topic, FindingLabels.Level);
            var zone = FindingLabels.Value(topic, FindingLabels.Zone);

            if (level != null) issue.Facts[Issue.LevelFact] = level;
            if (zone != null) issue.Facts[Issue.ZoneFact] = zone;
        }

        // -------------------------------------------------------------------------------------
        // The topic's prose, its viewpoint and its sidecar row
        // -------------------------------------------------------------------------------------

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
        /// topic is for and not about the producer that wrote the fact. This is the description
        /// only: the sidecar carries every fact, because that is what round-trips a check's own
        /// numbers.
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
        public static string Describe(Finding finding) =>
            finding == null ? string.Empty : Describe(finding, finding.Facts);

        /// <summary>What the topic says about the finding, over the facts this pack is writing.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="facts">The finding's facts, and whatever the pack adds to them.</param>
        private static string Describe(Finding finding, IEnumerable<KeyValuePair<string, string?>> facts)
        {
            if (finding == null) return string.Empty;

            var said = new List<string>();

            if (!string.IsNullOrWhiteSpace(finding.Detail)) said.Add(finding.Detail!);

            said.Add("Found by " + finding.Rule + " (" + finding.Source + ").");

            foreach (var fact in facts.OrderBy(f => f.Key, StringComparer.Ordinal))
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
        /// One viewpoint: the finding's elements named, and a camera on its point.
        ///
        /// The elements are what makes a topic actionable in the receiving tool: a camera position
        /// on its own puts the reviewer in roughly the right place, and a selection puts the thing
        /// the topic is about on their screen highlighted. Both are written, because a reviewer
        /// whose model has no IFC GlobalIds still gets the camera.
        ///
        /// Null when there is neither: a viewpoint listing components nothing can find, from a
        /// camera pointed at the origin, is a button that does nothing in somebody else's tool.
        /// </summary>
        private static BcfViewpoint? Viewpoint(Finding finding, string guid, FindingBcfOptions how)
        {
            var view = new BcfViewpoint(BcfGuid.For(guid + "/viewpoint"));

            var elements = finding.AllElements();

            foreach (var element in elements)
            {
                // The GlobalId read off the element wins: it is the id the receiving model
                // actually uses. Failing that, the key's own value is tried, which is a GlobalId
                // exactly when the federation was built from IFC and the key was made from one.
                var ifc = IfcGuid.From(how.IfcGuidOf == null ? null : how.IfcGuidOf(element))
                          ?? IfcGuid.OrNull(element.UnscopedValue);

                view.Selection.Add(new BcfComponent(ifc, element.ToString(), "CamelWorks"));
            }

            var placed = finding.X != null && finding.Y != null && finding.Z != null;

            if (!placed) return view.Selection.Count == 0 ? null : view;

            var point = (X: finding.X!.Value, Y: finding.Y!.Value, Z: finding.Z!.Value);

            view.Camera = BcfFraming.Around(new[] { point },
                                            BcfFraming.DefaultFieldOfView, how.View).ToCamera();

            if (how.Highlight && view.Selection.Count > 0)
            {
                // Red for the first element and amber for the rest: on a clash that is the two
                // sides of the pair, and on a check with one element it is simply the element.
                view.Colouring.Add(new BcfColouring("FF3B30", new[] { view.Selection[0] }));

                if (view.Selection.Count > 1)
                    view.Colouring.Add(new BcfColouring("FF9F0A",
                                                        view.Selection.Skip(1).ToList()));
            }

            if (how.Section && how.SectionMetres > 0)
            {
                // BCF has no box of its own; a box is what six planes make. Each plane sits at the
                // stated reach from the point with its normal pointing away from that point,
                // because a plane's normal names the side that is cut away - the convention the
                // repo's own single-plane fixture uses. Pointed the other way, all six cut towards
                // the finding and the topic opens showing everything except the finding.
                var reach = how.SectionMetres;

                view.ClippingPlanes.Add(new BcfClippingPlane(point.X - reach, point.Y, point.Z, -1, 0, 0));
                view.ClippingPlanes.Add(new BcfClippingPlane(point.X + reach, point.Y, point.Z, 1, 0, 0));
                view.ClippingPlanes.Add(new BcfClippingPlane(point.X, point.Y - reach, point.Z, 0, -1, 0));
                view.ClippingPlanes.Add(new BcfClippingPlane(point.X, point.Y + reach, point.Z, 0, 1, 0));
                view.ClippingPlanes.Add(new BcfClippingPlane(point.X, point.Y, point.Z - reach, 0, 0, -1));
                view.ClippingPlanes.Add(new BcfClippingPlane(point.X, point.Y, point.Z + reach, 0, 0, 1));
            }

            return view;
        }

        /// <summary>
        /// One finding's row in the sidecar - its identity, its point and all of its facts.
        ///
        /// Every fact, not a chosen few. A check's own numbers are the thing a generic sidecar
        /// exists to round-trip: the clash writer's sheet named twenty-five clash fields, so a
        /// clearance breach travelled with none of its measurements and came home with none.
        /// </summary>
        private static JsonValue Record(Finding finding, string topic, FindingBcfOptions how)
        {
            var json = JsonValue.Object()
                .Set("topic", topic)
                .Set("id", finding.Id)
                .Set("source", finding.Source)
                .Set("rule", finding.Rule);

            if (!string.IsNullOrEmpty(finding.Discriminator))
                json.Set("cell", finding.Discriminator);

            if (finding.X != null) json.Set("x", finding.X.Value);
            if (finding.Y != null) json.Set("y", finding.Y.Value);
            if (finding.Z != null) json.Set("z", finding.Z.Value);

            var elements = new List<JsonValue>();

            // AllElements, not Elements: the viewpoint names the finding's own elements and its
            // locations' together, and a sidecar over the narrower list would send a locator for
            // some of the components and none for the rest.
            foreach (var element in finding.AllElements())
            {
                if (element.IsEmpty) continue;

                var named = JsonValue.Object().Set("key", element.ToString());
                var at = how.LocatorOf == null ? default : how.LocatorOf(element);

                if (!string.IsNullOrEmpty(at.PathId)) named.Set("path", at.PathId);
                if (!string.IsNullOrEmpty(at.TreePath)) named.Set("tree", at.TreePath);

                elements.Add(named);
            }

            if (elements.Count > 0) json.Set("elements", JsonValue.Array(elements));

            var written = Facts(finding, how);

            if (written.Count > 0)
            {
                var facts = JsonValue.Object();

                foreach (var fact in written.OrderBy(one => one.Key, StringComparer.Ordinal))
                    facts.Set(fact.Key, fact.Value);

                json.Set("facts", facts);
            }

            return json;
        }

        /// <summary>
        /// A finding's facts as this pack writes them: its own, with the pack's on top.
        ///
        /// The pack's win. They are what a board derived from the open document — the level, the
        /// grid, the zone, the property columns — and the finding's own copy of one of those is
        /// whatever the last board to write it left there.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="how">How the pack is being written.</param>
        private static IReadOnlyDictionary<string, string?> Facts(Finding finding,
                                                                  FindingBcfOptions how)
        {
            var added = how.FactsOf == null ? null : how.FactsOf(finding);

            if (added == null || added.Count == 0)
                return (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(
                    finding.Facts, StringComparer.OrdinalIgnoreCase);

            var facts = new Dictionary<string, string?>(finding.Facts,
                                                        StringComparer.OrdinalIgnoreCase);

            foreach (var pair in added)
                if (string.IsNullOrWhiteSpace(pair.Value)) facts.Remove(pair.Key);
                else facts[pair.Key] = pair.Value;

            return facts;
        }

        /// <summary>The whole sidecar.</summary>
        private static JsonValue Sheet(IReadOnlyList<JsonValue> records, FindingBcfOptions how,
                                       DateTimeOffset when)
        {
            var json = JsonValue.Object()
                .Set("version", SidecarVersion)
                .Set("written", "CamelWorks")
                .Set("when", when.UtcTicks);

            if (!string.IsNullOrWhiteSpace(how.SourceFile)) json.Set("source", how.SourceFile);

            json.Set("findings", JsonValue.Array(records));

            return json;
        }

        /// <summary>The sidecar rows, by topic guid, or empty when the file carries none.</summary>
        private static IReadOnlyDictionary<string, FindingRecord> Sidecars(
            BcfReadResult read, List<string> warnings)
        {
            var sheet = new Dictionary<string, FindingRecord>(StringComparer.OrdinalIgnoreCase);

            if (!read.Extras.TryGetValue(Sidecar, out var text) || string.IsNullOrWhiteSpace(text))
                return sheet;

            if (!JsonReader.TryParse(text, out var json) || json.Kind != JsonKind.Object)
            {
                warnings.Add("This file carries a CamelWorks detail sheet that could not be read, "
                    + "so it was ignored. The topics themselves read normally.");

                return sheet;
            }

            var version = (int)json["version"].AsLong(SidecarVersion);

            if (version > SidecarVersion)
                warnings.Add("This file's CamelWorks detail was written by a newer version of "
                    + "CamelWorks. Whatever this one understands has been read; anything newer "
                    + "was left alone rather than guessed at.");

            // One row per topic. A file this writer produced carries one, because the writer
            // claims a topic guid per finding; a hand-edited or re-authored file can carry two,
            // and the row kept is then the one whose finding id sorts first rather than whichever
            // was written last.
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in json["findings"].Items
                                       .OrderBy(one => one["id"].AsString() ?? string.Empty,
                                                StringComparer.Ordinal))
            {
                var topic = record["topic"].AsString();

                if (string.IsNullOrEmpty(topic) || !claimed.Add(topic!)) continue;

                var elements = new List<ElementLocator>();

                foreach (var named in record["elements"].Items)
                    if (ElementKey.TryParse(named["key"].AsString(), out var key) && !key.IsEmpty)
                        elements.Add(new ElementLocator(key, named["path"].AsString(),
                                                        named["tree"].AsString()));

                var facts = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var written = record["facts"];

                foreach (var field in written.Keys) facts[field] = written[field].AsString();

                sheet[topic!] = new FindingRecord(
                    topic!, record["id"].AsString(), record["source"].AsString(),
                    record["rule"].AsString(), record["cell"].AsString(), elements,
                    record.Has("x") ? record["x"].AsDouble() : (double?)null,
                    record.Has("y") ? record["y"].AsDouble() : (double?)null,
                    record.Has("z") ? record["z"].AsDouble() : (double?)null,
                    facts);
            }

            return sheet;
        }

        /// <summary>How many topics another tool can point at something with, in one sentence.</summary>
        private static string Reach(int blind, int partial, int findings)
        {
            var reach = findings - blind;

            return (blind == 0
                    ? Measure.Count(partial)
                      + (partial == 1 ? " topic names an element" : " topics name elements")
                      + " another tool cannot find"
                    : Measure.Count(blind) + " of " + Measure.Count(findings)
                      + (findings == 1 ? " topic names no element" : " topics name no elements")
                      + " another tool can find"
                      + (partial > 0
                          ? ", and " + Measure.Count(partial) + " more name only some"
                          : string.Empty))
                + ". Those topics still carry a camera and a picture, so the reviewer lands in "
                + "the right place - their tool just will not select the elements. Only an IFC "
                + "GlobalId can be looked up, and these elements carry none; exporting the "
                + "GlobalId into the NWCs, or federating from IFC, is what fixes it."
                + (reach > 0 && blind > 0
                    ? " " + Measure.Count(reach) + " will select."
                    : string.Empty);
        }

        private static FindingPackRead Refuse(string why) =>
            new FindingPackRead(BcfVersion.V21, Array.Empty<BcfTopic>(),
                                new Dictionary<string, FindingRecord>(StringComparer.OrdinalIgnoreCase),
                                Array.Empty<string>(), why);

        /// <summary>
        /// Relate two topics, both ways round and once each.
        ///
        /// A reader that opened the clash first should be able to get to the conversation, not
        /// only the other way about.
        /// </summary>
        /// <param name="one">One topic.</param>
        /// <param name="other">The other.</param>
        private static void Relate(BcfTopic one, BcfTopic other)
        {
            if (!one.RelatedTopics.Any(named =>
                    string.Equals(named, other.Guid, StringComparison.OrdinalIgnoreCase)))
                one.RelatedTopics.Add(other.Guid);

            if (!other.RelatedTopics.Any(named =>
                    string.Equals(named, one.Guid, StringComparison.OrdinalIgnoreCase)))
                other.RelatedTopics.Add(one.Guid);
        }

        /// <summary>Write a prefixed label, when there is something to say.</summary>
        private static void Label(BcfTopic topic, string prefix, string? value)
        {
            var written = FindingLabels.Written(prefix, value);

            if (written != null
                && !topic.Labels.Contains(written, StringComparer.OrdinalIgnoreCase))
                topic.Labels.Add(written);
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
