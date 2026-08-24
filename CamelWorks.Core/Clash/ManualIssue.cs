using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// A coordination problem somebody raised, that no clash test produced.
    ///
    /// Most of what goes wrong in a federation is not an intersection. A door swings into a
    /// corridor, a valve has nowhere to be pulled to, an access route stops at a wall, a riser has
    /// no builder's work opening — none of them are clashes, all of them are coordination, and
    /// none of them can be raised in a tool whose board only knows what the clash engine said. So
    /// they end up in a spreadsheet beside the board, get reported separately, and are closed
    /// without anybody being able to see them next to the work they belong with.
    ///
    /// A raised issue is an ordinary board item from the moment it exists. It groups, assigns,
    /// reports, exports as BCF and carries its status across runs exactly like a clash does,
    /// because it <i>is</i> one as far as everything downstream is concerned. The only difference
    /// is where it came from, which the board says on its face.
    /// </summary>
    public sealed class ManualIssue
    {
        /// <summary>What the test column says for something nobody's engine found.</summary>
        public const string RaisedByHand = "Raised by hand";

        /// <summary>Create an issue.</summary>
        /// <param name="id">Stable id.</param>
        /// <param name="key">Its board identity, made once and kept.</param>
        /// <param name="title">What it is called on the board.</param>
        public ManualIssue(string id, ClashKey key, string title)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("id is required", nameof(id)) : id;
            Key = key;
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled issue" : title.Trim();
        }

        /// <summary>Stable id.</summary>
        public string Id { get; }

        /// <summary>
        /// Its board identity, made once when the issue was raised and never recomputed.
        ///
        /// Kept rather than derived because deriving it again would move it: a key is quantised
        /// around a point, and the centroid of a selection changes the moment somebody nudges one
        /// of the elements. A raised issue that changed identity when the model moved would look
        /// resolved and then new, every week, forever.
        /// </summary>
        public ClashKey Key { get; }

        /// <summary>What it is called on the board.</summary>
        public string Title { get; set; }

        /// <summary>What is wrong, in the words of whoever saw it.</summary>
        public string? Note { get; set; }

        /// <summary>
        /// What to do about it, once somebody has said. An issue somebody raised deserves the
        /// typed instruction as much as a clash does — more, since nobody's engine will restate it.
        /// </summary>
        public Resolution Instruction { get; set; } = Resolution.None;

        /// <summary>Who has to do something about it.</summary>
        public string? AssignedTo { get; set; }

        /// <summary>How urgent.</summary>
        public string? Priority { get; set; }

        /// <summary>Which level it is on, when one could be worked out.</summary>
        public string? Level { get; set; }

        /// <summary>Where it is, in metres.</summary>
        public double X { get; set; }

        /// <summary>Where it is, in metres.</summary>
        public double Y { get; set; }

        /// <summary>Where it is, in metres.</summary>
        public double Z { get; set; }

        /// <summary>Where it stands.</summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <summary>Who raised it.</summary>
        public string? Author { get; set; }

        /// <summary>When, as ticks, or zero when nobody recorded it.</summary>
        public long RaisedTicks { get; set; }

        /// <summary>The elements it is about, beyond the two in the key.</summary>
        public IList<ElementKey> Elements { get; } = new List<ElementKey>();

        /// <summary>
        /// Turn it into a board item.
        ///
        /// Its own group, always: an issue somebody wrote a sentence about is not one of eleven
        /// rows under "Grid C-4". The title is the group, and a pinned group is never re-derived by
        /// the rules, so it stays where it was put.
        /// </summary>
        public ClashItem ToItem() => new ClashItem(Key, RaisedByHand)
        {
            Status = Status,
            CarriedGroup = Title,
            Level = Level,
            X = X,
            Y = Y,
            Z = Z,
            ModelA = Author,
            CategoryA = "Raised",
            CategoryB = Priority,
        };

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("id", JsonValue.String(Id))
                .Set("key", JsonValue.String(Key.ToString()))
                .Set("title", JsonValue.String(Title))
                .Set("status", JsonValue.String(Status.ToString()))
                .Set("x", JsonValue.Number(X))
                .Set("y", JsonValue.Number(Y))
                .Set("z", JsonValue.Number(Z));

            if (!string.IsNullOrEmpty(Note)) json.Set("note", JsonValue.String(Note));
            if (Instruction.IsStated) json.Set("instruction", Instruction.ToJson());
            if (!string.IsNullOrEmpty(AssignedTo)) json.Set("party", JsonValue.String(AssignedTo));
            if (!string.IsNullOrEmpty(Priority)) json.Set("priority", JsonValue.String(Priority));
            if (!string.IsNullOrEmpty(Level)) json.Set("level", JsonValue.String(Level));
            if (!string.IsNullOrEmpty(Author)) json.Set("author", JsonValue.String(Author));
            if (RaisedTicks > 0) json.Set("raised", JsonValue.Number(RaisedTicks));

            if (Elements.Count > 0)
                json.Set("elements", JsonValue.Array(Elements.Select(e => JsonValue.String(e.ToString()))));

            return json;
        }

        /// <summary>Read one back, or null when the record cannot be read.</summary>
        /// <param name="json">One saved issue.</param>
        public static ManualIssue? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var id = json["id"].AsString();
            var title = json["title"].AsString();

            if (string.IsNullOrWhiteSpace(id) || !ClashKey.TryParse(json["key"].AsString(), out var key))
                return null;

            var issue = new ManualIssue(id!, key, title ?? "Untitled issue")
            {
                Note = json["note"].AsString(),
                AssignedTo = json["party"].AsString(),
                Priority = json["priority"].AsString(),
                Level = json["level"].AsString(),
                Author = json["author"].AsString(),
                X = json["x"].AsDouble(),
                Y = json["y"].AsDouble(),
                Z = json["z"].AsDouble(),
                Status = Enum.TryParse<FindingStatus>(json["status"].AsString(), out var status)
                    ? status
                    : FindingStatus.New,
            };

            issue.RaisedTicks = json["raised"].AsLong();
            issue.Instruction = Resolution.FromJson(json["instruction"]);

            foreach (var element in json["elements"].Items)
            {
                if (ElementKey.TryParse(element.AsString(), out var parsed)) issue.Elements.Add(parsed);
            }

            return issue;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Title + (AssignedTo == null ? string.Empty : " - " + AssignedTo);
    }

    /// <summary>Every issue raised on this project.</summary>
    public sealed class ManualIssueList
    {
        private readonly List<ManualIssue> _issues = new List<ManualIssue>();

        /// <summary>The issues, in the order they were raised.</summary>
        public IReadOnlyList<ManualIssue> Issues => _issues;

        /// <summary>How many records could not be read back.</summary>
        public int Unreadable { get; private set; }

        /// <summary>Add or replace by id.</summary>
        /// <param name="issue">The issue.</param>
        public void Put(ManualIssue issue)
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
        public ManualIssue? Find(string? id) =>
            id == null ? null : _issues.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

        /// <summary>A free id that is not in use.</summary>
        public string NextId()
        {
            for (var n = 1; ; n++)
            {
                var candidate = "issue-" + n.ToString(CultureInfo.InvariantCulture);
                if (Find(candidate) == null) return candidate;
            }
        }

        /// <summary>
        /// The board items for every issue that is still open.
        ///
        /// Resolved ones are left out rather than deleted. The record of what was raised and closed
        /// is the only evidence a coordinator has that the week's work happened, and a board that
        /// carried every issue ever raised would be unusable within a month.
        /// </summary>
        public IReadOnlyList<ClashItem> Open() =>
            _issues.Where(i => i.Status != FindingStatus.Resolved).Select(i => i.ToItem()).ToList();

        /// <summary>Serialise.</summary>
        public JsonValue ToJson() => JsonValue.Array(_issues.Select(i => i.ToJson()));

        /// <summary>Read a list back.</summary>
        /// <param name="json">The saved array.</param>
        public static ManualIssueList FromJson(JsonValue? json)
        {
            var list = new ManualIssueList();
            if (json == null || json.Kind != JsonKind.Array) return list;

            foreach (var item in json.Items)
            {
                var issue = ManualIssue.FromJson(item);

                // Counted rather than dropped in silence. An issue that vanished because its record
                // could not be parsed is somebody's coordination note disappearing without trace.
                if (issue != null) list.Put(issue);
                else list.Unreadable++;
            }

            return list;
        }
    }
}
