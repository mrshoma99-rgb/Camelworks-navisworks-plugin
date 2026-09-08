using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Fall hazard as a check: saved runs, findings that name the elements the owner grid found,
    /// and what a run says when it could not do what it was asked.
    /// </summary>
    public class FallCheckTests
    {
        /// <summary>Two triangles covering an axis-aligned rectangle.</summary>
        private static IEnumerable<Tri2> Rect(double x0, double y0, double x1, double y1)
        {
            yield return new Tri2(x0, y0, x1, y0, x1, y1);
            yield return new Tri2(x0, y0, x1, y1, x0, y1);
        }

        /// <summary>The west half of a 10 m × 10 m floor, with half a hole in it.</summary>
        private static List<Tri2> West()
        {
            var tris = new List<Tri2>();

            tris.AddRange(Rect(0, 0, 5, 4));
            tris.AddRange(Rect(0, 6, 5, 10));
            tris.AddRange(Rect(0, 4, 4, 6));

            return tris;
        }

        /// <summary>The east half, with the other half of the hole.</summary>
        private static List<Tri2> East()
        {
            var tris = new List<Tri2>();

            tris.AddRange(Rect(5, 0, 10, 4));
            tris.AddRange(Rect(5, 6, 10, 10));
            tris.AddRange(Rect(6, 4, 10, 6));

            return tris;
        }

        /// <summary>Give an element a flat mesh of plan triangles at one elevation.</summary>
        /// <param name="document">The document.</param>
        /// <param name="key">Which element.</param>
        /// <param name="triangles">The triangles, in plan.</param>
        /// <param name="z">What elevation to lay them at, in metres.</param>
        private static void Lay(FakeDocument document, ElementKey key,
                                IReadOnlyList<Tri2> triangles, double z)
        {
            var positions = new List<double>();
            var indices = new List<int>();

            foreach (var triangle in triangles)
            {
                var at = positions.Count / 3;

                positions.AddRange(new[]
                {
                    triangle.Ax, triangle.Ay, z,
                    triangle.Bx, triangle.By, z,
                    triangle.Cx, triangle.Cy, z,
                });

                indices.AddRange(new[] { at, at + 1, at + 2 });
            }

            document.SetMesh(key, positions, indices);
        }

        /// <summary>A floor in two slabs at Z = 0, with a 2 m × 2 m hole across the join.</summary>
        private static FakeDocument Model() => Model(out _, out _);

        /// <summary>The same floor, with the two slabs' keys.</summary>
        /// <param name="west">The west slab.</param>
        /// <param name="east">The east slab.</param>
        private static FakeDocument Model(out ElementKey west, out ElementKey east)
        {
            var document = new FakeDocument();
            var model = document.AddModel("Struct", @"C:\job\Struct.nwc");

            west = document.AddItem(model, "Slab L3 west", "Floors", "Concrete",
                                    new BoundingBox(0, 0, -0.2, 5, 10, 0)).Key;

            east = document.AddItem(model, "Slab L3 east", "Floors", "Concrete",
                                    new BoundingBox(5, 0, -0.2, 10, 10, 0)).Key;

            Lay(document, west, West(), 0);
            Lay(document, east, East(), 0);

            return document;
        }

        /// <summary>A saved run over everything loaded, at the level the slabs sit at.</summary>
        private static CheckRunSpec Spec(string id, string name, double cell = 0.1,
                                          double limit = 0.5)
        {
            var spec = new CheckRunSpec(FindingSource.FallHazard, id, name);

            new FallSettings
            {
                Floors = CheckSource.Everything(),
                LevelMetres = 0,
                BandMetres = 1.0,
                CellMetres = cell,
                LimitMetres = limit,
            }.Write(spec);

            return spec;
        }

        private static FallCheck Check(FindingRegister register, CheckRunStore runs,
                                       FakeDocument document) =>
            new FallCheck(register, runs, document, document);

        // ------------------------------------------------------------------ What a run finds

        [Fact]
        public void An_openings_finding_names_the_slabs_around_it()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, Model(out var west, out var east));

            Assert.True(check.Runner!.Run("level3", null));

            var read = check.Read(new[] { "level3" }, null);
            var opening = read.Findings.Single(one => !FallHazardFindings.IsEdge(one));
            var named = opening.AllElements().ToList();

            Assert.Equal(2, named.Count);
            Assert.Contains(west, named);
            Assert.Contains(east, named);

            // And the row says so without anything having to resolve a key.
            Assert.Contains("Slab L3", opening.Title);
        }

        [Fact]
        public void A_run_reports_the_opening_and_the_edge_around_it()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("level3", null));

            var read = check.Read(null, null);
            var opening = read.Findings.Single(one => !FallHazardFindings.IsEdge(one));
            var edge = read.Findings.Single(FallHazardFindings.IsEdge);

            // The hole is 2 m × 2 m, so its area is 4 m² and its widest gap about 2 m. Metres
            // throughout, at full precision, whatever the seat's own units are.
            Assert.Equal(4.0, FallHazardFindings.MetresOf(opening, FallHazardFindings.Area)!.Value, 1);
            Assert.InRange(FallHazardFindings.MetresOf(opening, FallHazardFindings.WidestGap)!.Value, 1.5, 2.5);
            Assert.Equal(5.0, opening.X!.Value, 1);
            Assert.Equal(5.0, opening.Y!.Value, 1);
            Assert.Equal(0.0, opening.Z!.Value, 6);

            // The rim of a 2 m hole is about 8 m of unrailed edge.
            Assert.InRange(FallHazardFindings.MetresOf(edge, FallHazardFindings.EdgeLength)!.Value, 7.0, 10.0);
            Assert.Null(FallHazardFindings.MetresOf(edge, FallHazardFindings.Area));

            // Both are over the 0.5 m the run allowed, and the check says so in its own words.
            Assert.All(read.Findings, one => Assert.Equal("far over the gap allowed",
                                                          check.SeverityBand(check.Severity(one))));
        }

        [Fact]
        public void Two_runs_over_the_same_hole_stay_two_findings()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            // Both called the same thing, which two coordinators naming their own level run do.
            // The id is what separates them, so the name cannot be doing the work here.
            runs.Put(Spec("shell", "Level 3 openings"));
            runs.Put(Spec("fitout", "Level 3 openings"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("shell", null));
            Assert.True(check.Runner.Run("fitout", null));

            var shell = check.Read(new[] { "shell" }, null);
            var fitout = check.Read(new[] { "fitout" }, null);

            Assert.NotEmpty(shell.Findings);
            Assert.Equal(shell.Findings.Count, fitout.Findings.Count);
            Assert.Empty(shell.Findings.Select(one => one.Id)
                              .Intersect(fitout.Findings.Select(one => one.Id)));
        }

        [Fact]
        public void Running_one_run_leaves_the_other_runs_findings_alone()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("shell", "Shell and core"));
            runs.Put(Spec("fitout", "Fit-out"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("shell", null));

            var before = check.Read(new[] { "shell" }, null).Findings.Count;

            Assert.True(check.Runner.Run("fitout", null));
            Assert.Equal(before, check.Read(new[] { "shell" }, null).Findings.Count);
        }

        [Fact]
        public void Renaming_a_run_keeps_its_findings()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("level3", null));

            var before = check.Read(new[] { "level3" }, null).Findings
                              .Select(one => one.Id).OrderBy(one => one).ToList();

            Assert.True(check.Naming!.Rename("level3", "Level 3, shell and core"));
            Assert.True(check.Runner.Run("level3", null));

            var after = check.Read(new[] { "level3" }, null).Findings
                             .Select(one => one.Id).OrderBy(one => one).ToList();

            // The run's id is the identity and its name is not, so the same hazards come back
            // under the same ids and whatever was decided about them is still on them.
            Assert.Equal(before, after);
        }

        // ---------------------------------------------------------------- What a run cannot do

        [Fact]
        public void A_plan_the_sweeps_cannot_settle_says_so_in_the_read()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Struct", @"C:\job\Struct.nwc");
            var floor = FallHazardTests.Serpentine(200, out var span);

            var slab = document.AddItem(model, "Serpentine slab", "Floors", "Concrete",
                                        new BoundingBox(0, 0, -0.2, span.Item1, span.Item2, 0));

            Lay(document, slab.Key, floor, 0);

            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("snake", "The winding void"));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("snake", null));
            Assert.Contains(check.Read(new[] { "snake" }, null).Notes,
                            note => note.Contains("still changing"));
        }

        [Fact]
        public void A_seat_with_no_triangles_cannot_offer_the_check_at_all()
        {
            var document = Model();

            document.CanReadTriangles = false;
            document.Unavailable = "the primitive reader is missing on this seat";

            var check = Check(new FindingRegister(), new CheckRunStore(), document);

            Assert.False(check.IsAvailable);
            Assert.Contains("no triangles", check.Unavailable);
            Assert.False(check.Runner!.CanRun);
        }

        [Fact]
        public void A_run_that_reads_no_floor_says_which_step_lost_it_and_marks_nothing_absent()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("level3", null));

            var found = check.Read(new[] { "level3" }, null).Findings.Count;

            Assert.True(found > 0);

            // The same run, moved to a level the floor does not reach.
            var spec = check.Spec("level3")!;
            var settings = FallSettings.From(spec);

            settings.LevelMetres = 40;
            settings.Write(spec);

            Assert.False(check.Runner.Run("level3", null));

            var read = check.Read(new[] { "level3" }, null);

            Assert.Contains(read.Notes, note => note.Contains("sat outside the band"));
            Assert.Equal(found, read.Findings.Count);
        }

        [Fact]
        public void A_new_run_says_nothing_about_where_to_cut_and_is_refused()
        {
            // There is no safe cut elevation. On a site-coordinate model zero is below the
            // foundations, the band catches nothing, and the run comes back with no findings and
            // no reason on the screen the button is on.
            var settings = new FallSettings { Floors = CheckSource.Sets("Level 03 Floors") };

            Assert.False(settings.HasLevel);
            Assert.Contains("elevation", settings.Wrong());

            settings.LevelMetres = 0;

            Assert.True(settings.HasLevel);

            // And a deliberate zero survives the project file, where "not set" does not.
            var spec = new CheckRunSpec(FindingSource.FallHazard, "ground", "Ground floor");

            settings.Write(spec);

            Assert.True(FallSettings.From(spec).HasLevel);
            Assert.False(FallSettings.From(new CheckRunSpec(FindingSource.FallHazard, "x", "X")).HasLevel);
        }

        [Fact]
        public void A_new_run_names_no_floor_and_ignores_a_gap_narrower_than_a_person()
        {
            var settings = new FallSettings();

            // Everything loaded reads walls and ducts at the cut elevation and projects each of
            // them as solid floor, so the run asks rather than assuming.
            Assert.False(settings.HasFloors);
            Assert.Equal(0.3, settings.MinPassageMetres, 6);
        }

        [Fact]
        public void Changing_the_floor_side_says_why_every_hazard_reads_as_new()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var document = Model();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("level3", null));
            Assert.DoesNotContain(check.Notes, note => note.Contains("read the floor from"));

            var spec = check.Spec("level3")!;
            var settings = FallSettings.From(spec);

            settings.LevelMetres = 0.5;
            settings.Write(spec);

            Assert.True(check.Runner.Run("level3", null));
            Assert.Contains(check.Notes, note => note.Contains("read the floor from"));

            // Said once, for the change that happened. Running again at the same settings is not
            // another change.
            Assert.True(check.Runner.Run("level3", null));
            Assert.DoesNotContain(check.Notes, note => note.Contains("read the floor from"));
        }

        [Fact]
        public void A_run_that_names_no_floor_is_refused_before_it_reads_anything()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var spec = new CheckRunSpec(FindingSource.FallHazard, "empty", "Nothing named");

            new FallSettings { Floors = CheckSource.None() }.Write(spec);
            runs.Put(spec);

            var check = Check(register, runs, Model());

            Assert.False(check.Runner!.Run("empty", null));
            Assert.Contains(check.Notes, note => note.Contains("names nothing"));
        }

        [Fact]
        public void A_grid_over_the_cell_cap_is_reported_rather_than_thrown()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("fine", "One millimetre cells", cell: 0.001));

            var check = Check(register, runs, Model());

            Assert.False(check.Runner!.Run("fine", null));
            Assert.Contains(check.Notes, note => note.Contains("cell limit"));
        }

        // --------------------------------------------------------------- What the check offers

        [Fact]
        public void The_check_declares_three_columns_and_two_null_capabilities()
        {
            var check = Check(new FindingRegister(), new CheckRunStore(), Model());

            Assert.Equal(new[] { "Opening", "Widest gap", "Edge with no rail" },
                         check.Columns.Select(one => one.Title).ToArray());

            Assert.Null(check.HostEcho);
            Assert.Null(check.Grouping);
            Assert.NotNull(check.Runner);
            Assert.NotNull(check.Naming);
            Assert.NotNull(check.Geometry);

            // A property reader, because a hazard names ordinary elements. The board gates the
            // property columns on this, so a null left every added column blank.
            Assert.NotNull(check.Properties);

            // And null with no document, which is the one state with nothing to resolve against.
            Assert.Null(new FallCheck(new FindingRegister(), new CheckRunStore()).Properties);
        }

        [Fact]
        public void A_property_column_reads_a_value_off_the_slab_the_opening_is_in()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();
            var document = Model(out var west, out _);

            document.Traverse(TraversalScope.WholeDocument).OfType<FakeItem>()
                    .First(one => one.Key.ToString() == west.ToString())
                    .WithProperty("Element", "Fire rating", "REI 60");

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("level3", null));

            var opening = check.Read(null, null).Findings
                               .First(one => !FallHazardFindings.IsEdge(one));

            var values = check.Properties!.ValuesFor(opening, -1, "Element", "Fire rating");

            Assert.Contains(values, one => one.Value == "REI 60");
        }

        [Fact]
        public void The_run_rail_reads_a_saved_run_in_metres()
        {
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(new FindingRegister(), runs, Model());
            var run = Assert.Single(check.Runs());

            Assert.Equal("Fall hazard", run.Kind);
            Assert.Equal(0, run.LastRunTicks);
            Assert.Contains("0.5 m gap", run.Threshold);
            Assert.Contains("no handrails named", run.Between);
            Assert.True(run.IsOurs);
        }

        [Fact]
        public void A_finding_from_the_run_just_swept_can_be_drawn()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("level3", null));

            var opening = check.Read(null, null).Findings
                               .First(one => !FallHazardFindings.IsEdge(one));

            var png = check.Geometry!.Snapshot(opening, 400, 400);

            Assert.NotNull(png);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, png!.Take(4).ToArray());

            // A finding from a run this session has not swept has no plan to draw.
            var elsewhere = Finding.Create(FindingSource.FallHazard, "Another run", "Elsewhere",
                                           Array.Empty<ElementKey>(), "0,0/opening", "another");

            Assert.Null(check.Geometry.Snapshot(elsewhere, 400, 400));
        }

        [Fact]
        public void A_runs_caveat_stays_on_that_run_and_survives_a_reload()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Struct", @"C:\job\Struct.nwc");
            var floor = FallHazardTests.Serpentine(200, out var span);

            var slab = document.AddItem(model, "Serpentine slab", "Floors", "Concrete",
                                        new BoundingBox(0, 0, -0.2, span.Item1, span.Item2, 0));

            Lay(document, slab.Key, floor, 0);

            // The winding void hits the sweep cap; the second run is over a plain slab and does
            // not. The cap note is what the read has to carry, so it must stay on the run it
            // belongs to.
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("snake", "The winding void"));
            runs.Put(Spec("quiet", "The plain slab", cell: 0.5));

            var check = Check(register, runs, document);

            Assert.True(check.Runner!.Run("snake", null));
            Assert.True(check.Runner.Run("quiet", null));

            // Running the second run used to clear the first run's caveat and hand it to whoever
            // read the second.
            Assert.Contains(check.Read(new[] { "snake" }, null).Notes,
                            note => note.Contains("still changing"));

            Assert.DoesNotContain(check.Read(new[] { "quiet" }, null).Notes,
                                  note => note.Contains("still changing"));

            // And it is on the run, so a project written now and read back tomorrow still has it.
            var json = runs.Find(FindingSource.FallHazard, "snake")!.ToJson();
            var back = CheckRunSpec.FromJson(json)!;

            Assert.Contains(back.Notes, note => note.Contains("still changing"));
        }

        [Fact]
        public void Severity_has_no_measure_where_the_run_allowed_no_gap()
        {
            var check = Check(new FindingRegister(), new CheckRunStore(), Model());

            var opening = new FloorOpening(1, 1, 4, 2, 0, 0, 2, 2, 400);
            var finding = FallHazardFindings.FromOpening(opening, null, null, 0, 0.0);

            // A limit of nothing is not a limit of zero: dividing by it would make every hazard
            // infinitely bad, and the run should not have been runnable in the first place.
            Assert.Null(check.Severity(finding));
            Assert.Equal("no measure", check.SeverityBand(check.Severity(finding)));

            // The three bands, each of which a reported hazard can reach.
            Assert.Equal("just over the gap allowed", check.SeverityBand(1.5));
            Assert.Equal("over the gap allowed", check.SeverityBand(2.0));
            Assert.Equal("far over the gap allowed", check.SeverityBand(4.0));
        }

        [Fact]
        public void The_gap_column_marks_a_gap_over_what_the_run_allowed()
        {
            var gap = FallColumns.Declare().Single(one => one.Key == FallHazardFindings.WidestGap);

            var opening = new FloorOpening(1, 1, 4, 2, 0, 0, 2, 2, 400);
            var over = FallHazardFindings.FromOpening(opening, null, null, 0, 0.5);
            var under = FallHazardFindings.FromOpening(opening, null, null, 0, 3.0);
            var nothing = FallHazardFindings.FromOpening(opening, null, null, 0, 0.0);

            Assert.True(gap.IsBad!(over));            // a 2 m gap where 0.5 m was allowed
            Assert.False(gap.IsBad!(under));          // the same gap where 3 m was allowed
            Assert.False(gap.IsBad!(nothing));        // and no limit is no judgement

            // The band the range filter groups on is the tenth the number falls in.
            Assert.Equal(2.0, gap.Number!(over)!.Value, 6);
            Assert.Equal("2.0", gap.Bucket!(2.04));
            Assert.Equal("1.9", gap.Bucket!(1.99));
            Assert.Equal("2 m", gap.Format!(2.0));
        }

        // --------------------------------------------------------------------- The settings

        [Theory]
        [InlineData("cell", "cell size")]
        [InlineData("band", "band")]
        [InlineData("limit", "gap that needs a handrail")]
        public void A_setting_of_nothing_or_less_refuses_the_run(string key, string said)
        {
            var settings = new FallSettings { Floors = CheckSource.Everything(), LevelMetres = 10.5 };

            Assert.Null(settings.Wrong());

            switch (key)
            {
                case "cell": settings.CellMetres = 0; break;
                case "band": settings.BandMetres = 0; break;
                default: settings.LimitMetres = -1; break;
            }

            Assert.Contains(said, settings.Wrong());
        }

        [Fact]
        public void A_run_keeps_the_settings_this_build_does_not_know_about()
        {
            var spec = new CheckRunSpec(FindingSource.FallHazard, "level3", "Level 3");

            // A newer build wrote this. Reading a run and writing it back must not drop it, or
            // opening a project in an older build would quietly delete half of somebody's run.
            spec.Settings["ceilingHeight"] = "2.4";

            var settings = FallSettings.From(spec);

            settings.LimitMetres = 0.4;
            settings.Write(spec);

            Assert.Equal("2.4", spec.Settings["ceilingHeight"]);
            Assert.Equal(0.4, FallSettings.From(spec).LimitMetres, 6);
        }

        [Fact]
        public void Deleting_a_run_takes_its_findings_with_it()
        {
            var register = new FindingRegister();
            var runs = new CheckRunStore();

            runs.Put(Spec("level3", "Level 3 openings"));

            var check = Check(register, runs, Model());

            Assert.True(check.Runner!.Run("level3", null));
            Assert.NotEmpty(register.From(FindingSource.FallHazard));

            Assert.True(check.Runner.Delete(new[] { "level3" }));
            Assert.Empty(register.From(FindingSource.FallHazard));
            Assert.Empty(check.Runs());
        }
    }
}
