using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Clearance as a check: named runs, kept findings, and the identity that keeps two runs apart.
    ///
    /// The behaviour under test is the one the tab was restructured for. Clearance used to have a
    /// single unnamed run, so a finding's identity did not need to say which run reported it. With
    /// named runs it does: two runs over overlapping geometry meet at the same place, and without
    /// the run in the identity they collide on one finding, one decision and one status.
    /// </summary>
    public class ClearanceCheckTests
    {
        /// <summary>
        /// A floor with two ducts over it, both too low, four metres apart in plan.
        ///
        /// Two ducts rather than one so a run scoped to part of the floor still has something to
        /// report, and far enough apart that the position cell tells the two breaches apart.
        /// </summary>
        private static FakeDocument Model()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            document.AddItem(model, "Slab L3", "Floors", "Concrete",
                             new BoundingBox(0, 0, -0.2, 20, 10, 0));

            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 1.85, 6, 4, 2.15));

            document.AddItem(model, "Duct B", "Ducts", "Rectangular",
                             new BoundingBox(12, 2, 1.90, 16, 4, 2.20));

            return document;
        }

        /// <summary>A saved run over the guessed surfaces, at 2.1 m.</summary>
        private static CheckRunSpec Spec(string id, string name, double minimum = 2.1)
        {
            var spec = new CheckRunSpec(FindingSource.Headroom, id, name);

            var settings = new ClearanceSettings
            {
                MinimumMetres = minimum,
                Surfaces = ClearanceSettings.Guessed,
                Targets = ClearanceSettings.NotTheSurfaces,
                WorstPerPatch = false,
            };

            settings.Write(spec);

            return spec;
        }

        private static ClearanceCheck Check(FindingRegister register, CheckRunStore runs,
                                            IModelDocument document) =>
            new ClearanceCheck(register, runs, document);

        // ---------------------------------------------------------------------------------
        // Two named runs over the same geometry
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Two_named_runs_over_overlapping_geometry_produce_disjoint_findings()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            // Both called the same thing, which two coordinators naming their own corridor run do.
            // The id is what separates them, so the name cannot be doing the work here.
            runs.Put(Spec("corridor", "Corridors, 2.1 m"));
            runs.Put(Spec("plant", "Corridors, 2.1 m"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.True(check.Runner!.Run("plant", null));

            var corridor = register.From(check.Key)
                                   .Where(one => CheckFacts.IsIn(one, "corridor"))
                                   .ToList();

            var plant = register.From(check.Key)
                                .Where(one => CheckFacts.IsIn(one, "plant"))
                                .ToList();

            // Both runs measure the same floor and the same two ducts, so what separates them is
            // the run id in the identity and nothing else.
            Assert.NotEmpty(corridor);
            Assert.Equal(corridor.Count, plant.Count);
            Assert.Empty(corridor.Select(one => one.Id).Intersect(plant.Select(one => one.Id),
                                                                  StringComparer.Ordinal));
        }

        [Fact]
        public void Running_one_run_leaves_the_other_runs_findings_present()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));
            runs.Put(Spec("plant", "Plant room, 2.1 m"));

            var check = Check(register, runs, Model());

            check.Runner!.Run("corridor", null);
            check.Runner!.Run("plant", null);

            var plant = register.From(check.Key)
                                .Where(one => CheckFacts.IsIn(one, "plant"))
                                .ToList();

            Assert.NotEmpty(plant);

            // The run that is run again speaks for itself alone. Without the scope this observe is
            // made under, the second run would mark the first run's findings absent.
            check.Runner!.Run("corridor", null);

            Assert.All(plant, one => Assert.True(one.IsPresent));
        }

        [Fact]
        public void A_finding_reported_by_two_runs_names_both()
        {
            var finding = ClearanceFindings.From(
                Span(), default, default, 2.1, null, ClearanceFindings.DefaultCell, "corridor", "Corridors");

            Assert.Equal(new[] { "corridor" }, CheckFacts.RunsOf(finding));

            // What a producer writes when one physical breach is reported by two runs. The board
            // counts it under both and draws it once.
            CheckFacts.SetRuns(finding, new[] { "corridor", "plant" });

            Assert.Equal(new[] { "corridor", "plant" }, CheckFacts.RunsOf(finding));
            Assert.True(CheckFacts.IsIn(finding, "corridor"));
            Assert.True(CheckFacts.IsIn(finding, "plant"));
        }

        // ---------------------------------------------------------------------------------
        // The empty states
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_run_saved_and_never_run_is_not_a_run_that_found_nothing()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("waiting", "Corridors, 2.1 m"));

            // Nothing in this model is under 2.1 m: the floor is at 0 and the duct's underside is
            // at 3 m, so the run reports nothing and says so.
            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            document.AddItem(model, "Slab L3", "Floors", "Concrete",
                             new BoundingBox(0, 0, -0.2, 20, 10, 0));
            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 3.0, 6, 4, 3.3));

            runs.Put(Spec("empty", "Plant room, 2.1 m"));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("empty", null));

            var read = check.Read(null, null);

            var waiting = read.Runs.Single(run => run.Id == "waiting");
            var empty = read.Runs.Single(run => run.Id == "empty");

            Assert.Equal(0, waiting.LastRunTicks);
            Assert.NotEqual(0, empty.LastRunTicks);
            Assert.Equal(0, waiting.ResultCount);
            Assert.Equal(0, empty.ResultCount);

            // The read says which of the two is which, because the board draws opposite sentences
            // for them: one is waiting to be run and one has an answer.
            Assert.Contains(read.Notes, note => note.Contains("Corridors, 2.1 m", StringComparison.Ordinal));
            Assert.DoesNotContain(read.Notes, note => note.Contains("Plant room", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------------------------
        // The settings
        // ---------------------------------------------------------------------------------

        [Fact]
        public void The_tolerance_band_is_distinct_from_the_minimum()
        {
            var spec = Spec("corridor", "Corridors, 2.1 m");

            var settings = ClearanceSettings.From(spec);
            settings.ToleranceMetres = 0.2;
            settings.Write(spec);

            var read = ClearanceSettings.From(spec);

            Assert.Equal(2.1, read.MinimumMetres, 6);
            Assert.Equal(0.2, read.ToleranceMetres, 6);

            // A 2.1 m corridor measured at 2.098 m is a survey difference; the same corridor at
            // 1.850 m is a coordination issue. The band is what separates them.
            Assert.Equal(1.9, read.ReportUnderMetres, 6);
        }

        [Fact]
        public void A_run_reports_nothing_shallower_than_its_tolerance_band()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var spec = Spec("corridor", "Corridors, 2.1 m");
            var settings = ClearanceSettings.From(spec);

            settings.ToleranceMetres = 0.15;
            settings.Write(spec);
            runs.Put(spec);

            // The duct's underside is 2.0 m up: 100 mm under the minimum, and inside the band.
            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            document.AddItem(model, "Slab L3", "Floors", "Concrete",
                             new BoundingBox(0, 0, -0.2, 20, 10, 0));
            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 2.0, 6, 4, 2.3));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.DoesNotContain(register.From(check.Key), one => one.IsPresent);

            settings.ToleranceMetres = 0;
            settings.Write(spec);

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.Contains(register.From(check.Key), one => one.IsPresent);
        }

        [Fact]
        public void An_ignored_category_is_kept_off_the_obstruction_side()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var spec = Spec("corridor", "Corridors, 2.1 m");
            var settings = ClearanceSettings.From(spec);

            settings.Ignore.Add("Sprinkler");
            settings.Write(spec);
            runs.Put(spec);

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            document.AddItem(model, "Slab L3", "Floors", "Concrete",
                             new BoundingBox(0, 0, -0.2, 20, 10, 0));
            document.AddItem(model, "Head 12", "Sprinklers", "Pendent",
                             new BoundingBox(2, 2, 1.9, 2.1, 2.1, 2.0));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("corridor", null));

            // Without the ignore list the first federation run is four thousand sprinkler heads and
            // the first act on the board is deleting them.
            Assert.DoesNotContain(register.From(check.Key), one => one.IsPresent);
        }

        [Fact]
        public void Every_breach_is_reported_when_the_run_asks_for_more_than_the_worst()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("every", "Corridors, every breach"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("every", null));

            var every = register.From(check.Key).Count(one => one.IsPresent);

            var worst = Spec("worst", "Corridors, worst only");
            var settings = ClearanceSettings.From(worst);

            settings.WorstPerPatch = true;
            settings.Write(worst);
            runs.Put(worst);

            Assert.True(check.Runner!.Run("worst", null));

            var reduced = register.From(check.Key)
                                  .Count(one => one.IsPresent && CheckFacts.IsIn(one, "worst"));

            Assert.Equal(2, every);
            Assert.Equal(1, reduced);
        }

        // ---------------------------------------------------------------------------------
        // Renaming and deleting
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Renaming_a_run_keeps_its_findings_and_their_decisions()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));

            var check = Check(register, runs, Model());

            check.Runner!.Run("corridor", null);

            var one = register.From(check.Key).First();

            one.Status = FindingStatus.Approved;

            Assert.True(check.Naming!.Rename("corridor", "Level 3 corridors"));

            check.Runner!.Run("corridor", null);

            var after = register.Find(one.Id);

            Assert.NotNull(after);
            Assert.Equal(FindingStatus.Approved, after!.Status);
            Assert.Equal("Level 3 corridors", after.Rule);
        }

        /// <summary>One breach, for the tests that need a span and no document.</summary>
        private static HeadroomSpan Span()
        {
            var floor = new HeadroomElement("floor", "Slab L3")
            {
                MinX = 0, MinY = 0, MinZ = -0.2, MaxX = 10, MaxY = 10, MaxZ = 0,
            };

            var duct = new HeadroomElement("duct", "Duct 400x250")
            {
                MinX = 2, MinY = 2, MinZ = 1.85, MaxX = 6, MaxY = 4, MaxZ = 2.15,
            };

            return Headroom.Check(new[] { floor }, new[] { duct }, 2.1).Spans.Single();
        }
    }
}
