using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Issues
{
    /// <summary>
    /// A finding-set somebody raised.
    ///
    /// A finding is what a check produced — a clash, a clearance breach, a rule the model failed.
    /// An issue is what somebody decided to do about one or several of them, and it can exist with
    /// no finding at all ("no access to this valve"). Collapsing the two would mean either inventing
    /// machine findings for things no machine found, or giving every one of eighteen thousand
    /// clashes a conversation nobody will have.
    ///
    /// It gains nothing a finding does not have. The status is the same five-rung lattice, the
    /// comment is the same type, the priority comes from the same project-configured list, and the
    /// due date is the same day number. An issue over three findings offering a different
    /// vocabulary from the findings it covers is how two records that describe one problem drift.
    ///
    /// An issue may only be attached to findings that already exist. There is no holding pen for a
    /// result the register has not seen: a placeholder keyed on something other than a finding id
    /// has to be swapped for one later, and that swap is what the deleted clash-key list was.
    /// </summary>
    public sealed class Issue
    {
        /// <summary>What <see cref="Provenance"/> says for an issue somebody typed.</summary>
        public const string ByHand = "Hand";

        /// <summary>What <see cref="Provenance"/> says for an issue read out of a BCF file.</summary>
        public const string ByImport = "Bcf";

        /// <summary>The <see cref="Facts"/> key holding which level it is on.</summary>
        public const string LevelFact = "level";

        /// <summary>The <see cref="Facts"/> key holding which zone or room it is in.</summary>
        public const string ZoneFact = "zone";

        /// <summary>The <see cref="Facts"/> key holding where it came from — a test name, a file name.</summary>
        public const string SourceFact = "source";

        /// <summary>The <see cref="Facts"/> key holding a typed resolution, as JSON.</summary>
        public const string InstructionFact = "instruction";

        /// <summary>
        /// The <see cref="Facts"/> key holding this issue's board identity, as a clash key.
        ///
        /// Written once and read back thereafter. A clash key is quantised around a point, so
        /// re-deriving it every board build would move it the moment somebody nudged an element,
        /// and the group decision, the pinned group and the per-key assignment recorded against the
        /// old key would all be left behind. A clash-pipeline concept and not a field every issue
        /// needs, so it rides here rather than on the record. See <c>IssueItems.KeyFor</c>.
        /// </summary>
        public const string KeyFact = "key";

        /// <summary>The kinds an issue is, offered rather than imposed — the field takes any text.</summary>
        public static readonly string[] Types =
            { "Coordination", "Clash", "Clearance", "Design", "Safety", "Quality", "RFI", "Snag" };

        /// <summary>Why it happened, offered for the ones that repeat.</summary>
        public static readonly string[] RootCauses =
            { "Design", "Coordination", "Information", "Fabrication", "Site", "Change", "Unknown" };

        /// <summary>Create an issue.</summary>
        /// <param name="id">Stable id, unique on this project.</param>
        /// <param name="title">What it is called.</param>
        /// <param name="provenance">Where it came from. See <see cref="Provenance"/>.</param>
        public Issue(string id, string title, string? provenance = ByHand)
        {
            Id = string.IsNullOrWhiteSpace(id)
                ? throw new ArgumentException("id is required", nameof(id))
                : id.Trim();

            Title = string.IsNullOrWhiteSpace(title) ? "Untitled issue" : title.Trim();
            Provenance = string.IsNullOrWhiteSpace(provenance) ? ByHand : provenance!.Trim();
        }

        /// <summary>Stable id, unique on this project.</summary>
        public string Id { get; }

        /// <summary>
        /// Where this issue came from — <see cref="ByHand"/>, <see cref="ByImport"/>, a check's key.
        ///
        /// Set at creation and never derived. An imported topic arrives with no findings attached,
        /// so the number of attached findings cannot tell an imported issue from a hand-typed one,
        /// and the list screens filter on exactly that distinction.
        /// </summary>
        public string Provenance { get; set; }

        /// <summary>What it is called.</summary>
        public string Title { get; set; }

        /// <summary>What is wrong, in the words of whoever saw it.</summary>
        public string? Description { get; set; }

        /// <summary>What kind of issue it is. A BCF round-trip field.</summary>
        public string? Type { get; set; } = "Coordination";

        /// <summary>
        /// Where it stands, on the same lattice as a finding.
        ///
        /// A person deciding about an issue may demote it; that is a direct edit, not a merge, and
        /// <see cref="FindingStatusLattice.IsDemotion"/> is what a screen confirms on.
        /// </summary>
        public FindingStatus Status { get; set; } = FindingStatus.Active;

        /// <summary>How urgent, from the project's configurable list. Null until somebody sets one.</summary>
        public string? Priority { get; set; }

        /// <summary>Who has to do something about it.</summary>
        public string? AssignedTo { get; set; }

        /// <summary>Who raised it. A BCF round-trip field.</summary>
        public string? Author { get; set; }

        /// <summary>Why it happened, when somebody has said.</summary>
        public string? RootCause { get; set; }

        /// <summary>When it was raised, in UTC ticks.</summary>
        public long CreatedTicks { get; set; }

        /// <summary>When it was last edited, in UTC ticks.</summary>
        public long ModifiedTicks { get; set; }

        /// <summary>
        /// When it is due, as a day number, or null for no date.
        ///
        /// Whole days since <see cref="DateTime.MinValue"/>, the same unit as
        /// <see cref="Finding.DueDayNumber"/>. Not ticks: a due date is a day somebody named and
        /// not an instant anybody observed, and a tick value read as a day number lands in year 1.
        /// </summary>
        public int? DueDayNumber { get; set; }

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
        /// The findings it covers, by id — one clash, a whole specification, a clearance breach.
        /// Any check's, and any mixture.
        ///
        /// Ids rather than the findings themselves. A finding's id is derived from what makes it
        /// that finding, so it survives a re-run that renumbers everything the host issued. An issue
        /// holding the record itself would be holding a stale copy.
        /// </summary>
        public IList<string> Findings { get; } = new List<string>();

        /// <summary>Labels, free text, as BCF carries them.</summary>
        public IList<string> Labels { get; } = new List<string>();

        /// <summary>The conversation, oldest first. The same comment type a finding carries.</summary>
        public IList<FindingComment> Comments { get; } = new List<FindingComment>();

        /// <summary>
        /// The producer's own values, by name — level, zone, the file a topic arrived in.
        ///
        /// The open bag that stops this record growing a field per check. Nothing
        /// coordination-specific is a field here, so a check can carry its own facts onto an issue.
        /// </summary>
        public IDictionary<string, string?> Facts { get; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Its GUID in the BCF file it came from or went out in, when it has one.</summary>
        public string? BcfGuid { get; set; }

        /// <summary>True when it is still somebody's to deal with.</summary>
        public bool IsOpen => FindingStatusLattice.IsOpen(Status);

        /// <summary>True when it is open, due, and the day has passed.</summary>
        /// <param name="today">Today, as a day number.</param>
        public bool IsOverdue(int today) =>
            IsOpen && DueDayNumber != null && DueDayNumber.Value < today;

        /// <summary>The latest thing anybody said, or null.</summary>
        public FindingComment? LastComment =>
            Comments.Count == 0 ? null : Comments[Comments.Count - 1];

        /// <summary>
        /// Which checks this issue's findings came from, in the order they read.
        ///
        /// Derived from the findings, falling back to <see cref="Provenance"/> when none are
        /// attached — which is the ordinary state of an imported or hand-typed issue.
        /// </summary>
        /// <param name="register">Where the findings are.</param>
        public IReadOnlyList<string> Sources(FindingRegister? register)
        {
            var sources = new List<string>();

            foreach (var id in Findings)
            {
                var finding = register?.Find(id);

                if (finding != null && !sources.Contains(finding.Source, StringComparer.Ordinal))
                    sources.Add(finding.Source);
            }

            if (sources.Count == 0) sources.Add(Provenance);

            return sources;
        }

        /// <summary>How it reads where only one line fits.</summary>
        public string Describe() =>
            Id + " · " + Title + " (" + FindingStatusLattice.ToName(Status)
            + (string.IsNullOrEmpty(AssignedTo) ? ", unassigned" : ", " + AssignedTo) + ")";

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("id", Id)
                .Set("title", Title)
                .Set("provenance", Provenance)
                .Set("status", FindingStatusLattice.ToName(Status));

            if (!string.IsNullOrEmpty(Type)) json.Set("type", Type);
            if (!string.IsNullOrEmpty(Priority)) json.Set("priority", Priority);
            if (!string.IsNullOrEmpty(Description)) json.Set("description", Description);
            if (!string.IsNullOrEmpty(AssignedTo)) json.Set("assigned", AssignedTo);
            if (!string.IsNullOrEmpty(Author)) json.Set("author", Author);
            if (!string.IsNullOrEmpty(RootCause)) json.Set("cause", RootCause);
            if (!string.IsNullOrEmpty(BcfGuid)) json.Set("bcf", BcfGuid);

            if (CreatedTicks > 0) json.Set("created", CreatedTicks);
            if (ModifiedTicks > 0) json.Set("modified", ModifiedTicks);
            if (DueDayNumber != null) json.Set("due", DueDayNumber.Value);

            if (HasPoint)
            {
                json.Set("point", true);
                json.Set("x", X).Set("y", Y).Set("z", Z);
            }

            if (Elements.Count > 0)
                json.Set("elements", JsonValue.Array(Elements.Select(e => JsonValue.String(e.ToString()))));

            if (Findings.Count > 0)
                json.Set("findings", JsonValue.Array(Findings.Select(JsonValue.String)));

            if (Labels.Count > 0)
                json.Set("labels", JsonValue.Array(Labels.Select(JsonValue.String)));

            if (Comments.Count > 0)
                json.Set("comments", JsonValue.Array(Comments.Select(c =>
                    JsonValue.Object()
                        .Set("author", c.Author)
                        .Set("when", c.WhenTicks)
                        .Set("body", c.Body))));

            if (Facts.Count > 0)
            {
                var facts = JsonValue.Object();

                foreach (var pair in Facts.OrderBy(f => f.Key, StringComparer.Ordinal))
                    facts.Set(pair.Key, pair.Value);

                json.Set("facts", facts);
            }

            return json;
        }

        /// <summary>Read one back, or null when the record cannot be read.</summary>
        /// <param name="json">One saved issue.</param>
        public static Issue? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var id = json["id"].AsString();
            if (string.IsNullOrWhiteSpace(id)) return null;

            var issue = new Issue(id!, json["title"].AsString() ?? "Untitled issue",
                                  json["provenance"].AsString())
            {
                Description = json["description"].AsString(),
                Type = json["type"].AsString(),
                Status = FindingStatusLattice.TryParse(json["status"].AsString(), out var status)
                    ? status
                    : FindingStatus.Active,
                Priority = json["priority"].AsString(),
                AssignedTo = json["assigned"].AsString(),
                Author = json["author"].AsString(),
                RootCause = json["cause"].AsString(),
                BcfGuid = json["bcf"].AsString(),
                CreatedTicks = json["created"].AsLong(),
                ModifiedTicks = json["modified"].AsLong(),
                HasPoint = json["point"].AsBool(),
                X = json["x"].AsDouble(),
                Y = json["y"].AsDouble(),
                Z = json["z"].AsDouble(),
            };

            if (json.Has("due")) issue.DueDayNumber = (int)json["due"].AsLong();

            foreach (var element in json["elements"].Items)
                if (ElementKey.TryParse(element.AsString(), out var key)) issue.Elements.Add(key);

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
                if (comment["body"].AsString() is string body)
                    issue.Comments.Add(new FindingComment(comment["author"].AsString(),
                                                          comment["when"].AsLong(), body));

            var facts = json["facts"];

            foreach (var field in facts.Keys) issue.Facts[field] = facts[field].AsString();

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

        /// <summary>The shape's version, so a later change can be told apart.</summary>
        public const int Version = 1;

        /// <summary>The issues, in the order they were raised.</summary>
        public IReadOnlyList<Issue> Issues => _issues;

        /// <summary>How many records could not be read back.</summary>
        public int Unreadable { get; private set; }

        /// <summary>How many are still somebody's to deal with.</summary>
        public int Open => _issues.Count(i => i.IsOpen);

        /// <summary>How many are in one status.</summary>
        /// <param name="status">The status.</param>
        public int Of(FindingStatus status) => _issues.Count(i => i.Status == status);

        /// <summary>How many are open and past their date.</summary>
        /// <param name="today">Today, as a day number.</param>
        public int Overdue(int today) => _issues.Count(i => i.IsOverdue(today));

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
        /// The issues covering a finding, in register order.
        ///
        /// A list rather than one: the same duct through the same beam can be both a coordination
        /// issue and an RFI, and refusing the second would be refusing to describe the job.
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
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("version", Version)
                .Set("issues", JsonValue.Array(_issues.Select(i => i.ToJson())));

        /// <summary>Read a register back. An unreadable record is counted, never thrown.</summary>
        /// <param name="json">What was saved, or null.</param>
        /// <param name="notes">Anything worth saying about what was read.</param>
        public static IssueRegister FromJson(JsonValue? json, IList<string>? notes = null)
        {
            var register = new IssueRegister();

            if (json == null || json.Kind != JsonKind.Object) return register;

            // The version is written, so it is read. A file from a later build is read for what this
            // build understands rather than refused, and the reader is told that is what happened.
            if ((int)json["version"].AsLong(Version) > Version)
                notes?.Add("These issues were written by a newer version of CamelWorks. What this "
                           + "one understands has been read.");

            foreach (var entry in json["issues"].Items)
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
