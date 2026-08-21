using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashHydrationTests
    {
        private static readonly string Scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");

        private static ElementKey K(string name) =>
            ElementKey.FromTreePath(Scope, @"C:\p\ARCH.nwc", name);

        private static ClashKey Key(string a, string b, double x = 0, double y = 0, double z = 0) =>
            ClashKey.Create(K(a), K(b), x, y, z);

        [Fact]
        public void Nothing_recorded_rebuilds_to_nothing_and_says_so()
        {
            var board = ClashHydration.From(null, 1.0);

            Assert.True(board.IsEmpty);
            Assert.Equal(0, board.Records);
            Assert.Contains("no previous run is recorded", board.Explain());
        }

        [Fact]
        public void Identity_status_and_hand_grouping_survive_exactly()
        {
            var key = Key("wall", "duct", 4, 8, 3);

            var snapshot = new ClashSnapshot(new[]
            {
                new ClashRecord(key, FindingStatus.Approved, "Level 2 - MEP through structure", true, "L02 ARC vs MEP"),
            });

            var item = ClashHydration.From(snapshot, 1.0).Items.Single();

            Assert.Equal(key, item.Key);
            Assert.Equal(FindingStatus.Approved, item.Status);
            Assert.Equal("Level 2 - MEP through structure", item.CarriedGroup);
            Assert.Equal("L02 ARC vs MEP", item.TestName);
        }

        [Fact]
        public void A_derived_group_name_is_not_carried()
        {
            // Only a group somebody pinned by hand carries. A derived name is re-derived by the
            // rules, and carrying it would freeze the board against its own rule stack.
            var snapshot = new ClashSnapshot(new[]
            {
                new ClashRecord(Key("a", "b"), FindingStatus.Active, "Grid C-4", false, "test"),
            });

            Assert.Null(ClashHydration.From(snapshot, 1.0).Items.Single().CarriedGroup);
        }

        [Fact]
        public void The_position_comes_back_as_the_centre_of_its_cell()
        {
            // Quantise rounds to the nearest cell, so the index times the size is the centre.
            var snapshot = new ClashSnapshot(new[]
            {
                new ClashRecord(Key("a", "b", 4.03, 8.0, -2.0), FindingStatus.Active),
            });

            var item = ClashHydration.From(snapshot, 1.0, cell: 0.25).Items.Single();

            Assert.Equal(4.0, item.X, 6);
            Assert.Equal(8.0, item.Y, 6);
            Assert.Equal(-2.0, item.Z, 6);
        }

        [Fact]
        public void A_millimetre_model_comes_back_in_metres()
        {
            // The cell lives in host units because that is what the clash engine reported and what
            // the key was made from. Everything above the seam is metres, so the scale has to come
            // back in on the way out or a millimetre job lands a thousand times off.
            var snapshot = new ClashSnapshot(new[]
            {
                new ClashRecord(Key("a", "b", 4000, 0, 0), FindingStatus.Active),
            });

            var board = ClashHydration.From(snapshot, metresPerUnit: 0.001, cell: 0.25);

            Assert.Equal(4.0, board.Items.Single().X, 6);
            Assert.Equal(0.000125, board.PrecisionMetres, 9);
        }

        [Fact]
        public void How_exact_the_positions_are_is_stated_not_implied()
        {
            var snapshot = new ClashSnapshot(new[] { new ClashRecord(Key("a", "b"), FindingStatus.Active) });
            var board = ClashHydration.From(snapshot, 1.0, cell: 0.25);

            Assert.Equal(0.125, board.PrecisionMetres, 9);
            Assert.Contains("within 125 mm", board.Explain());
        }

        [Fact]
        public void A_record_that_cannot_be_read_is_counted_not_dropped_quietly()
        {
            var snapshot = new ClashSnapshot(new[]
            {
                new ClashRecord(Key("a", "b"), FindingStatus.Active),
                new ClashRecord(default, FindingStatus.Active),
            });

            var board = ClashHydration.From(snapshot, 1.0);

            Assert.Single(board.Items);
            Assert.Equal(2, board.Records);
            Assert.Equal(1, board.Unusable);
            Assert.Contains("1 records could not be read", board.Explain());
        }

        [Fact]
        public void A_result_with_no_test_name_still_reads_as_something()
        {
            var snapshot = new ClashSnapshot(new[] { new ClashRecord(Key("a", "b"), FindingStatus.Active) });

            Assert.Equal(ClashHydration.UnknownTest, ClashHydration.From(snapshot, 1.0).Items.Single().TestName);
        }

        [Fact]
        public void The_rebuilt_board_still_runs_through_the_rules()
        {
            // The point of rebuilding: a Simulate seat gets a board it can group, assign, report on
            // and issue as BCF, from a run a Manage seat made.
            var snapshot = new ClashSnapshot(Enumerable.Range(0, 6)
                .Select(n => new ClashRecord(Key("wall" + n, "duct" + n, n, 0, 0), FindingStatus.Active, null, false, "L02"))
                .ToList());

            var items = ClashHydration.From(snapshot, 1.0).Items;
            var board = ClashPipeline.Run(items, new ClashPipelineOptions());

            Assert.Equal(6, board.Funnel.Input);
            Assert.NotEmpty(board.Groups);
        }
    }
}
