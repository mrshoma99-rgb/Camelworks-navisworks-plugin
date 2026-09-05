using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Issues
{
    /// <summary>
    /// Where an issue came from. The one fact about an issue nobody may edit.
    ///
    /// Written and read back by name rather than by number, so a member appended here leaves
    /// every issue anybody has already raised reading exactly as it did.
    /// </summary>
    public enum IssueOrigin
    {
        /// <summary>Somebody wrote it.</summary>
        Manual = 0,

        /// <summary>Made from one or more clash results.</summary>
        Clash = 1,

        /// <summary>Made from a clearance finding.</summary>
        Clearance = 2,

        /// <summary>Read out of a BCF file somebody sent.</summary>
        Imported = 3,

        /// <summary>Made from a difference between two revisions of the model.</summary>
        Compare = 4,

        /// <summary>Made from a model or data health rule.</summary>
        Health = 5,

        /// <summary>Made from a specification the model failed.</summary>
        Ids = 6,
    }

    /// <summary>One thing somebody said on an issue, and when.</summary>
    public sealed class IssueComment
    {
        /// <summary>Create a comment.</summary>
        /// <param name="author">Who said it.</param>
        /// <param name="whenTicks">When, in UTC ticks.</param>
        /// <param name="body">What they said.</param>
        public IssueComment(string author, long whenTicks, string body)
        {
            Author = string.IsNullOrWhiteSpace(author) ? "unknown" : author.Trim();
            WhenTicks = whenTicks;
            Body = body ?? string.Empty;
        }

        /// <summary>Who said it.</summary>
        public string Author { get; }

        /// <summary>When, in UTC ticks.</summary>
        public long WhenTicks { get; }

        /// <summary>What they said.</summary>
        public string Body { get; }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("author", Author)
                .Set("when", WhenTicks)
                .Set("body", Body);

        /// <summary>Read one back, or null when the record cannot be read.</summary>
        /// <param name="json">One saved comment.</param>
        public static IssueComment? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var body = json["body"].AsString();
            if (body == null) return null;

            return new IssueComment(json["author"].AsString() ?? "unknown",
                                    json["when"].AsLong(), body);
        }

        /// <inheritdoc />
        public override string ToString() => Author + ": " + Body;
    }

    /// <summary>
    /// One coordination issue, whatever found it.
    ///
    /// Separate from the clash result because a clash result is what an engine measured and an issue
    /// is what somebody decided to do about something. Most of what goes wrong in a federation is
    /// not an intersection — a door swings into a corridor, a valve has nowhere to be pulled to, the
    /// clear height under a duct is short, a reviewer sent a BCF file of their own — and none of
    /// those fits a record whose identity is "two elements that touch". So the issue is its own
    /// record, and the clash result, the clearance finding and the imported topic are three of the
    /// four ways one gets raised.
    ///
    /// The origin is never edited. Everything else about an issue is judgement and can be changed by
    /// the next person; where it came from is a fact, and an issue that could be re-labelled as
    /// manual after being imported could not be reconciled with the file it came from.
    /// </summary>
    public sealed class Issue
    {
        /// <summary>The statuses an issue moves through, in the order they read.</summary>
        public static readonly string[] Statuses =
            { "Draft", "Open", "Answered", "Pending", "In dispute", "Closed" };

        /// <summary>The kinds an issue is, offered rather than imposed — the field takes any text.</summary>
        public static readonly string[] Types =
            { "Coordination", "Clash", "Clearance", "Design", "Safety", "Quality", "RFI", "Snag" };

        /// <summary>How urgent, in the order they read.</summary>
        public static readonly string[] Priorities = { "Low", "Normal", "High", "Critical" };

        /// <summary>Why it happened, offered for the ones that repeat.</summary>
        public static readonly string[] RootCauses =
            { "Design", "Coordination", "Information", "Fabrication", "Site", "Change", "Unknown" };

        /// <summary>The status an issue nobody has closed is in.</summary>
        public const string OpenStatus = "Open";

        /// <summary>The status that takes an issue off the open count.</summary>
        public const string ClosedStatus = "Closed";

        /// <summary>Create an issue.</summary>
        /// <param name="id">Stable id, unique on this project.</param>
        /// <param name="title">What it is called.</param>
        /// <param name="origin">What raised it. Never changes afterwards.</param>
        public Issue(string id, string title, IssueOrigin origin = IssueOrigin.Manual)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("id is required", nameof(id))
                : id.Trim();

            Title = string.IsNullOrWhiteSpace(title) ? "Untitled issue" : title.Trim();
            Origin = origin;
        }

        /// <summary>Stable id, unique on this project.</summary>
        public string Id { get; }

        /// <summary>What raised it. A fact about the past, so it is set once.</summary>
        public IssueOrigin Origin { get; }

        /// <summary>What it is called.</summary>
        public string Title { get; set; }

        /// <summary>What is wrong, in the words of whoever saw it.</summary>
        public string? Description { get; set; }

        /// <summary>What kind of issue it is.</summary>
        public string Type { get; set; } = "Coordination";

        /// <summary>Where it stands.</summary>
        public string Status { get; set; } = OpenStatus;

        /// <summary>How urgent.</summary>
        public string Priority { get; set; } = "Normal";

        /// <summary>Who has to do something about it.</summary>
        public string? AssignedTo { get; set; }

        /// <summary>Who raised it.</summary>
        public string? Author { get; set; }

        /// <summary>Why it happened, when somebody has said.</summary>
        public string? RootCause { get; set; }

        /// <summary>Which level it is on, when one is known.</summary>
        public string? Level { get; set; }

        /// <summary>Which zone or room, when one is known.</summary>
        public string? Zone { get; set; }

        /// <summary>Where it came from, said in one line — a test name, a file name.</summary>
        public string? Source { get; set; }

        /// <summary>When it was raised, in UTC ticks.</summary>
        public long CreatedTicks { get; set; }

        /// <summary>When it was last edited, in UTC ticks.</summary>
        public long ModifiedTicks { get; set; }

        /// <summary>When it is due, in UTC ticks, or zero for no date.</summary>
        public long DueTicks { get; set; }

        /// <summary>Whether it has a place in the model.</summary>
        public bool HasPoint { get; set; }

        /// <summary>Where it is, in metres.</summary>
        public double X { get; set; }

        /// <summary>Where it is, in metres.</summary>
        public double Y { get; set; }

        /// <summary>Where it is, in metres.</summary>
        public double Z { get; set; }

        /// <summary>The elements it is about.</summary>
        public IList<ElementKey> Elements { get; } = new List<ElementKey>();

        /// <summary>
        /// The clash results it covers, when it was raised from any.
        ///
        /// Superseded by <see cref="Findings"/> and kept for reading only. An issue could previously
        /// hold clash results and nothing else, so this is what older projects have. A clash key
        /// names two elements and a place but not the test that found them, so nothing can turn one
        /// into a finding id on its own; the swap happens on the next read of the results, where
        /// both are in hand. Until then the issue still records which clashes it is about.
        /// </summary>
        public IList<ClashKey> Clashes { get; } = new List<ClashKey>();

        /// <summary>
        /// The findings it covers, by id — one clash, a whole test's worth, a clearance breach, a
        /// health rule's failures. Any check's, and any mixture.
        ///
        /// Ids rather than the findings themselves. A finding's id is derived from what makes it
        /// that finding, so it survives a re-run that renumbers everything the host issued. An issue
        /// holding the record itself would be holding a stale copy.
        /// </summary>
        public IList<string> Findings { get; } = new List<string>();

        /// <summary>Labels, free text, as BCF carries them.</summary>
        public IList<string> Labels { get; } = new List<string>();

        /// <summary>The conversation, oldest first.</summary>
        public IList<IssueComment> Comments { get; } = new List<IssueComment>();

        /// <summary>Its GUID in the BCF file it came from or went out in, when it has one.</summary>
        public string? BcfGuid { get; set; }

        /// <summary>True when it is not closed.</summary>
        public bool IsOpen =>
            !string.Equals(Status, ClosedStatus, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when it is due and the day has passed.</summary>
        /// <param name="nowTicks">Now, in UTC ticks.</param>
        public bool IsOverdue(long nowTicks) => IsOpen && DueTicks > 0 && DueTicks < nowTicks;

        /// <summary>The latest thing anybody said, or null.</summary>
        public IssueComment? LastComment =>
            Comments.Count == 0 ? null : Comments[Comments.Count - 1];

        /// <summary>Say something on it, and stamp it as edited.</summary>
        /// <param name="author">Who is speaking.</param>
        /// <param name="body">What they say.</param>
        /// <param name="whenTicks">When, in UTC ticks.</param>
        public void Say(string author, string body, long whenTicks)
        {
            if (string.IsNullOrWhiteSpace(body)) return;

            Comments.Add(new IssueComment(author, whenTicks, body.Trim()));
            ModifiedTicks = whenTicks;
        }

        /// <summary>How it reads where only one line fits.</summary>
        public string Describe() =>
            Id + " · " + Title + " (" + Status
            + (string.IsNullOrEmpty(AssignedTo) ? ", unassigned" : ", " + AssignedTo) + ")";

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("id", Id)
                .Set("title", Title)
                .Set("origin", Origin.ToString())
                .Set("type", Type)
                .Set("status", Status)
                .Set("priority", Priority);

            if (!string.IsNullOrEmpty(Description)) json.Set("description", Description);
            if (!string.IsNullOrEmpty(AssignedTo)) json.Set("assigned", AssignedTo);
            if (!string.IsNullOrEmpty(Author)) json.Set("author", Author);
            if (!string.IsNullOrEmpty(RootCause)) json.Set("cause", RootCause);
            if (!string.IsNullOrEmpty(Level)) json.Set("level", Level);
            if (!string.IsNullOrEmpty(Zone)) json.Set("zone", Zone);
            if (!string.IsNullOrEmpty(Source)) json.Set("source", Source);
            if (!string.IsNullOrEmpty(BcfGuid)) json.Set("bcf", BcfGuid);

            if (CreatedTicks > 0) json.Set("created", CreatedTicks);
            if (ModifiedTicks > 0) json.Set("modified", ModifiedTicks);
            if (DueTicks > 0) json.Set("due", DueTicks);

            if (HasPoint)
            {
                json.Set("point", true);
                json.Set("x", X).Set("y", Y).Set("z", Z);
            }

            if (Elements.Count > 0)
                json.Set("elements", JsonValue.Array(Elements.Select(e => JsonValue.String(e.ToString()))));

            if (Clashes.Count > 0)
                json.Set("clashes", JsonValue.Array(Clashes.Select(k => JsonValue.String(k.ToString()))));

            if (Findings.Count > 0)
                json.Set("findings", JsonValue.Array(Findings.Select(JsonValue.String)));

            if (Labels.Count > 0)
                json.Set("labels", JsonValue.Array(Labels.Select(JsonValue.String)));

            if (Comments.Count > 0)
                json.Set("comments", JsonValue.Array(Comments.Select(c => c.ToJson())));

            return json;
        }

        /// <summary>Read one back, or null when the record cannot be read.</summary>
        /// <param name="json">One saved issue.</param>
        public static Issue? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var id = json["id"].AsString();
            if (string.IsNullOrWhiteSpace(id)) return null;

            var origin = Enum.TryParse<IssueOrigin>(json["origin"].AsString(), true, out var parsed)
                ? parsed
                : IssueOrigin.Manual;

            var issue = new Issue(id!, json["title"].AsString() ?? "Untitled issue", origin)
            {
                Description = json["description"].AsString(),
                Type = json["type"].AsString() ?? "Coordination",
                Status = json["status"].AsString() ?? OpenStatus,
                Priority = json["priority"].AsString() ?? "Normal",
                AssignedTo = json["assigned"].AsString(),
                Author = json["author"].AsString(),
                RootCause = json["cause"].AsString(),
                Level = json["level"].AsString(),
                Zone = json["zone"].AsString(),
                Source = json["source"].AsString(),
                BcfGuid = json["bcf"].AsString(),
                CreatedTicks = json["created"].AsLong(),
                ModifiedTicks = json["modified"].AsLong(),
                DueTicks = json["due"].AsLong(),
                HasPoint = json["point"].AsBool(),
                X = json["x"].AsDouble(),
                Y = json["y"].AsDouble(),
                Z = json["z"].AsDouble(),
            };

            foreach (var element in json["elements"].Items)
                if (ElementKey.TryParse(element.AsString(), out var key)) issue.Elements.Add(key);

            // A key that will not parse is dropped rather than failing the issue: the sentence
            // somebody wrote is the valuable half, and losing it over one unreadable attachment
            // would be the record punishing the reader for a bug in the writer.
            foreach (var clash in json["clashes"].Items)
                if (ClashKey.TryParse(clash.AsString(), out var key)) issue.Clashes.Add(key);

            foreach (var finding in json["findings"].Items)
                if (finding.AsString() is string attached && attached.Length > 0
                    && !issue.Findings.Contains(attached))
                    issue.Findings.Add(attached);

            foreach (var label in json["labels"].Items)
            {
                var text = label.AsString();
                if (!string.IsNullOrWhiteSpace(text)) issue.Labels.Add(text!);
            }

            foreach (var comment in json["comments"].Items)
            {
                var read = IssueComment.FromJson(comment);
                if (read != null) issue.Comments.Add(read);
            }

            return issue;
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }

    /// <summary>
    /// Every issue on this project, however it was raised.
    ///
    /// One register rather than one per source: a coordinator's question is "what is open on level
    /// three", never "what is open on level three among the ones that came from a clash test". The
    /// source is a column, not a filing cabinet.
    /// </summary>
    public sealed class IssueRegister
    {
        private readonly List<Issue> _issues = new List<Issue>();

        /// <summary>What ids look like before the number.</summary>
        public const string Prefix = "CW-";

        /// <summary>The issues, in the order they were raised.</summary>
        public IReadOnlyList<Issue> Issues => _issues;

        /// <summary>How many records could not be read back.</summary>
        public int Unreadable { get; private set; }

        /// <summary>How many are not closed.</summary>
        public int Open => _issues.Count(i => i.IsOpen);

        /// <summary>How many are in one status.</summary>
        /// <param name="status">The status.</param>
        public int Of(string status) =>
            _issues.Count(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase));

        /// <summary>How many are open and past their date.</summary>
        /// <param name="nowTicks">Now, in UTC ticks.</param>
        public int Overdue(long nowTicks) => _issues.Count(i => i.IsOverdue(nowTicks));

        /// <summary>Add or replace by id.</summary>
        /// <param name="issue">The issue.</param>
        public void Put(Issue issue)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue));

            var at = _issues.FindIndex(i => string.Equals(i.Id, issue.Id, StringComparison.Ordinal));
            if (at >= 0) _issues[at] = issue; else _issues.Add(issue);
        }

        /// <summary>Remove by id. Removing something absent is not an error.</summary>
        /// <param name="id">The issue id.</param>
        public void Remove(string? id) =>
            _issues.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal));

        /// <summary>One by id, or null.</summary>
        /// <param name="id">The issue id.</param>
        public Issue? Find(string? id) =>
            id == null ? null : _issues.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

        /// <summary>One by the GUID of the BCF topic it came from, or null.</summary>
        /// <param name="guid">The topic GUID.</param>
        public Issue? ByGuid(string? guid) =>
            string.IsNullOrWhiteSpace(guid)
                ? null
                : _issues.FirstOrDefault(i => string.Equals(i.BcfGuid, guid, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The issue covering this clash result, or null.
        ///
        /// One clash, at most one issue: two issues both claiming the same conflict is two people
        /// each believing the other is dealing with it.
        /// </summary>
        /// <param name="key">The clash result.</param>
        public Issue? Holding(ClashKey key) =>
            key.IsEmpty ? null : _issues.FirstOrDefault(i => i.Clashes.Contains(key));

        /// <summary>
        /// The issues covering a finding, in register order.
        ///
        /// A list, unlike <see cref="Holding(ClashKey)"/>: a clash may sit on at most one issue,
        /// because two issues claiming one conflict is two people each believing the other is
        /// dealing with it. A finding is not so lucky - the same duct through the same beam can
        /// be both a coordination issue and an RFI, and refusing the second would be refusing to
        /// describe the job.
        /// </summary>
        /// <param name="findingId">The finding's id.</param>
        public IReadOnlyList<Issue> Holding(string? findingId) =>
            findingId == null
                ? Array.Empty<Issue>()
                : _issues.Where(i => i.Findings.Contains(findingId, StringComparer.Ordinal)).ToList();

        /// <summary>The next free id, numbered from one.</summary>
        public string NextId()
        {
            for (var n = _issues.Count + 1; ; n++)
            {
                var candidate = Prefix + n.ToString(CultureInfo.InvariantCulture);
                if (Find(candidate) == null) return candidate;
            }
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson() => JsonValue.Array(_issues.Select(i => i.ToJson()));

        /// <summary>Read a register back. An unreadable record is counted, never thrown.</summary>
        /// <param name="json">The saved array.</param>
        public static IssueRegister FromJson(JsonValue? json)
        {
            var register = new IssueRegister();

            if (json == null) return register;

            foreach (var entry in json.Items)
            {
                var issue = Issue.FromJson(entry);

                if (issue == null) register.Unreadable++;
                else register.Put(issue);
            }

            return register;
        }

        /// <inheritdoc />
        public override string ToString() =>
            _issues.Count.ToString(CultureInfo.InvariantCulture) + " issues, "
            + Open.ToString(CultureInfo.InvariantCulture) + " open";
    }
}
