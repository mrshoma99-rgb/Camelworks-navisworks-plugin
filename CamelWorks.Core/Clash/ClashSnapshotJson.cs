using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// The snapshot's file format.
    ///
    /// This is the record that decides whether last month's approvals survive a re-run, and it was
    /// the one persisted type built inline in the UI with no round-trip test. It sits beside
    /// <see cref="FindingJson"/> for the same reason that does: the format is a contract with every
    /// project file already on disk, and a contract belongs where it can be tested.
    ///
    /// One shape. A <see cref="Version"/> object carries the records, the per-party open counts,
    /// the label and the time in one value, so moving a snapshot to the undo slot moves all of it.
    /// </summary>
    public static class ClashSnapshotJson
    {
        /// <summary>The format version this writes.</summary>
        public const int Version = 1;

        /// <summary>
        /// Write a snapshot.
        /// </summary>
        /// <param name="snapshot">The snapshot.</param>
        /// <param name="keep">
        /// Which records to write, or null for all of them. The board uses this to leave out
        /// hand-raised issues: the snapshot is what the next run's carry-over compares the engine's
        /// results against, and a record the engine can never produce reads as resolved every run.
        /// </param>
        public static JsonValue ToJson(ClashSnapshot snapshot, Func<ClashRecord, bool>? keep = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var json = JsonValue.Object().Set("version", Version);

            json.Set("records", JsonValue.Array(snapshot.Records
                .Where(r => keep == null || keep(r))
                .Select(RecordToJson)));

            if (snapshot.OpenByParty.Count > 0)
            {
                var parties = JsonValue.Object();

                foreach (var entry in snapshot.OpenByParty) parties.Set(entry.Key, (long)entry.Value);

                json.Set("parties", parties);
            }

            if (snapshot.Decisions.Count > 0)
            {
                var decisions = JsonValue.Object();

                foreach (var entry in snapshot.Decisions)
                    decisions.Set(entry.Key, DecisionToJson(entry.Value));

                json.Set("decisions", decisions);
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Label)) json.Set("label", snapshot.Label);
            if (snapshot.TakenUtc != null) json.Set("taken", snapshot.TakenUtc.Value.ToUnixTimeSeconds());

            return json;
        }

        /// <summary>
        /// Read a snapshot, or null when the value is not one.
        /// </summary>
        /// <param name="json">The stored value.</param>
        /// <param name="decisions">
        /// Decisions to use instead of the ones in the file. The board keeps them in their own key,
        /// because a decision is about the work rather than about a run.
        /// </param>
        public static ClashSnapshot? FromJson(JsonValue? json,
            IReadOnlyDictionary<string, GroupDecision>? decisions = null)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var parties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var saved = json["parties"];

            foreach (var party in saved.Keys) parties[party] = (int)saved[party].AsLong();

            var taken = json["taken"];

            return new ClashSnapshot(
                Records(json["records"]),
                decisions ?? Decisions(json["decisions"]),
                json["label"].AsString(),
                parties,
                taken.Kind == JsonKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(taken.AsLong())
                    : (DateTimeOffset?)null);
        }

        /// <summary>True when the value holds a snapshot.</summary>
        /// <param name="json">The stored value.</param>
        public static bool IsSnapshot(JsonValue? json) =>
            json != null && json.Kind == JsonKind.Object
            && json["records"].Kind == JsonKind.Array;

        private static IReadOnlyList<ClashRecord> Records(JsonValue json)
        {
            var records = new List<ClashRecord>();

            if (json.Kind != JsonKind.Array) return records;

            foreach (var entry in json.Items)
            {
                var record = RecordFromJson(entry);

                if (record != null) records.Add(record);
            }

            return records;
        }

        private static IReadOnlyDictionary<string, GroupDecision> Decisions(JsonValue json)
        {
            var decisions = new Dictionary<string, GroupDecision>(StringComparer.Ordinal);

            foreach (var name in json.Keys)
                decisions[name] = new GroupDecision(json[name]["party"].AsString(),
                                                   json[name]["priority"].AsString(),
                                                   Resolution.FromJson(json[name]["instruction"]));

            return decisions;
        }

        private static JsonValue DecisionToJson(GroupDecision decision)
        {
            var json = JsonValue.Object();

            if (decision.AssignedTo != null) json.Set("party", decision.AssignedTo);
            if (decision.Priority != null) json.Set("priority", decision.Priority);
            if (decision.Instruction.IsStated) json.Set("instruction", decision.Instruction.ToJson());

            return json;
        }

        /// <summary>Write one record.</summary>
        /// <param name="record">The record.</param>
        public static JsonValue RecordToJson(ClashRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            var json = JsonValue.Object()
                .Set("key", record.Key.ToString())
                .Set("status", record.Status.ToString());

            if (record.GroupName != null) json.Set("group", record.GroupName);
            if (record.GroupWasPinned) json.Set("pinned", "yes");
            if (record.TestName != null) json.Set("test", record.TestName);

            return json;
        }

        /// <summary>Read one record, or null when it names no key this reader can parse.</summary>
        /// <param name="json">The stored record.</param>
        public static ClashRecord? RecordFromJson(JsonValue? json)
        {
            if (json == null || !ClashKey.TryParse(json["key"].AsString(), out var key)) return null;

            var status = Enum.TryParse<FindingStatus>(json["status"].AsString(), out var parsed)
                ? parsed
                : FindingStatus.New;

            return new ClashRecord(key, status, json["group"].AsString(),
                                   json["pinned"].AsString() == "yes", json["test"].AsString());
        }
    }
}
