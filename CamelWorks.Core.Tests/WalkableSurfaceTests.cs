using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Detecting the surfaces people stand on, over synthetic meshes.
    ///
    /// The cases are the ported engine's own, plus the three faults this port does not carry: a
    /// box grown from the world origin, a prism swept from a bounding rectangle, and a result with
    /// no measurement on it.
    /// </summary>
    public class WalkableSurfaceTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath(ElementKey.ScopeOf(@"C:\jobs\str.nwc"), @"C:\jobs\str.nwc", name);

        private static KeyValuePair<ElementKey, TriangleMesh> Mesh(string name, TriangleMesh mesh) =>
            new KeyValuePair<ElementKey, TriangleMesh>(Key(name), mesh);

        [Fact]
        public void A_slab_gives_one_patch_and_its_underside_gives_none()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");
            var item = document.AddItem(model, "Slab");

            document.SetSlab(item.Key, 0, 0, 0, 6, 5, 0.2);
            document.TryRead(item.Key, out var mesh);

            var patches = WalkableSurfaces.Detect(
                new[] { new KeyValuePair<ElementKey, TriangleMesh>(item.Key, mesh!) },
                new WalkableOptions());

            Assert.Single(patches);
            Assert.Equal(30.0, patches[0].AreaSquareMetres, 6);
            Assert.Equal(0.2, patches[0].TopMetres, 6);
        }

        [Fact]
        public void A_patch_at_site_coordinates_has_a_footprint_at_site_coordinates()
        {
            // The fault this port does not carry. The engine it came from builds two of its boxes
            // by growing an empty one, and an empty box is a point at the world origin: the
            // footprint then reaches from (0,0,0) to the geometry and the containment test answers
            // yes for everything in between.
            var plate = Plates.Flat(400000, 300000, 400010, 300008, 12.0);

            var patches = WalkableSurfaces.Detect(new[] { Mesh("Slab", plate) }, new WalkableOptions());

            Assert.Single(patches);

            var footprint = patches[0].Footprint;

            Assert.Equal(400000, footprint.MinX, 6);
            Assert.Equal(300000, footprint.MinY, 6);
            Assert.Equal(12.0, footprint.MinZ, 6);
            Assert.Equal(400010, footprint.MaxX, 6);
            Assert.Equal(300008, footprint.MaxY, 6);
        }

        [Fact]
        public void A_cluster_under_the_minimum_area_is_dropped_and_counted()
        {
            var plate = Plates.Flat(0, 0, 0.2, 0.2, 3.0, 0.2);   // 0.04 m², under the 0.05 default
            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Detect(new[] { Mesh("Sill", plate) },
                                                  new WalkableOptions(), diagnostics);

            Assert.Empty(patches);
            Assert.Equal(1, diagnostics.PatchesUnderMinimumArea);
        }

        [Fact]
        public void A_finish_forty_millimetres_over_a_slab_dedupes_to_one_patch()
        {
            var structure = Plates.Flat(0, 0, 6, 5, 3.000);
            var finish = Plates.Flat(0, 0, 6, 5, 3.040);

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Detect(
                new[] { Mesh("Slab", structure), Mesh("Screed", finish) },
                new WalkableOptions(), diagnostics);

            Assert.Single(patches);
            Assert.Equal(3.040, patches[0].TopMetres, 6);
            Assert.Equal(1, diagnostics.PatchesMerged);
        }

        [Fact]
        public void A_storey_above_does_not_stop_the_pair_below_it_deduping()
        {
            // The dedupe walks the surfaces it has kept from the closest one up. Walking them from
            // the highest down and stopping at the first one out of reach stops at the roof.
            var upper = Plates.Flat(0, 0, 6, 5, 6.600);
            var structure = Plates.Flat(0, 0, 6, 5, 3.000);
            var finish = Plates.Flat(0, 0, 6, 5, 3.040);

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Detect(
                new[] { Mesh("L02", upper), Mesh("Slab", structure), Mesh("Screed", finish) },
                new WalkableOptions(), diagnostics);

            Assert.Equal(2, patches.Count);
            Assert.Equal(1, diagnostics.PatchesMerged);
            Assert.DoesNotContain(patches, patch => patch.TopMetres == 3.000);
        }

        [Fact]
        public void Two_floors_a_storey_apart_are_two_patches()
        {
            var lower = Plates.Flat(0, 0, 6, 5, 0.0);
            var upper = Plates.Flat(0, 0, 6, 5, 3.6);

            var patches = WalkableSurfaces.Detect(
                new[] { Mesh("L01", lower), Mesh("L02", upper) }, new WalkableOptions());

            Assert.Equal(2, patches.Count);
        }

        [Fact]
        public void A_steep_face_is_not_walked_on()
        {
            // A 30-degree roof plane, against the 10-degree default.
            var pitched = new TriangleMesh(
                new double[] { 0, 0, 0, 4, 0, 0, 4, 4, 2.31, 0, 4, 2.31 },
                new[] { 0, 1, 2, 0, 2, 3 });

            Assert.Empty(WalkableSurfaces.Detect(new[] { Mesh("Roof", pitched) }, new WalkableOptions()));

            // And is walked on when somebody says a ramp that steep counts.
            var steeper = WalkableSurfaces.Detect(new[] { Mesh("Roof", pitched) },
                                                  new WalkableOptions { MaxSlopeDegrees = 35 });

            Assert.Single(steeper);
        }

        [Fact]
        public void The_up_axis_is_taken_from_where_the_horizontal_area_is()
        {
            var zUp = Plates.Flat(0, 0, 10, 10, 0);

            Assert.Equal(2, UpAxis.Detect(new[] { zUp }, 10, out var areas));
            Assert.Equal(100.0, areas[2], 6);

            // The same plate on its side, as a Y-up source arrives.
            var yUp = new TriangleMesh(
                new double[] { 0, 0, 0, 10, 0, 0, 10, 0, 10, 0, 0, 10 },
                new[] { 0, 1, 2, 0, 2, 3 });

            Assert.Equal(1, UpAxis.Detect(new[] { yUp }, 10, out _));
        }

        [Fact]
        public void Nothing_facing_any_axis_still_answers_Z()
        {
            // A face 55 degrees off all three axes, so no axis collects any area. Z is the seed:
            // it is what nearly every AEC model uses, and it is also what a tie answers.
            var corner = new TriangleMesh(
                new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, new[] { 0, 1, 2 });

            Assert.Equal(2, UpAxis.Detect(new[] { corner }, 10, out var areas));
            Assert.Equal(0.0, areas[0], 6);
            Assert.Equal(0.0, areas[1], 6);
            Assert.Equal(0.0, areas[2], 6);
        }

        [Fact]
        public void The_same_surface_gets_the_same_id_in_two_runs()
        {
            var plate = Plates.Flat(0, 0, 6, 5, 3.0);

            var first = WalkableSurfaces.Detect(new[] { Mesh("Slab", plate) }, new WalkableOptions());
            var second = WalkableSurfaces.Detect(new[] { Mesh("Slab", plate) }, new WalkableOptions());

            Assert.Equal(first[0].Id, second[0].Id);
        }

        [Fact]
        public void Two_surfaces_that_derive_the_same_id_are_told_apart()
        {
            // Same rectangle, same elevation, two elements: the derived id collides, and an id
            // that names two surfaces detaches a decision from the surface it was made about.
            var plate = Plates.Flat(0, 0, 6, 5, 3.0);

            var patches = WalkableSurfaces.Detect(
                new[] { Mesh("Slab A", plate), Mesh("Slab B", plate) },
                new WalkableOptions { ElevationBucketMetres = 0.050 });

            // The dedupe keeps one of an identical stacked pair, so this asks the namer directly
            // with the pair kept apart by level.
            var options = new WalkableOptions();
            options.Levels[Key("Slab A")] = "Level 01";
            options.Levels[Key("Slab B")] = "Level 02";

            var levelled = WalkableSurfaces.Detect(
                new[] { Mesh("Slab A", plate), Mesh("Slab B", plate) }, options);

            Assert.Single(patches);
            Assert.Equal(2, levelled.Count);
            Assert.NotEqual(levelled[0].Id, levelled[1].Id);
        }

        [Fact]
        public void Stairs_are_left_out_when_the_toggle_is_on()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");

            var slab = document.AddItem(model, "Slab L01", "Floors");
            var tread = document.AddItem(model, "Tread 4", "Stairs");

            document.SetSlab(slab.Key, 0, 0, 3.0, 6, 5, 0.2);
            document.SetSlab(tread.Key, 20, 20, 3.4, 1.2, 0.28, 0.05);

            var both = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                             new WalkableOptions());

            Assert.Equal(2, both.Count);

            var diagnostics = new WalkableDiagnostics();

            var without = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                                new WalkableOptions { ExcludeStairs = true }, diagnostics);

            Assert.Single(without);
            Assert.Equal(slab.Key, without[0].Source);
            Assert.Contains(diagnostics.Notes, note => note.Contains("category and type"));
        }

        [Fact]
        public void Preferring_floor_categories_takes_only_the_elements_named_as_floors()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Mixed", @"C:\jobs\str.nwc");

            var slab = document.AddItem(model, "Slab L01", "Floors");
            var desk = document.AddItem(model, "Desk 12", "Furniture");

            document.SetSlab(slab.Key, 0, 0, 3.0, 6, 5, 0.2);
            document.SetSlab(desk.Key, 20, 20, 3.0, 1.6, 0.8, 0.03);

            var geometric = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument,
                                                  1.0, new WalkableOptions());

            // The honest default: the desk top is up-facing and over the minimum area, so it is a
            // walkable surface as far as geometry is concerned.
            Assert.Equal(2, geometric.Count);

            var named = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                              new WalkableOptions { PreferFloorCategories = true });

            Assert.Single(named);
            Assert.Equal(slab.Key, named[0].Source);
        }

        // ---- The prism -----------------------------------------------------------------

        private static HeadroomElement Over(string id, double x, double y, double z,
                                            double sizeX = 2, double sizeY = 2, double sizeZ = 0.4) =>
            new HeadroomElement(id)
            {
                MinX = x, MaxX = x + sizeX,
                MinY = y, MaxY = y + sizeY,
                MinZ = z, MaxZ = z + sizeZ,
            };

        /// <summary>An L: ten by four along the bottom, four by six up the left.</summary>
        private static TriangleMesh Elled() =>
            Plates.Shaped(0, 0, 10, 10, 3.0, 1.0, (x, y) => y < 4 || x < 4);

        [Fact]
        public void An_L_shaped_floor_does_not_sweep_its_missing_corner()
        {
            // The second fault this port does not carry. The engine it came from extrudes the
            // patch's bounding rectangle, so everything standing in the missing corner of an L, the
            // courtyard of a ring or a stair void is reported as low headroom over a floor nobody
            // can stand on.
            var patches = WalkableSurfaces.Detect(new[] { Mesh("Slab", Elled()) }, new WalkableOptions());

            Assert.Single(patches);

            var inside = Headroom.Check(patches, new[] { Over("duct", 1, 1, 4.5) }, 2.100);
            var outside = Headroom.Check(patches, new[] { Over("duct", 6, 6, 4.5) }, 2.100);

            Assert.Single(inside.Spans);
            Assert.Empty(outside.Spans);

            // And the rectangle does cover the corner, which is why the outline has to be asked.
            Assert.True(patches[0].Footprint.MaxX >= 10 && patches[0].Footprint.MaxY >= 10);
        }

        [Fact]
        public void The_worst_point_is_on_the_surface_it_is_over()
        {
            // An obstruction that straddles the missing corner and one leg of the L. The centre of
            // the two rectangles' overlap is off the floor; a place nobody can stand is a place
            // nobody can go and look at.
            var patches = WalkableSurfaces.Detect(new[] { Mesh("Slab", Elled()) }, new WalkableOptions());

            var found = Headroom.Check(patches, new[] { Over("duct", 3, 3, 4.4, sizeX: 4, sizeY: 4) },
                                       2.100);

            Assert.Single(found.Spans);

            var span = found.Spans[0];
            var outline = patches[0].Outline;

            Assert.Contains(outline, triangle => triangle.Holds(span.X, span.Y));
        }

        [Fact]
        public void A_finding_carries_the_clear_height_and_where_it_is()
        {
            // The third fault this port does not carry: the engine it came from reports which band
            // an element is in and nothing else — no clear height, no depth, no point — which
            // leaves a list that cannot be sorted or prioritised.
            var patches = WalkableSurfaces.Detect(new[] { Mesh("Slab", Plates.Flat(0, 0, 10, 8, 3.0)) },
                                                  new WalkableOptions());

            var found = Headroom.Check(patches, new[] { Over("duct", 4, 3, 4.8) }, 2.100);

            Assert.Single(found.Spans);

            var span = found.Spans[0];

            Assert.Equal(1.8, span.Clear, 6);
            Assert.Equal(5.0, span.X, 6);
            Assert.Equal(4.0, span.Y, 6);
            Assert.Equal(3.0, span.Z, 6);
        }

        [Fact]
        public void An_element_that_never_reaches_into_the_prism_is_not_a_finding()
        {
            var patches = WalkableSurfaces.Detect(new[] { Mesh("Slab", Plates.Flat(0, 0, 10, 8, 3.0)) },
                                                  new WalkableOptions());

            var high = Headroom.Check(patches, new[] { Over("duct", 4, 3, 5.6) }, 2.100);

            Assert.Empty(high.Spans);
        }

        [Fact]
        public void The_surfaces_a_run_measured_are_counted()
        {
            var patches = WalkableSurfaces.Detect(
                new[] { Mesh("A", Plates.Flat(0, 0, 10, 8, 3.0)), Mesh("B", Plates.Flat(0, 0, 10, 8, 6.6)) },
                new WalkableOptions());

            var found = Headroom.Check(patches, new[] { Over("duct", 4, 3, 4.8) }, 2.100);

            Assert.Equal(2, found.Floors);
            Assert.Equal(2.100, found.Minimum, 6);
        }

        [Fact]
        public void Total_detected_area_is_what_the_screen_reports()
        {
            var patches = WalkableSurfaces.Detect(
                new[] { Mesh("A", Plates.Flat(0, 0, 10, 8, 3.0)), Mesh("B", Plates.Flat(0, 0, 6, 5, 6.6)) },
                new WalkableOptions());

            Assert.Equal(110.0, patches.Sum(p => p.AreaSquareMetres), 6);
        }
    }
}
