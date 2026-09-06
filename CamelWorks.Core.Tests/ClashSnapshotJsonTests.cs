using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashSnapshotJsonTests
    {
        private static readonly DateTimeOffset Taken =
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

        private static ElementKey Key(string guid) =>
            ElementKey.FromInstanceGuid(ElementKey.ScopeOf("model.nwc"), Guid.Parse(guid));

        private static ClashRecord Record(string a, string b, FindingStatus status,
                                          string? group = "Level 3 ducts", bool pinned = true) =>
            new ClashRecord(
                ClashKey.Create(Key(a), Key(b), 10, 20, 30),
                status, group, pinned, "MEP vs Structure");

        private static ClashSnapshot Full() =>
            new ClashSnapshot(
                new[]
                {
                    Record("11111111-1111-1111-1111-111111111111",
                           "22222222-2222-2222-2222-222222222222", FindingStatus.Approved),
                    Record("33333333-3333-3333-3333-333333333333",
                           "44444444-4444-4444-4444-444444444444", FindingStatus.Active,
                           group: null, pinned: false),
                },
                new Dictionary<string, GroupDecision>(StringComparer.Ordinal)
                {
                    ["Level 3 ducts"] = new GroupDecision(
                        "Mechanical", "High", new Resolution(ResolutionAction.Move, "Duct", ResolutionAxis.ZMinus, 0.15)),
                },
                "Week 12",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mechanical"] = 14, ["Structure"] = 3,
                },
                Taken);

        [Fact]
        public void Every_field_survives_a_round_trip()
        {
            var read = ClashSnapshotJson.FromJson(ClashSnapshotJson.ToJson(Full()));

            Assert.NotNull(read);
            Assert.Equal("Week 12", read!.Label);
            Assert.Equal(Taken, read.TakenUtc);
            Assert.Equal(14, read.OpenByParty["Mechanical"]);
            Assert.Equal(3, read.OpenByParty["Structure"]);

            var decision = read.Decisions["Level 3 ducts"];

            Assert.Equal("Mechanical", decision.AssignedTo);
            Assert.Equal("High", decision.Priority);
            Assert.True(decision.Instruction.IsStated);

            Assert.Equal(2, read.Records.Count);

            var first = read.Records[0];

            Assert.Equal(Full().Records[0].Key, first.Key);
            Assert.Equal(FindingStatus.Approved, first.Status);
            Assert.Equal("Level 3 ducts", first.GroupName);
            Assert.True(first.GroupWasPinned);
            Assert.Equal("MEP vs Structure", first.TestName);

            var second = read.Records[1];

            Assert.Null(second.GroupName);
            Assert.False(second.GroupWasPinned);
        }

        [Fact]
        public void The_written_shape_carries_its_version()
        {
            Assert.Equal(ClashSnapshotJson.Version,
                         (int)ClashSnapshotJson.ToJson(Full())["version"].AsLong());
        }

        [Fact]
        public void A_caller_can_leave_records_out()
        {
            var json = ClashSnapshotJson.ToJson(Full(), r => r.Status == FindingStatus.Approved);

            Assert.Single(ClashSnapshotJson.FromJson(json)!.Records);
        }

        [Fact]
        public void The_shape_written_before_this_file_existed_still_reads()
        {
            // Project files on disk hold a bare array of records. Failing to read one would lose
            // every carried-over decision the first time somebody opened the board after upgrading.
            var array = JsonValue.Array(Full().Records.Select(ClashSnapshotJson.RecordToJson));
            var read = ClashSnapshotJson.FromJson(array);

            Assert.NotNull(read);
            Assert.Equal(2, read!.Records.Count);
            Assert.Empty(read.OpenByParty);
            Assert.Null(read.TakenUtc);

            Assert.True(ClashSnapshotJson.IsSnapshot(array));
        }

        [Fact]
        public void Decisions_kept_elsewhere_win_over_the_ones_in_the_file()
        {
            // The board keeps decisions in their own key, because a decision is about the work
            // rather than about a run.
            var theirs = new Dictionary<string, GroupDecision>(StringComparer.Ordinal)
            {
                ["Level 3 ducts"] = new GroupDecision("Structure"),
            };

            var read = ClashSnapshotJson.FromJson(ClashSnapshotJson.ToJson(Full()), theirs);

            Assert.Equal("Structure", read!.Decisions["Level 3 ducts"].AssignedTo);
        }

        [Fact]
        public void Anything_else_is_not_a_snapshot()
        {
            Assert.Null(ClashSnapshotJson.FromJson(JsonValue.String("no")));
            Assert.False(ClashSnapshotJson.IsSnapshot(JsonValue.Object()));
            Assert.False(ClashSnapshotJson.IsSnapshot(null));
        }

        [Fact]
        public void A_record_naming_no_readable_key_is_dropped_rather_than_throwing()
        {
            Assert.Null(ClashSnapshotJson.RecordFromJson(JsonValue.Object().Set("key", "nonsense")));
        }
    }
}
