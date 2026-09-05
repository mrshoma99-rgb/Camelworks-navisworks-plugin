using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// A finding on disk.
    ///
    /// Separate from the record on purpose. <see cref="Finding"/> is constructed through
    /// <see cref="Finding.Create"/> so its identity is always derived rather than assigned. Reading
    /// one back has to rebuild it the same way — from its source, rule, elements and discriminator —
    /// rather than restoring a stored id, because a stored id that no longer matches what the record
    /// says would be a finding that stops matching its own re-run.
    ///
    /// So the file keeps the discriminator, and the id is recomputed on load and checked against
    /// what was written. A mismatch means the derivation changed between versions; the file's
    /// findings are then re-identified rather than trusted, which loses the link to an issue but
    /// never silently mismatches one.
    /// </summary>
    public static class FindingJson
    {
        /// <summary>The shape's version, so a later change can be told apart.</summary>
        public const int Version = 1;

        /// <summary>Write one finding, and its children.</summary>
        /// <param name="finding">The finding.</param>
        public static JsonValue Write(Finding finding)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));

            var json = JsonValue.Object()
                .Set("id", finding.Id)
                .Set("source", finding.Source.ToString())
                .Set("rule", finding.Rule)
                .Set("title", finding.Title)
                .Set("status", FindingStatusLattice.ToName(finding.Status));

            void Text(string field, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) json.Set(field, value);
            }

            Text("discriminator", finding.Discriminator);
            Text("detail", finding.Detail);
            Text("assigned", finding.AssignedTo);
            Text("priority", finding.Priority);
            Text("stage", finding.Stage);
            Text("group", finding.Group);
            Text("origin", finding.Origin);
            Text("hostProblem", finding.HostProblem);
            Text("topic", finding.TopicGuid);

            if (finding.DueDayNumber != null) json.Set("due", finding.DueDayNumber.Value);
            if (finding.LastSeenTicks > 0) json.Set("seen", finding.LastSeenTicks);
            if (!finding.IsPresent) json.Set("gone", true);
            if (finding.HostHasIt) json.Set("inHost", true);

            if (finding.X != null) json.Set("x", finding.X.Value);
            if (finding.Y != null) json.Set("y", finding.Y.Value);
            if (finding.Z != null) json.Set("z", finding.Z.Value);

            if (finding.Elements.Count > 0)
                json.Set("elements", JsonValue.Array(
                    finding.Elements.Select(e => JsonValue.String(e.ToString()))));

            if (finding.Pending.Count > 0)
                json.Set("pending", JsonValue.Array(finding.Pending.Select(JsonValue.String)));

            if (finding.Labels.Count > 0)
                json.Set("labels", JsonValue.Array(finding.Labels.Select(JsonValue.String)));

            if (finding.Comments.Count > 0)
                json.Set("comments", JsonValue.Array(finding.Comments.Select(c =>
                    JsonValue.Object()
                        .Set("author", c.Author)
                        .Set("when", c.WhenTicks)
                        .Set("body", c.Body)
                        .Set("inHost", c.InHost))));

            if (finding.Facts.Count > 0)
            {
                var facts = JsonValue.Object();

                foreach (var pair in finding.Facts.OrderBy(f => f.Key, StringComparer.Ordinal))
                    facts.Set(pair.Key, pair.Value);

                json.Set("facts", facts);
            }

            if (finding.Children.Count > 0)
                json.Set("children", JsonValue.Array(finding.Children.Select(Write)));

            return json;
        }

        /// <summary>
        /// Read one back, or null when the record does not say enough to rebuild it.
        /// </summary>
        /// <param name="json">One saved finding.</param>
        /// <param name="renamed">
        /// Set when the id had to be recomputed to something other than what was written — the
        /// derivation changed between versions. The caller reports it rather than hiding it.
        /// </param>
        public static Finding? Read(JsonValue? json, out bool renamed)
        {
            renamed = false;

            if (json == null || json.Kind != JsonKind.Object) return null;

            var rule = json["rule"].AsString();
            var title = json["title"].AsString();

            if (string.IsNullOrEmpty(rule) || string.IsNullOrEmpty(title)) return null;

            if (!Enum.TryParse<FindingSource>(json["source"].AsString(), false, out var source))
                return null;

            var elements = new List<ElementKey>();

            foreach (var element in json["elements"].Items)
                if (ElementKey.TryParse(element.AsString(), out var key) && !key.IsEmpty)
                    elements.Add(key);

            var discriminator = json["discriminator"].AsString();

            // Derived again rather than restored. See the type remarks.
            var finding = Finding.Create(source, rule!, title!, elements, discriminator);

            var written = json["id"].AsString();

            if (written != null && !string.Equals(written, finding.Id, StringComparison.Ordinal))
                renamed = true;

            finding.Status = FindingStatusLattice.TryParse(json["status"].AsString(), out var status)
                ? status
                : FindingStatus.New;
            finding.Detail = json["detail"].AsString();
            finding.AssignedTo = json["assigned"].AsString();
            finding.Priority = json["priority"].AsString();
            finding.Stage = json["stage"].AsString();
            finding.Group = json["group"].AsString();
            finding.Origin = json["origin"].AsString();
            finding.HostProblem = json["hostProblem"].AsString();
            finding.TopicGuid = json["topic"].AsString();
            finding.LastSeenTicks = json["seen"].AsLong();
            finding.IsPresent = !json["gone"].AsBool();
            finding.HostHasIt = json["inHost"].AsBool();

            if (json.Has("due")) finding.DueDayNumber = (int)json["due"].AsLong();
            if (json.Has("x")) finding.X = json["x"].AsDouble();
            if (json.Has("y")) finding.Y = json["y"].AsDouble();
            if (json.Has("z")) finding.Z = json["z"].AsDouble();

            foreach (var label in json["labels"].Items)
                if (label.AsString() is string named && named.Length > 0)
                    finding.Labels.Add(named);

            foreach (var field in json["pending"].Items)
                if (field.AsString() is string waiting && waiting.Length > 0)
                    finding.Pending.Add(waiting);

            foreach (var comment in json["comments"].Items)
                if (comment["body"].AsString() is string body)
                    finding.Comments.Add(new FindingComment(comment["author"].AsString(),
                                                            comment["when"].AsLong(), body)
                    {
                        InHost = comment["inHost"].AsBool(),
                    });

            var facts = json["facts"];

            foreach (var field in facts.Keys) finding.Facts[field] = facts[field].AsString();

            foreach (var record in json["children"].Items)
            {
                var child = Read(record, out var childRenamed);

                renamed |= childRenamed;

                // A child restored with triage state of its own would be refused by AddChild,
                // which is right: children inherit. Cleared rather than dropped, because the
                // location is still worth keeping.
                if (child == null) continue;

                child.Status = FindingStatus.New;
                child.AssignedTo = null;
                child.DueDayNumber = null;

                finding.AddChild(child);
            }

            return finding;
        }
    }
}
