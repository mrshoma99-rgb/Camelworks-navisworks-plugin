using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The board off the register: the item projection, the run comparison behind the
    /// changed-since line, and a board rebuilt on a seat with no clash engine.
    ///
    /// The board that draws these is in CamelWorks.UI, which has no test project and needs a
    /// licensed Navisworks to run at all. Everything the Coordinate tab does between the read and
    /// the rules is therefore in Core and asserted here; what is left in the UI is the three
    /// lookups the projection is given and the wiring that calls it.
    /// </summary>
    public class ClashProjectionTests
    {
        private const double Feet = 0.3048;

        private static readonly string ArchScope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");
        private static readonly string MepScope = ElementKey.ScopeOf(@"C:\p\MEP.nwc");

        private static ElementKey Arch(string name) =>
            ElementKey.FromTreePath(ArchScope, @"C:\p\ARCH.nwc", "L03", name);

        private static ElementKey Mep(string name) =>
            ElementKey.FromTreePath(MepScope, @"C:\p\MEP.nwc", "L03", name);

        private static ProjectStore Project() =>
            ProjectStore.Open(new InMemoryFileSystem(), @"C:\job\Tower.nwf", null);

        /// <summary>What the seat calls each model, by the scope its elements carry.</summary>
        private static readonly Dictionary<string, string> ModelNameByScope =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ArchScope] = "ARCH.nwc",
                [MepScope] = "MEP.nwc",
            };

        /// <summary>What discipline each model is, as the project profile answers it.</summary>
        private static readonly Dictionary<string, string> DisciplineByScope =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ArchScope] = "Architecture",
                [MepScope] = "Mechanical",
            };

        private static string? ModelOf(ElementKey key) =>
            ModelNameByScope.TryGetValue(key.ModelScope, out var name) ? name : null;

        private static string? DisciplineOf(ElementKey key) =>
            DisciplineByScope.TryGetValue(key.ModelScope, out var name) ? name : null;

        /// <summary>
        /// A level derived from the height, so an item whose coordinates came back wrong lands on
        /// the wrong level rather than passing on a constant.
        /// </summary>
        private static string? LevelAt(double metres) =>
            "L" + Math.Floor(metres).ToString(CultureInfo.InvariantCulture);

        private static ClashProjection Projection(double metresPerUnit = 1.0) =>
            new ClashProjection(metresPerUnit)
            {
                ModelName = ModelOf,
                Discipline = DisciplineOf,
                Level = LevelAt,
                Folder = _ => "Weekly",
            };

        // ---------------------------------------------------------------------------------
        // The projection
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The items the board's own host read used to build, so the projection can be held
        /// against them. Every coordinate is scaled to metres and the key is quantised in model
        /// units, which is what the board did before it stopped reading the host twice.
        /// </summary>
        private static IReadOnlyList<ClashItem> AsTheHostReadIt(IClashSource source, double scale)
        {
            var items = new List<ClashItem>();

            foreach (var test in source.Tests())
                foreach (var result in source.Results(test.Id))
                    items.Add(new ClashItem(result.Key(), test.Name)
                    {
                        Name = result.Name,
                        TestFolder = test.Folder,
                        X = result.PointX * scale,
                        Y = result.PointY * scale,
                        Z = result.PointZ * scale,
                        ModelA = ModelOf(result.A),
                        ModelB = ModelOf(result.B),
                        DisciplineA = DisciplineOf(result.A),
                        DisciplineB = DisciplineOf(result.B),
                        Level = LevelAt(result.PointZ * scale),
                    });

            return items;
        }

        [Fact]
        public void The_projection_produces_the_item_set_the_host_read_produces()
        {
            // On a foot document, so the two spines are genuinely different: the finding holds
            // metres and the key is quantised in model units. A projection that forgot the scale
            // would key every result three cells out and land it on the wrong level.
            var seat = new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Mep("Duct 1"), Arch("Beam 1"), 1.1, 2.2, 3.3)
                .Finding(Mep("Duct 2"), Arch("Beam 2"), 4.4, 5.5, 6.6)
                .With("L04 MEP v STR", "Weekly", "t2")
                .Finding(Mep("Duct 3"), Arch("Beam 3"), 7.7, 8.8, 9.9);

            var read = new ClashCheck(seat, Feet).Read(null, null);
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, read.Findings, 1, null, read.Scope);

            var projected = Projection(Feet).Items(register.Present.ToList());
            var host = AsTheHostReadIt(seat, Feet);

            Assert.Equal(host.Count, projected.Count);

            Assert.Equal(host.Select(one => one.Key.ToString()).OrderBy(one => one, StringComparer.Ordinal),
                         projected.Select(one => one.Key.ToString()).OrderBy(one => one, StringComparer.Ordinal));

            var mine = projected.ToDictionary(one => one.Key.ToString(), StringComparer.Ordinal);

            foreach (var theirs in host)
            {
                var ours = mine[theirs.Key.ToString()];

                Assert.Equal(theirs.Level, ours.Level);
                Assert.Equal(theirs.DisciplineA, ours.DisciplineA);
                Assert.Equal(theirs.DisciplineB, ours.DisciplineB);
                Assert.Equal(theirs.ModelA, ours.ModelA);
                Assert.Equal(theirs.ModelB, ours.ModelB);
                Assert.Equal(theirs.X, ours.X, 9);
                Assert.Equal(theirs.Y, ours.Y, 9);
                Assert.Equal(theirs.Z, ours.Z, 9);
                Assert.Equal(theirs.TestName, ours.TestName);
                Assert.Equal(theirs.TestFolder, ours.TestFolder);
            }

            // And the item knows which finding it came from, which is what the changed-since line,
            // the report's "back again" count and the write-back all match on.
            Assert.All(projected, one => Assert.NotNull(one.FindingId));
        }

        [Fact]
        public void A_finding_the_adapter_could_not_resolve_both_sides_of_projects_to_nothing()
        {
            // A result with one participant has no clash key, and the rules, the grouping and the
            // comparison all index on one. Left out rather than given an empty key.
            var finding = Finding.Create(FindingSource.Clash, "L03 MEP v STR", "Clash1",
                                         new[] { Mep("Duct 1") }, "0,0,0");

            Assert.Null(Projection().Item(finding));
            Assert.Empty(Projection().Items(new[] { finding }));
        }

        // ---------------------------------------------------------------------------------
        // The run comparison
        // ---------------------------------------------------------------------------------

        /// <summary>Three results, the second of which can be moved.</summary>
        /// <param name="secondX">Where the second result is.</param>
        /// <param name="thirdPair">What the third result is between.</param>
        private static FakeClashSource Week(double secondX, string thirdPair) =>
            new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Mep("Duct 1"), Arch("Beam 1"), 1, 2, 3)
                .Finding(Mep("Duct 2"), Arch("Beam 2"), secondX, 5, 6)
                .Finding(Mep("Duct " + thirdPair), Arch("Beam " + thirdPair), 7, 8, 9);

        [Fact]
        public void The_comparison_counts_what_came_went_and_drifted_across_two_runs()
        {
            // The counts the snapshot's own carry-over reported for the same shape of run, which
            // is what the journal and the aliases have to reproduce: one result exactly where it
            // was, one a cell over because the model moved, one gone, one new.
            var register = new FindingRegister();
            var store = Project();

            var first = new ClashCheck(Week(4, "3"));
            var week1 = first.Read(null, null);

            register.Observe(FindingSource.Clash, week1.Findings, 1, null, week1.Scope);

            foreach (var run in week1.Runs)
                RunJournal.Of(first.Key, run.Id, 1, register.Present.Where(week1.Scope).ToList())
                          .Write(store);

            // 4.3 is one cell over from 4 at the default quarter-metre cell, and "Beam 4" is a
            // different pair from "Beam 3", so the third result is one gone and one new.
            var second = new ClashCheck(Week(4.3, "4"));
            var week2 = second.Read(null, null);

            var before = RunJournal.Together(
                second.Key, week2.Runs.Select(run => RunJournal.Current(store, second.Key, run.Id)));

            Assert.NotNull(before);

            var intake = register.Observe(FindingSource.Clash, week2.Findings, 2, null, week2.Scope);
            var delta = CheckDelta.Between(before, intake, register, week2.Scope, second.Aliases);

            Assert.False(delta.NoPreviousRun);
            Assert.Equal(2, delta.Persisting.Count);
            Assert.Single(delta.New);
            Assert.Single(delta.Gone);
            Assert.Equal(1, delta.MatchedOnAlias);

            // The drift is said out loud, because a large count means the model has moved and every
            // carried status is a weaker claim than it looks.
            Assert.Contains("matched on a neighbouring position", delta.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_run_that_has_never_run_reports_no_previous_run_rather_than_a_wholesale_change()
        {
            var store = Project();
            var check = new ClashCheck(Week(4, "3"));
            var read = check.Read(null, null);
            var register = new FindingRegister();

            var before = RunJournal.Together(
                check.Key, read.Runs.Select(run => RunJournal.Current(store, check.Key, run.Id)));

            Assert.Null(before);

            var intake = register.Observe(FindingSource.Clash, read.Findings, 1, null, read.Scope);
            var delta = CheckDelta.Between(before, intake, register, read.Scope, check.Aliases);

            Assert.True(delta.NoPreviousRun);
            Assert.Empty(delta.New);
            Assert.Empty(delta.Gone);
            Assert.Empty(delta.Persisting);
            Assert.Contains("no previous run recorded", delta.Explain(),
                            StringComparison.OrdinalIgnoreCase);

            // Three results and none of them counted as new: a first run is not a change, and the
            // sentence has to say that rather than reporting the whole model.
            Assert.Equal(3, register.Present.Count());
        }

        // ---------------------------------------------------------------------------------
        // Hydration, on a seat with no clash engine
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_register_rebuilds_a_board_carrying_its_suppressed_results_and_its_hand_group()
        {
            // What a Manage seat recorded last week.
            var seat = new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Mep("Duct 1"), Arch("Beam 1"), 1, 2, 3)
                .Finding(Mep("Duct 2"), Arch("Beam 2"), 4, 5, 6)
                .Finding(Arch("Wall 1"), Arch("Beam 3"), 7, 8, 9);

            var register = new FindingRegister();
            var read = new ClashCheck(seat).Read(null, null);

            register.Observe(FindingSource.Clash, read.Findings, 1, null, read.Scope);

            var projection = Projection();
            var items = projection.Items(register.Present.ToList());

            // Two elements in one model is usually a modelling artefact, and the third result is
            // the only one of the three that is.
            var rules = new ClashPipelineOptions
            {
                Filters = new[] { new FilterRule("Same model", ClashPredicates.SameModel(), suppress: true) },
                PinnedGroups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["Riser 3"] = new[] { items[0].Key.ToString() },
                },
            };

            var manage = ClashPipeline.Run(items, rules);

            Assert.Single(manage.Suppressed);
            Assert.Contains(manage.Groups, group => group.Name == "Riser 3" && group.IsPinned);

            ClashFindings.Recorded(manage, register);

            // A different seat: no clash source at all, nothing but the findings.
            var hydrated = Projection().Items(register.Present.ToList());

            Assert.Equal(3, hydrated.Count);

            var carried = Assert.Single(hydrated.Where(one => one.CarriedGroup != null));
            Assert.Equal("Riser 3", carried.CarriedGroup);
            Assert.Equal(items[0].Key, carried.Key);

            // The suppressed result is still on the board's input. Losing it would make it arrive
            // as new the moment somebody turns the rule off.
            var wasSuppressed = Assert.Single(hydrated.Where(one => one.SuppressedBy != null));
            Assert.Equal("Same model", wasSuppressed.SuppressedBy);

            var simulate = ClashPipeline.Run(hydrated, new ClashPipelineOptions { Filters = rules.Filters });

            Assert.Single(simulate.Suppressed);
            Assert.Equal(wasSuppressed.Key, simulate.Suppressed[0].Key);

            // The hand-made group re-forms without anybody pinning it again on this seat: the pin
            // is in the project file on the seat that made it, and the fact is what travels.
            var group = Assert.Single(simulate.Groups.Where(one => one.Name == "Riser 3"));

            Assert.True(group.IsPinned);
            Assert.Equal(carried.Key, Assert.Single(group.Items).Key);
        }

        [Fact]
        public void A_result_the_rules_stop_suppressing_stops_naming_the_rule_that_did()
        {
            // The fact is written or removed on every board, never left alone: the register merges
            // facts additively and removes none, so a rule somebody switched off would go on
            // taking the result off every future board.
            var register = new FindingRegister();
            var read = new ClashCheck(new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Arch("Wall 1"), Arch("Beam 3"), 7, 8, 9)).Read(null, null);

            register.Observe(FindingSource.Clash, read.Findings, 1, null, read.Scope);

            var filtered = new ClashPipelineOptions
            {
                Filters = new[] { new FilterRule("Same model", ClashPredicates.SameModel(), suppress: true) },
            };

            ClashFindings.Recorded(ClashPipeline.Run(Projection().Items(register.Present.ToList()), filtered),
                                   register);

            Assert.All(register.Present,
                       finding => Assert.True(finding.Facts.ContainsKey(ClashFindings.SuppressedBy)));

            ClashFindings.Recorded(ClashPipeline.Run(Projection().Items(register.Present.ToList()),
                                                     new ClashPipelineOptions()),
                                   register);

            Assert.All(register.Present,
                       finding => Assert.False(finding.Facts.ContainsKey(ClashFindings.SuppressedBy)));
        }
    }
}
