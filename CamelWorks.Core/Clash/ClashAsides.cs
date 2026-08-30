using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>What a reviewer set on one clash that the host has no field for.</summary>
    public sealed class ClashAside
    {
        /// <summary>Name the topic this belongs to.</summary>
        /// <param name="topicGuid">The BCF topic id, which is stable across runs and exports.</param>
        public ClashAside(string topicGuid) =>
            TopicGuid = topicGuid ?? string.Empty;

        /// <summary>The topic. Also the key: it outlives a re-run, and a returned file carries it.</summary>
        public string TopicGuid { get; }

        /// <summary>Which test the clash sat in when this was recorded, when it was known.</summary>
        public string? TestId { get; set; }

        /// <summary>Which result within it.</summary>
        public string? ResultId { get; set; }

        /// <summary>The priority somebody set, or null.</summary>
        public string? Priority { get; set; }

        /// <summary>The stage, or null.</summary>
        public string? Stage { get; set; }

        /// <summary>The title they gave it, when it is not the one this export generated.</summary>
        public string? Title { get; set; }

        /// <summary>When it is due, as UTC ticks. Zero when nobody said.</summary>
        public long DueTicks { get; set; }

        /// <summary>Their own tags — every label that did not come out of this writer.</summary>
        public List<string> Labels { get; } = new List<string>();

        /// <summary>Which file it arrived in, so the board can say where an answer came from.</summary>
        public string? Source { get; set; }

        /// <summary>When it arrived, as UTC ticks.</summary>
        public long WhenTicks { get; set; }

        /// <summary>Whether this holds nothing worth keeping.</summary>
        public bool IsEmpty =>
            Priority == null && Stage == null && Title == null && DueTicks == 0 && Labels.Count == 0;

        /// <summary>The due date as a date, or null.</summary>
        public DateTime? Due =>
            DueTicks > 0 ? new DateTime(DueTicks, DateTimeKind.Utc) : (DateTime?)null;

        /// <summary>Whether it is past its due date.</summary>
        /// <param name="nowTicks">Now, as UTC ticks.</param>
        public bool IsOverdue(long nowTicks) => DueTicks > 0 && DueTicks < nowTicks;

        /// <summary>How this reads in a column - short, because it sits in a table.</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (Priority != null) parts.Add(Priority);

            if (Due != null) parts.Add("due " + Due.Value.ToString("d MMM", CultureInfo.InvariantCulture));

            if (Stage != null) parts.Add(Stage);
            if (Labels.Count > 0) parts.Add(string.Join(", ", Labels));

            return string.Join(" · ", parts);
        }

        /// <summary>The whole of it, for a message rather than a column.</summary>
        public string Explain()
        {
            var said = new List<string>();

            if (Priority != null) said.Add("priority " + Priority);
            if (Stage != null) said.Add("stage " + Stage);

            if (Due != null)
                said.Add("due " + Due.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (Title != null) said.Add("titled \"" + Title + "\"");
            if (Labels.Count > 0) said.Add("tagged " + string.Join(", ", Labels));

            return said.Count == 0 ? "nothing" : string.Join("; ", said);
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("topic", TopicGuid);

            void Text(string field, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) json.Set(field, value);
            }

            Text("test", TestId);
            Text("result", ResultId);
            Text("priority", Priority);
            Text("stage", Stage);
            Text("title", Title);
            Text("source", Source);

            if (DueTicks > 0) json.Set("due", DueTicks);
            if (WhenTicks > 0) json.Set("when", WhenTicks);

            if (Labels.Count > 0)
                json.Set("labels", JsonValue.Array(Labels.Select(JsonValue.String)));

            return json;
        }

        /// <summary>Read one back, or null when the record names no topic.</summary>
        /// <param name="json">One saved record.</param>
        public static ClashAside? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var topic = json["topic"].AsString();

            if (string.IsNullOrEmpty(topic)) return null;

            var aside = new ClashAside(topic!)
            {
                TestId = json["test"].AsString(),
                ResultId = json["result"].AsString(),
                Priority = json["priority"].AsString(),
                Stage = json["stage"].AsString(),
                Title = json["title"].AsString(),
                Source = json["source"].AsString(),
                DueTicks = json["due"].AsLong(),
                WhenTicks = json["when"].AsLong(),
            };

            foreach (var label in json["labels"].Items)
                if (label.AsString() is string named && named.Length > 0)
                    aside.Labels.Add(named);

            return aside;
        }
    }

    /// <summary>
    /// The clash properties Navisworks has nowhere to keep, kept here instead.
    ///
    /// <b>Why this exists.</b> BCF carries a priority, a due date, a stage, a title and whatever
    /// tags a reviewer likes. A Navisworks clash result carries a status, an assignee, a group and
    /// comments, and that is all. So when a returned file says "high priority, due the fifteenth",
    /// the host has no field to put it in - and the choice was between dropping it and mashing it
    /// into a comment as prose.
    ///
    /// Neither is right. Dropping it loses what somebody wrote; a comment keeps the words but not
    /// the fact, so the board cannot show a due date, sort by priority, or say what is overdue.
    /// The board should be able to, and the host not having a column is not a reason it cannot:
    /// this is what the project file is for.
    ///
    /// <b>Keyed on the BCF topic id</b>, which is a function of the two clashing elements. It
    /// survives a re-run, it is what a returned file brings back, and it is what the sync already
    /// matches on - so a record made from last week's markup still finds its clash today.
    ///
    /// <b>They go back out.</b> The next export writes them onto the topic again, so a reviewer's
    /// priority is not quietly reset every time the file makes a round trip.
    /// </summary>
    public sealed class ClashAsides
    {
        private readonly Dictionary<string, ClashAside> _asides =
            new Dictionary<string, ClashAside>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How many clashes carry one.</summary>
        public int Count => _asides.Count;

        /// <summary>Whether anything is held.</summary>
        public bool IsEmpty => _asides.Count == 0;

        /// <summary>Every record, in no particular order.</summary>
        public IReadOnlyCollection<ClashAside> All => _asides.Values;

        /// <summary>How many are past their due date.</summary>
        /// <param name="nowTicks">Now, as UTC ticks.</param>
        public int Overdue(long nowTicks) => _asides.Values.Count(a => a.IsOverdue(nowTicks));

        /// <summary>The record for a topic, creating it on first use.</summary>
        /// <param name="topicGuid">The topic.</param>
        public ClashAside For(string topicGuid)
        {
            var key = topicGuid ?? string.Empty;

            if (!_asides.TryGetValue(key, out var aside))
            {
                aside = new ClashAside(key);
                _asides[key] = aside;
            }

            return aside;
        }

        /// <summary>
        /// Put a record in, replacing whatever was there for that topic.
        ///
        /// The later markup wins outright rather than merging field by field: a reviewer who
        /// cleared a due date has said something, and a merge that kept the old one would refuse
        /// to hear it.
        /// </summary>
        /// <param name="aside">The record. One holding nothing removes the topic's entry.</param>
        public void Put(ClashAside? aside)
        {
            if (aside == null || aside.TopicGuid.Length == 0) return;

            if (aside.IsEmpty) _asides.Remove(aside.TopicGuid);
            else _asides[aside.TopicGuid] = aside;
        }

        /// <summary>The record for a topic, or null.</summary>
        /// <param name="topicGuid">The topic.</param>
        public ClashAside? Find(string? topicGuid) =>
            topicGuid != null && _asides.TryGetValue(topicGuid, out var aside) ? aside : null;

        /// <summary>Forget one.</summary>
        /// <param name="topicGuid">The topic.</param>
        public bool Release(string? topicGuid) =>
            topicGuid != null && _asides.Remove(topicGuid);

        /// <summary>Forget everything.</summary>
        public void Clear() => _asides.Clear();

        /// <summary>Drop the records that ended up holding nothing.</summary>
        public void Tidy()
        {
            foreach (var empty in _asides.Where(a => a.Value.IsEmpty).Select(a => a.Key).ToList())
                _asides.Remove(empty);
        }

        /// <summary>Serialise.</summary>
        public JsonValue ToJson()
        {
            Tidy();

            return JsonValue.Object()
                .Set("asides", JsonValue.Array(
                    _asides.Values.OrderBy(a => a.TopicGuid, StringComparer.Ordinal)
                                  .Select(a => a.ToJson())));
        }

        /// <summary>Read a register back. Never null: nothing held is the ordinary state.</summary>
        /// <param name="json">What was saved, or null.</param>
        public static ClashAsides FromJson(JsonValue? json)
        {
            var held = new ClashAsides();

            if (json == null || json.Kind != JsonKind.Object) return held;

            foreach (var record in json["asides"].Items)
            {
                var aside = ClashAside.FromJson(record);

                if (aside == null || aside.IsEmpty) continue;

                held._asides[aside.TopicGuid] = aside;
            }

            return held;
        }
    }
}
