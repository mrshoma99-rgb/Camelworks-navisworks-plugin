using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
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

            var slab = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                        new BoundingBox(0, 0, -0.2, 20, 10, 0));

            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 1.85, 6, 4, 2.15));

            document.AddItem(model, "Duct B", "Ducts", "Rectangular",
                             new BoundingBox(12, 2, 1.90, 16, 4, 2.20));

            document.DefineSet("Floors", slab.Key);

            return document;
        }

        /// <summary>A saved run over the named floors set, at 2.1 m.</summary>
        private static CheckRunSpec Spec(string id, string name, double minimum = 2.1)
        {
            var spec = new CheckRunSpec(FindingSource.Headroom, id, name);

            var settings = new ClearanceSettings
            {
                MinimumMetres = minimum,
                Surfaces = "Floors",
                DetectSurfaces = false,
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

        /// <summary>
        /// The run a clearance finding names is the run that produced it, and only that run.
        ///
        /// This replaces a test that called <c>CheckFacts.SetRuns</c> by hand to make a finding
        /// naming two runs. Clearance cannot produce one: the run id is part of the identity, so
        /// the same breach found by two runs is two findings with one run each. The fabricated
        /// state exercised <c>CheckFacts</c>, which has its own tests, and nothing in this check.
        /// </summary>
        [Fact]
        public void A_clearance_finding_names_exactly_the_run_that_produced_it()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            // The same name on both, so only the run id can be keeping the findings apart.
            runs.Put(Spec("corridor", "Corridors, 2.1 m"));
            runs.Put(Spec("plant", "Corridors, 2.1 m"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.True(check.Runner!.Run("plant", null));

            Assert.All(register.From(check.Key),
                       one => Assert.Single(CheckFacts.RunsOf(one)));

            // Same floor, same duct, same place, two runs: two findings, and neither names the
            // other's run.
            var corridor = register.From(check.Key).Where(one => CheckFacts.IsIn(one, "corridor"));

            Assert.NotEmpty(corridor);
            Assert.All(corridor, one => Assert.False(CheckFacts.IsIn(one, "plant")));
        }

        [Fact]
        public void A_breach_names_the_surface_it_was_measured_from()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));

            var one = register.From(check.Key).First();

            // The Surface column reads this fact. Without it the first column of every clearance
            // row was blank on screen, in search, in the copy, in the spreadsheet and in BCF.
            Assert.Equal("Slab L3", one.Facts[ClearanceFindings.Surface]);
            Assert.StartsWith("Duct", one.Facts[ClearanceFindings.Obstruction], StringComparison.Ordinal);

            var surface = check.Columns.Single(
                column => string.Equals(column.Title, ClearanceColumns.Surface, StringComparison.Ordinal));

            Assert.Equal("Slab L3", surface.Show(one));
        }

        [Fact]
        public void A_runs_caveat_stays_on_that_run_and_survives_a_reload()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var broken = Spec("broken", "Corridors, 2.1 m");
            var settings = ClearanceSettings.From(broken);

            settings.Zone = "Level 3 corridros";
            settings.Write(broken);

            runs.Put(broken);
            runs.Put(Spec("fine", "Plant room, 2.1 m"));

            var check = Check(register, runs, Model());

            Assert.False(check.Runner!.Run("broken", null));
            Assert.True(check.Runner.Run("fine", null));

            // Running the second run used to clear the first run's caveat, so the note saying
            // the first had measured nothing was lost while its stale findings stayed present.
            Assert.Contains(check.Read(new[] { "broken" }, null).Notes,
                            note => note.Contains("resolved to no surface", StringComparison.Ordinal));

            Assert.DoesNotContain(check.Read(new[] { "fine" }, null).Notes,
                                  note => note.Contains("resolved to no surface", StringComparison.Ordinal));

            // And it is on the run, so a project written now and read back tomorrow still has it.
            var back = CheckRunSpec.FromJson(runs.Find(FindingSource.Headroom, "broken")!.ToJson())!;

            Assert.Contains(back.Notes,
                            note => note.Contains("resolved to no surface", StringComparison.Ordinal));
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

            var floors = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                          new BoundingBox(0, 0, -0.2, 20, 10, 0));

            document.DefineSet("Floors", floors.Key);
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

            var floors = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                          new BoundingBox(0, 0, -0.2, 20, 10, 0));

            document.DefineSet("Floors", floors.Key);
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

            var floors = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                          new BoundingBox(0, 0, -0.2, 20, 10, 0));

            document.DefineSet("Floors", floors.Key);
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

        // ---------------------------------------------------------------------------------
        // A run that could not measure
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_run_that_resolves_to_no_surface_marks_nothing_absent()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var spec = Spec("corridor", "Corridors, 2.1 m");

            runs.Put(spec);

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));

            var found = register.From(check.Key).Where(one => one.IsPresent).ToList();

            Assert.NotEmpty(found);

            // One typo in the zone picker: the run now resolves to nothing.
            var settings = ClearanceSettings.From(spec);

            settings.Zone = "Level 3 corridros";
            settings.Write(spec);

            Assert.False(check.Runner!.Run("corridor", null));

            // Still present: a run that measured nothing is not evidence that anything is fixed.
            Assert.All(found, one => Assert.True(one.IsPresent));
            Assert.Contains(check.Notes, note => note.Contains("resolved to no surface", StringComparison.Ordinal));

            // And it is told apart from a run that measured and found nothing, which does mark its
            // findings absent.
            settings.Zone = null;
            settings.MinimumMetres = 0.5;
            settings.Write(spec);

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.All(found, one => Assert.False(one.IsPresent));
        }

        [Fact]
        public void A_run_whose_obstruction_side_resolves_to_nothing_marks_nothing_absent()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var spec = Spec("corridor", "Corridors, 2.1 m");

            var document = Model();
            var settings = ClearanceSettings.From(spec);

            settings.Targets = "Services";
            settings.Write(spec);
            runs.Put(spec);

            document.DefineSet("Services",
                               document.Traverse(TraversalScope.WholeDocument)
                                       .Where(item => item.Category == "Ducts")
                                       .Select(item => item.Key)
                                       .ToArray());

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("corridor", null));

            var found = register.From(check.Key).Where(one => one.IsPresent).ToList();

            Assert.NotEmpty(found);

            // The set is renamed in Set Builder, or deleted. The run now measures up to nothing.
            settings.Targets = "Services v2";
            settings.Write(spec);

            Assert.False(check.Runner!.Run("corridor", null));
            Assert.All(found, one => Assert.True(one.IsPresent));
            Assert.Contains(check.Notes, note => note.Contains("resolved to nothing over the surfaces",
                                                               StringComparison.Ordinal));
        }

        [Fact]
        public void Changing_the_surface_side_says_why_every_breach_reads_as_new()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var spec = Spec("corridor", "Corridors, 2.1 m");

            runs.Put(spec);

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.DoesNotContain(check.Notes, note => note.Contains("measured from", StringComparison.Ordinal));

            var settings = ClearanceSettings.From(spec);

            settings.Surfaces = ClearanceSettings.Loaded;
            settings.Write(spec);

            Assert.True(check.Runner!.Run("corridor", null));

            // The delta will report the whole run as gone and reported again, and only the run
            // knows that the ids were re-derived rather than the model having moved.
            Assert.Contains(check.Notes, note => note.Contains("measured from", StringComparison.Ordinal));

            // Said once, not on every run from then on.
            Assert.True(check.Runner!.Run("corridor", null));
            Assert.DoesNotContain(check.Notes, note => note.Contains("measured from", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------------------------
        // The scope
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_level_scope_reports_only_the_levels_it_names()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var spec = Spec("corridor", "Corridors, 2.1 m");
            var settings = ClearanceSettings.From(spec);

            settings.Levels.Add("Level 03");
            settings.Write(spec);
            runs.Put(spec);

            var plan = new LevelPlan();

            plan.Add("Level 03", 0);
            plan.Add("Level 04", 4);

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            var floors = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                          new BoundingBox(0, 0, -0.2, 20, 10, 0));

            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 1.85, 6, 4, 2.15));

            var upper = document.AddItem(model, "Slab L4", "Floors", "Concrete",
                                         new BoundingBox(0, 0, 3.8, 20, 10, 4));

            // Both storeys are in the set, so it is the level scope and not the source that drops
            // the one on Level 04.
            document.DefineSet("Floors", floors.Key, upper.Key);
            document.AddItem(model, "Duct B", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 5.85, 6, 4, 6.15));

            var check = new ClearanceCheck(register, runs, document, levels: () => plan);

            Assert.True(check.Runner!.Run("corridor", null));

            var found = register.From(check.Key).Where(one => one.IsPresent).ToList();

            Assert.Single(found);
            Assert.Equal("Slab L3", found[0].Facts[ClearanceFindings.Surface]);
        }

        [Fact]
        public void A_level_scope_keeps_a_surface_nobody_could_place_and_drops_the_ones_it_placed_elsewhere()
        {
            var settings = new ClearanceSettings();

            // No scope is every level, including the unplaced.
            Assert.True(settings.CoversLevel("Level 03"));
            Assert.True(settings.CoversLevel(null));

            settings.Levels.Add("Level 03");

            Assert.True(settings.CoversLevel("Level 03"));
            Assert.True(settings.CoversLevel("level 03"));
            Assert.False(settings.CoversLevel("Level 04"));

            // A level scope narrows what is reported; it is not evidence that a surface nobody
            // could place is outside it.
            Assert.True(settings.CoversLevel(null));
            Assert.True(settings.CoversLevel("  "));
        }

        [Fact]
        public void A_run_confined_to_a_zone_measures_only_what_is_in_it()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            var slab = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                        new BoundingBox(0, 0, -0.2, 20, 10, 0));
            var ductA = document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                                         new BoundingBox(2, 2, 1.85, 6, 4, 2.15));

            document.AddItem(model, "Duct B", "Ducts", "Rectangular",
                             new BoundingBox(12, 2, 1.90, 16, 4, 2.20));

            document.DefineSet("Floors", slab.Key);
            document.DefineSet("West corridor", slab.Key, ductA.Key);

            var spec = Spec("corridor", "Corridors, 2.1 m");
            var settings = ClearanceSettings.From(spec);

            settings.Zone = "West corridor";
            settings.Write(spec);
            runs.Put(spec);

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("corridor", null));

            var found = register.From(check.Key).Where(one => one.IsPresent).ToList();

            Assert.Single(found);
            Assert.Equal("Duct A", found[0].Facts[ClearanceFindings.Obstruction]);
        }

        [Fact]
        public void Detection_sweeps_the_source_rather_than_the_whole_document()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            var slab = document.AddItem(model, "Slab L3", "Floors", "Concrete");
            var desk = document.AddItem(model, "Desk 12", "Furniture", "Bench");

            document.SetSlab(slab.Key, 0, 0, -0.2, 20, 10, 0);

            // A desk top is an up-facing surface over the minimum area and detection cannot tell
            // it from a floor. Naming the floors set is how a coordinator says so, and before this
            // detection swept the whole document and threw the set away.
            document.SetSlab(desk.Key, 2, 2, 0.70, 3.6, 3, 0.75);

            document.DefineSet("Floors", slab.Key);

            var settings = new ClearanceSettings
            {
                MinimumMetres = 2.1,
                Surfaces = "Floors",
                DetectSurfaces = true,
                Targets = ClearanceSettings.NotTheSurfaces,
            };

            var check = new ClearanceCheck(register, runs, document, document);
            var patches = check.Detect(settings);

            Assert.NotEmpty(patches);
            Assert.All(patches, patch => Assert.Equal(slab.Key, patch.Source));

            // The same run over everything loaded finds the desk as well, which is the state the
            // caveat on the set-up screen describes.
            settings.Surfaces = ClearanceSettings.Loaded;

            Assert.Contains(check.Detect(settings), patch => patch.Source.Equals(desk.Key));
        }

        [Fact]
        public void Detection_falls_back_to_the_zone_when_the_source_names_everything()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            var slab = document.AddItem(model, "Slab L3", "Floors", "Concrete");
            var desk = document.AddItem(model, "Desk 12", "Furniture", "Bench");

            document.SetSlab(slab.Key, 0, 0, -0.2, 20, 10, 0);
            document.SetSlab(desk.Key, 2, 2, 0.70, 3.6, 3, 0.75);

            document.DefineSet("West corridor", slab.Key);

            var settings = new ClearanceSettings
            {
                MinimumMetres = 2.1,
                Surfaces = ClearanceSettings.Loaded,
                DetectSurfaces = true,
                Zone = "West corridor",
            };

            var check = new ClearanceCheck(register, runs, document, document);

            Assert.All(check.Detect(settings), patch => Assert.Equal(slab.Key, patch.Source));
        }

        [Fact]
        public void A_new_run_leaves_the_three_costliest_categories_out()
        {
            var settings = new ClearanceSettings();

            Assert.Equal(new[] { "Sprinkler", "Lighting", "Fire Alarm" }, settings.Ignore);
            Assert.True(settings.Ignores("Sprinklers"));
            Assert.True(settings.Ignores("Lighting Fixtures"));
            Assert.False(settings.Ignores("Ducts"));

            // A run that says "report everything" must survive the save. Without the key being
            // read as authoritative, an emptied list came back holding the three defaults.
            var spec = new CheckRunSpec(FindingSource.Headroom, "all", "Everything");

            settings.Ignore.Clear();
            settings.Write(spec);

            Assert.Empty(ClearanceSettings.From(spec).Ignore);
        }

        [Fact]
        public void A_new_run_reports_shortfalls_deeper_than_the_survey_band()
        {
            // 25 mm rather than nothing. At nothing a 2.099 m soffit under a 2.100 m minimum is a
            // row, and the first federation run is thousands of them.
            var settings = new ClearanceSettings();

            Assert.Equal(0.025, settings.ToleranceMetres, 6);
            Assert.Equal(2.075, settings.ReportUnderMetres, 6);
        }

        [Fact]
        public void A_dropped_surface_is_not_measured_from()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            var slab = document.AddItem(model, "Slab L3", "Floors", "Concrete");

            document.SetSlab(slab.Key, 0, 0, -0.2, 20, 10, 0);

            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 1.85, 6, 4, 2.15));

            var spec = new CheckRunSpec(FindingSource.Headroom, "corridor", "Corridors, 2.1 m");
            var settings = new ClearanceSettings
            {
                MinimumMetres = 2.1,
                Surfaces = ClearanceSettings.Loaded,
                DetectSurfaces = true,
                Targets = ClearanceSettings.NotTheSurfaces,
                WorstPerPatch = false,
            };

            settings.Write(spec);
            runs.Put(spec);

            var check = new ClearanceCheck(register, runs, document, document);

            var patches = check.Detect(settings);

            Assert.NotEmpty(patches);

            Assert.True(check.Runner!.Run("corridor", null));
            Assert.Contains(register.From(check.Key), one => one.IsPresent);

            // Dropped in the set-up window because it is a desk top rather than a floor. The run
            // then has nothing to measure from and says so rather than reporting the breach again.
            foreach (var patch in patches) settings.Excluded.Add(patch.Id);

            settings.Write(spec);

            Assert.False(check.Runner!.Run("corridor", null));
            Assert.Contains(check.Notes, note => note.Contains("resolved to no surface", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------------------------
        // Forgetting results
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Compact_drops_the_resolved_results_and_keeps_the_rest()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));

            var found = register.From(check.Key).ToList();

            Assert.Equal(2, found.Count);

            found[0].Status = FindingStatus.Resolved;
            found[1].Status = FindingStatus.Active;

            Assert.True(check.Runner!.Compact(new[] { "corridor" }));

            // The menu says "remove resolved results" and this is what it removes. Compacting
            // everything empties a board somebody is halfway through working.
            Assert.False(found[0].IsPresent);
            Assert.True(found[1].IsPresent);
        }

        [Fact]
        public void Deleting_a_run_takes_its_findings_off_the_board_and_leaves_them_in_the_register()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("corridor", null));

            var one = register.From(check.Key).First();

            one.Status = FindingStatus.Approved;

            Assert.True(check.Runner!.Delete(new[] { "corridor" }));

            // Off the board, because nothing will report them again.
            Assert.Empty(check.Read(null, null).Findings);
            Assert.Empty(check.Runs());

            // Still in the register, because an issue may name one and a removed finding is an
            // issue pointing at nothing.
            var after = register.Find(one.Id);

            Assert.NotNull(after);
            Assert.False(after!.IsPresent);
            Assert.Equal(FindingStatus.Approved, after.Status);
        }

        // ---------------------------------------------------------------------------------
        // How bad, in this check's own words
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Severity_is_measured_against_the_runs_own_minimum()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));
            runs.Put(Spec("plant", "Plant room, 2.4 m", minimum: 2.4));

            var check = Check(register, runs, Model());

            check.Runner!.Run("corridor", null);
            check.Runner!.Run("plant", null);

            Finding Lowest(string run) =>
                register.From(check.Key)
                        .Where(one => CheckFacts.IsIn(one, run))
                        .OrderBy(one => ClearanceFindings.MetresOf(one, ClearanceFindings.Measured))
                        .First();

            var corridor = check.Severity(Lowest("corridor"));
            var plant = check.Severity(Lowest("plant"));

            Assert.NotNull(corridor);
            Assert.NotNull(plant);

            // The same duct, the same height, two runs: the 2.4 m run is further under its own
            // minimum, so a board banding both together bands them apart.
            Assert.True(plant!.Value > corridor!.Value);

            // 1.85 m clear where 2.1 m was asked for.
            Assert.Equal(1.0 + (2.1 - 1.85) / 2.1, corridor.Value, 6);

            Assert.Equal("no measure", check.SeverityBand(null));
            Assert.Equal("at the minimum", check.SeverityBand(1.0));
            Assert.Equal("under the minimum", check.SeverityBand(1.1));
            Assert.Equal("far under the minimum", check.SeverityBand(1.3));

            // A finding with no measurement on it has no severity rather than a severity of nought.
            Assert.Null(check.Severity(ClearanceFindings.From(Span(), default, default, 0)));
        }

        // ---------------------------------------------------------------------------------
        // The property columns
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_property_column_reads_the_surface_and_the_obstruction()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("corridor", "Corridors, 2.1 m"));

            var document = new FakeDocument();
            var model = document.AddModel("Arch", @"C:\job\Arch.nwc");

            var floors = document.AddItem(model, "Slab L3", "Floors", "Concrete",
                                          new BoundingBox(0, 0, -0.2, 20, 10, 0))
                                 .WithProperty("Element", "Workset", "ST-Slabs");

            document.DefineSet("Floors", floors.Key);

            document.AddItem(model, "Duct A", "Ducts", "Rectangular",
                             new BoundingBox(2, 2, 1.85, 6, 4, 2.15))
                    .WithProperty("Element", "Workset", "ME-Supply");

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("corridor", null));

            var one = register.From(check.Key).Single(finding => finding.IsPresent);
            var properties = check.Properties;

            // The capability the board fills its added columns through. Clearance answered null
            // here, so every column somebody added to a clearance board stayed empty while the
            // board reported that it had read them.
            Assert.NotNull(properties);

            Assert.Equal(new[] { "ST-Slabs" },
                         properties!.ValuesFor(one, 0, "Element", "Workset")
                                    .Select(value => value.Value));

            Assert.Equal(new[] { "ME-Supply" },
                         properties.ValuesFor(one, 1, "Element", "Workset")
                                   .Select(value => value.Value));

            // "Both", as the board words it: every element the finding names.
            Assert.Equal(new[] { "ST-Slabs", "ME-Supply" },
                         properties.ValuesFor(one, -1, "Element", "Workset")
                                   .Select(value => value.Value));

            // A property nothing carries is empty rather than an error, which is the blank cell
            // the board counts and reports.
            Assert.Empty(properties.ValuesFor(one, -1, "Element", "Fire rating"));
        }

        // ---------------------------------------------------------------------------------
        // The settings, kept
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Every_setting_of_a_run_survives_the_project_file()
        {
            var store = ProjectStore.Open(new InMemoryFileSystem(), @"C:\job\Tower.nwf", null);
            var runs = new CheckRunStore();

            var spec = new CheckRunSpec(FindingSource.Headroom, "corridor", "Corridors")
            {
                Folder = "Level 3",
            };

            var settings = new ClearanceSettings
            {
                MinimumMetres = 2.4,
                ToleranceMetres = 0.15,
                Surfaces = ClearanceSettings.Loaded,
                Targets = "Services",
                Zone = "West corridor",
                WorstPerPatch = false,
                ExcludeStairs = true,
                PreferFloorCategories = true,
                MinPatchAreaSquareMetres = 0.25,
            };

            settings.Levels.Add("Level 03");
            settings.Levels.Add("Level 04");
            settings.Ignore.Clear();
            settings.Ignore.Add("Sprinklers");
            settings.Ignore.Add("Lighting Fixtures");
            settings.Excluded.Add("patch-7");
            settings.Excluded.Add("patch-9");

            settings.Write(spec);
            runs.Put(spec);
            runs.Write(store);

            // Read back the way a second session reads it: off the project file, through the store.
            var again = ClearanceSettings.From(CheckRunStore.Read(store).Find(FindingSource.Headroom,
                                                                              "corridor"));

            Assert.Equal(2.4, again.MinimumMetres, 6);
            Assert.Equal(0.15, again.ToleranceMetres, 6);
            Assert.Equal(ClearanceSettings.Loaded, again.Surfaces);
            Assert.True(again.DetectSurfaces);
            Assert.Equal("Services", again.Targets);
            Assert.Equal("West corridor", again.Zone);
            Assert.Equal(new[] { "Level 03", "Level 04" }, again.Levels);
            Assert.Equal(new[] { "Sprinklers", "Lighting Fixtures" }, again.Ignore);
            Assert.False(again.WorstPerPatch);
            Assert.True(again.ExcludeStairs);
            Assert.True(again.PreferFloorCategories);
            Assert.Equal(0.25, again.MinPatchAreaSquareMetres, 6);
            Assert.Equal(new[] { "patch-7", "patch-9" }, again.Excluded.OrderBy(one => one,
                                                                                StringComparer.Ordinal));
        }

        [Fact]
        public void An_ignored_category_is_matched_anywhere_in_the_category_name()
        {
            var settings = new ClearanceSettings();

            settings.Ignore.Add("Duct");

            // Substring, which is what makes one entry cover Ducts, Duct Fittings and Duct
            // Insulation - and what makes it cover Ductwork Supports nobody meant to leave out.
            Assert.True(settings.Ignores("Ducts"));
            Assert.True(settings.Ignores("Duct Fittings"));
            Assert.True(settings.Ignores("ducts"));
            Assert.False(settings.Ignores("Pipes"));
            Assert.False(settings.Ignores(null));
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
