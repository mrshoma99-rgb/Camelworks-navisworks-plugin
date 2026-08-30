using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>One result's decisions, held here because the document would not take them.</summary>
    public sealed class HeldEdit
    {
        /// <summary>Name the result this is about.</summary>
        /// <param name="testId">The test the result belongs to.</param>
        /// <param name="resultId">The result's own id within that test.</param>
        public HeldEdit(string testId, string resultId)
        {
            TestId = testId ?? string.Empty;
            ResultId = resultId ?? string.Empty;
        }

        /// <summary>The test.</summary>
        public string TestId { get; }

        /// <summary>The result within it.</summary>
        public string ResultId { get; }

        /// <summary>The status somebody set, or null when they did not set one.</summary>
        public string? Status { get; set; }

        /// <summary>Who it was given to, or null when nobody changed it.</summary>
        public string? Assigned { get; set; }

        /// <summary>The group it was put in, or null.</summary>
        public string? Group { get; set; }

        /// <summary>The name it was given, or null.</summary>
        public string? Name { get; set; }

        /// <summary>The comments written here, oldest first.</summary>
        public List<ClashComment> Comments { get; } = new List<ClashComment>();

        /// <summary>Whether this holds nothing worth keeping.</summary>
        public bool IsEmpty =>
            Status == null && Assigned == null && Group == null && Name == null && Comments.Count == 0;

        /// <summary>How this reads in a list of what is waiting.</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (Status != null) parts.Add("status " + Status);
            if (Assigned != null) parts.Add(Assigned.Length == 0 ? "unassigned" : "to " + Assigned);
            if (Group != null) parts.Add("group " + Group);
            if (Name != null) parts.Add("named " + Name);

            if (Comments.Count > 0)
                parts.Add(Comments.Count.ToString(CultureInfo.InvariantCulture)
                          + (Comments.Count == 1 ? " comment" : " comments"));

            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("test", TestId)
                .Set("result", ResultId);

            if (Status != null) json.Set("status", Status);
            if (Assigned != null) json.Set("assigned", Assigned);
            if (Group != null) json.Set("group", Group);
            if (Name != null) json.Set("name", Name);

            if (Comments.Count > 0)
                json.Set("comments", JsonValue.Array(Comments.Select(c =>
                    JsonValue.Object()
                        .Set("author", c.Author)
                        .Set("when", c.WhenTicks)
                        .Set("body", c.Body))));

            return json;
        }

        /// <summary>Read one back, or null when the record names no result.</summary>
        /// <param name="json">One saved edit.</param>
        public static HeldEdit? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var test = json["test"].AsString();
            var result = json["result"].AsString();

            if (string.IsNullOrEmpty(test) || string.IsNullOrEmpty(result)) return null;

            var edit = new HeldEdit(test!, result!)
            {
                Status = json["status"].AsString(),
                Assigned = json["assigned"].AsString(),
                Group = json["group"].AsString(),
                Name = json["name"].AsString(),
            };

            foreach (var comment in json["comments"].Items)
            {
                var body = comment["body"].AsString();
                if (body == null) continue;

                edit.Comments.Add(new ClashComment(comment["author"].AsString() ?? "unknown",
                                                   comment["when"].AsLong(), body));
            }

            return edit;
        }
    }

    /// <summary>
    /// The decisions a read-only document would not take, kept with the project until they can be
    /// written somewhere that will have them.
    ///
    /// <b>Why this exists.</b> Clash results in a published NWD are read-only: the host refuses
    /// every status, assignment, comment and group a coordinator sets. That is most of a clash
    /// manager, and being handed a read-only board is not an unusual day - the NWD is what gets
    /// issued, and it is what most people open. Refusing the edit and losing it is the one
    /// behaviour that makes the tool pointless, so the edit is kept here instead.
    ///
    /// <b>What happens to them.</b> They are applied to the board on screen, so the week's work
    /// reads correctly; they survive re-reads and re-opens, because they live in the project file
    /// beside the document; and they go out as clash XML, which is the file that carries them into
    /// the NWF the results actually belong to. Nothing here pretends the host took them: the bench
    /// says how many are held and what they are waiting for.
    ///
    /// Keyed on test and result id together, which is how the host identifies a result and how the
    /// XML round trip matches one back.
    /// </summary>
    public sealed class HeldEdits
    {
        // A unit separator: it cannot appear in a host id, so no pair of ids can collide with
        // another pair by running into each other.
        private const char Separator = (char)31;

        private readonly Dictionary<string, HeldEdit> _edits =
            new Dictionary<string, HeldEdit>(StringComparer.Ordinal);

        /// <summary>How many results carry a held edit.</summary>
        public int Count => _edits.Count;

        /// <summary>Whether anything is held.</summary>
        public bool IsEmpty => _edits.Count == 0;

        /// <summary>Every held edit, in no particular order.</summary>
        public IReadOnlyCollection<HeldEdit> All => _edits.Values;

        /// <summary>The edit held for a result, creating it on first use.</summary>
        /// <param name="testId">The test.</param>
        /// <param name="resultId">The result within it.</param>
        public HeldEdit For(string testId, string resultId)
        {
            var key = Key(testId, resultId);

            if (!_edits.TryGetValue(key, out var edit))
            {
                edit = new HeldEdit(testId ?? string.Empty, resultId ?? string.Empty);
                _edits[key] = edit;
            }

            return edit;
        }

        /// <summary>The edit held for a result, or null when there is none.</summary>
        /// <param name="testId">The test.</param>
        /// <param name="resultId">The result within it.</param>
        public HeldEdit? Find(string? testId, string? resultId) =>
            testId == null || resultId == null
                ? null
                : _edits.TryGetValue(Key(testId, resultId), out var edit) ? edit : null;

        /// <summary>Forget what is held for a result - it has been written somewhere real.</summary>
        /// <param name="testId">The test.</param>
        /// <param name="resultId">The result within it.</param>
        public bool Release(string? testId, string? resultId) =>
            testId != null && resultId != null && _edits.Remove(Key(testId, resultId));

        /// <summary>Forget everything.</summary>
        public void Clear() => _edits.Clear();

        /// <summary>Drop the records that ended up holding nothing.</summary>
        public void Tidy()
        {
            foreach (var empty in _edits.Where(e => e.Value.IsEmpty).Select(e => e.Key).ToList())
                _edits.Remove(empty);
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            Tidy();

            return JsonValue.Object()
                .Set("edits", JsonValue.Array(_edits.Values.Select(e => e.ToJson())));
        }

        /// <summary>Read a register back. Never null: nothing held is the ordinary state.</summary>
        /// <param name="json">What was saved, or null.</param>
        public static HeldEdits FromJson(JsonValue? json)
        {
            var held = new HeldEdits();

            if (json == null || json.Kind != JsonKind.Object) return held;

            foreach (var record in json["edits"].Items)
            {
                var edit = HeldEdit.FromJson(record);

                if (edit == null || edit.IsEmpty) continue;

                held._edits[Key(edit.TestId, edit.ResultId)] = edit;
            }

            return held;
        }

        private static string Key(string? testId, string? resultId) =>
            (testId ?? string.Empty) + Separator + (resultId ?? string.Empty);
    }
}
