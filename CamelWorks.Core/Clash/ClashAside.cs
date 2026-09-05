using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// What a reviewer set on one clash that the host has no field for.
    ///
    /// A message rather than a store. This is what one markup said, read out of it and handed to the
    /// finding it belongs to. Nothing keeps a register of these: the finding is the record, and a
    /// priority read out of a returned file lives on the finding beside the status and the trail
    /// rather than in a second file keyed a second way.
    ///
    /// BCF carries a priority, a due date, a stage, a title and any tags the reviewer adds. A
    /// Navisworks clash result carries a status, an assignee, a group and comments, so when a
    /// returned file says "high priority, due the fifteenth" the host has no field for it. Dropping
    /// it loses what was written; putting it in a comment keeps the words but not the fact, so the
    /// board could not show a due date or say what is overdue. It goes onto the finding.
    /// </summary>
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

        /// <summary>
        /// Lay what the markup said onto the finding it belongs to.
        ///
        /// Field by field, and only where the markup said something: a reviewer's tool that has
        /// no stage picker sends no stage, and reading that as "clear the stage" would let the
        /// least capable tool in the round trip erase what the others set. A cleared due date is
        /// the one exception people do mean, so a markup that carries a topic and no date at all
        /// leaves the date alone rather than guessing - clearing one is done on the board.
        /// </summary>
        /// <param name="finding">The finding. Null is ignored, so callers need no guard.</param>
        public void ApplyTo(Finding? finding)
        {
            if (finding == null) return;

            if (Priority != null) finding.Priority = Priority;
            if (Stage != null) finding.Stage = Stage;
            if (Title != null) finding.Title = Title;
            if (DueTicks > 0) finding.DueDayNumber = Finding.DayNumberFor(Due!.Value);

            foreach (var label in Labels)
                if (!finding.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    finding.Labels.Add(label);

            if (!string.IsNullOrWhiteSpace(Source)) finding.Origin = Source;
            if (TopicGuid.Length > 0) finding.TopicGuid = TopicGuid;
        }

        /// <summary>
        /// What a finding would say if it were written back out as a markup, or null when it
        /// carries nothing the host had no field for.
        ///
        /// The other direction of <see cref="ApplyTo"/>, so an export sends a reviewer's priority
        /// back out rather than quietly resetting it on every round trip.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public static ClashAside? Of(Finding? finding)
        {
            if (finding == null || string.IsNullOrEmpty(finding.TopicGuid)) return null;

            var aside = new ClashAside(finding.TopicGuid!)
            {
                Priority = finding.Priority,
                Stage = finding.Stage,
                DueTicks = finding.Due?.Ticks ?? 0,
                Source = finding.Origin,
            };

            foreach (var label in finding.Labels) aside.Labels.Add(label);

            return aside.IsEmpty ? null : aside;
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
}
