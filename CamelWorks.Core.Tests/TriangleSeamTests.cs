using System;
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
    /// The geometry seam's contract: what a mesh is when it crosses, and what the weld is for.
    ///
    /// The host emits a fresh vertex for every corner of every triangle. Everything downstream of
    /// this seam clusters by shared vertex index, so the weld is part of the contract rather than
    /// an optimisation, and these tests are where that is held.
    /// </summary>
    public class TriangleSeamTests
    {
        private static readonly double[] UnweldedQuad =
        {
            0, 0, 0,  1, 0, 0,  1, 1, 0,      // first triangle, its own three corners
            0, 0, 0,  1, 1, 0,  0, 1, 0,      // second triangle, its own copies of two of them
        };

        private static readonly int[] TwoTriangles = { 0, 1, 2, 3, 4, 5 };

        [Fact]
        public void An_unwelded_quad_welds_to_four_vertices_with_two_shared()
        {
            var mesh = new TriangleMesh(UnweldedQuad, TwoTriangles);

            Assert.Equal(6, mesh.Vertices);

            var welded = mesh.Weld();

            Assert.Equal(4, welded.Vertices);
            Assert.Equal(2, welded.Triangles);

            var first = new HashSet<int> { welded.Indices[0], welded.Indices[1], welded.Indices[2] };
            var second = new HashSet<int> { welded.Indices[3], welded.Indices[4], welded.Indices[5] };

            Assert.Equal(2, first.Intersect(second).Count());
        }

        [Fact]
        public void The_seam_welds_before_it_hands_a_mesh_over()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");
            var item = document.AddItem(model, "Slab");

            document.SetMesh(item.Key, UnweldedQuad, TwoTriangles);

            Assert.True(document.TryRead(item.Key, out var mesh));
            Assert.NotNull(mesh);
            Assert.Equal(4, mesh!.Vertices);
        }

        [Fact]
        public void A_source_that_forgets_to_weld_is_welded_anyway()
        {
            // The failure the weld exists to stop: a floor arrives as hundreds of small triangles,
            // unwelded no two share a vertex index, every cluster is one triangle, every cluster is
            // under the minimum area, and a building full of floors reports no walkable surface at
            // all. The seam's contract is that a mesh is welded before it is handed over;
            // detection welds again rather than trusting it, because the failure is silent.
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");
            var item = document.AddItem(model, "Slab");

            var plate = Plates.Loose(Plates.Flat(0, 0, 4, 4, 3.0, 0.2));

            document.SetMesh(item.Key, plate.Positions, plate.Indices);

            document.WeldsOnRead = false;
            Assert.True(document.TryRead(item.Key, out var raw));

            // What an unwelded mesh is: a fresh vertex for every corner of every triangle.
            Assert.Equal(raw!.Triangles * 3, raw.Vertices);

            var found = WalkableSurfaces.Detect(
                new[] { new KeyValuePair<ElementKey, TriangleMesh>(item.Key, raw) },
                new WalkableOptions());

            Assert.Single(found);
            Assert.Equal(16.0, found[0].AreaSquareMetres, 6);

            // And the clustering really did need it: the same triangles, clustered as they arrived,
            // are one triangle per cluster and every one of them under the minimum area.
            var apart = new WalkableOptions { MinPatchAreaSquareMetres = 0.5 };

            document.WeldsOnRead = true;
            Assert.True(document.TryRead(item.Key, out var welded));
            Assert.True(welded!.Vertices < raw.Vertices);
            Assert.Single(WalkableSurfaces.Detect(
                new[] { new KeyValuePair<ElementKey, TriangleMesh>(item.Key, welded) }, apart));
        }

        [Fact]
        public void A_meshs_box_is_seeded_from_its_first_vertex_not_from_the_origin()
        {
            var mesh = new TriangleMesh(
                new double[] { 400000, 300000, 12, 400004, 300000, 12, 400004, 300004, 12 },
                new[] { 0, 1, 2 });

            Assert.Equal(400000, mesh.Bounds.MinX, 6);
            Assert.Equal(300000, mesh.Bounds.MinY, 6);
            Assert.Equal(12, mesh.Bounds.MinZ, 6);
        }

        [Fact]
        public void A_position_a_float_cannot_hold_crosses_the_seam_intact()
        {
            // At site coordinates a float ulp is about 31 mm, which is coarser than the 0.1 mm weld
            // tolerance and the 50 mm elevation bucket. A seam that narrowed to float would put two
            // vertices half a millimetre apart on the same float and weld them into one.
            const double x = 400000.0;
            const double near = 400000.0005;

            Assert.Equal((float)x, (float)near);   // the same float, which is the whole problem
            Assert.NotEqual(x, near);

            var mesh = new TriangleMesh(
                new[] { x, 300000, 12, near, 300000, 12, x, 300004, 12, near, 300004, 12 },
                new[] { 0, 1, 2, 1, 3, 2 });

            var welded = mesh.Weld();

            Assert.Equal(4, welded.Vertices);
            Assert.Equal(near, welded.Bounds.MaxX);
            Assert.Equal(near - x, welded.Bounds.MaxX - welded.Bounds.MinX);
        }

        [Fact]
        public void An_empty_mesh_has_no_box_rather_than_one_at_the_origin()
        {
            // A zero box at the origin is a place, and every containment test against it answers
            // about that place — which on a site-coordinate model is kilometres from the geometry.
            Assert.Equal(0, TriangleMesh.Empty.Vertices);
            Assert.Throws<InvalidOperationException>(() => TriangleMesh.Empty.Bounds);
        }

        [Fact]
        public void A_refused_element_is_counted_and_falls_back_to_its_box()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");

            var good = document.AddItem(model, "Slab", bounds: new BoundingBox(0, 0, 0, 4, 4, 0.2));
            var bad = document.AddItem(model, "Awkward", bounds: new BoundingBox(10, 10, 3, 14, 14, 3.2));

            document.SetSlab(good.Key, 0, 0, 0, 4, 4, 0.2);
            document.RefuseTriangles(bad.Key);

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                                new WalkableOptions(), diagnostics);

            Assert.Equal(2, diagnostics.ElementsRead);
            Assert.Equal(1, diagnostics.ElementsRefused);
            Assert.Equal(2, patches.Count);
            Assert.Contains(diagnostics.Notes, note => note.Contains("bounding-box tops"));

            // The refused one still produced a surface, taken from the top of its box.
            var refused = patches.Single(p => p.Source == bad.Key);
            Assert.Equal(3.2, refused.TopMetres, 6);
        }

        [Fact]
        public void An_element_that_carries_no_triangles_is_not_a_refusal()
        {
            // The two answers a source must keep apart. A grouping node, a line and an annotation
            // carry no triangles and are not refusals: counting them as refusals degrades each one
            // to its bounding-box top, and every duct, ceiling and grouped node over the minimum
            // area then comes back as a detected walkable surface.
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");

            var slab = document.AddItem(model, "Slab", bounds: new BoundingBox(0, 0, 0, 4, 4, 0.2));
            var group = document.AddItem(model, "Duct run", bounds: new BoundingBox(10, 10, 3, 14, 14, 3.2));

            document.SetSlab(slab.Key, 0, 0, 0, 4, 4, 0.2);

            // Nothing set on the group, which is how the seam says "read, and carries none".
            Assert.True(document.TryRead(group.Key, out var none));
            Assert.Equal(0, none!.Triangles);

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                                new WalkableOptions(), diagnostics);

            Assert.Equal(0, diagnostics.ElementsRefused);
            Assert.Equal(slab.Key, Assert.Single(patches).Source);
            Assert.DoesNotContain(diagnostics.Notes, note => note.Contains("would not give triangles"));
        }

        [Fact]
        public void A_seat_with_no_triangles_degrades_to_boxes_and_says_so()
        {
            var document = new FakeDocument
            {
                CanReadTriangles = false,
                Unavailable = "this seat's primitive reader is not installed",
            };

            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");
            var item = document.AddItem(model, "Slab", bounds: new BoundingBox(0, 0, 0, 6, 5, 0.2));

            document.SetSlab(item.Key, 0, 0, 0, 6, 5, 0.2);

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                                new WalkableOptions(), diagnostics);

            Assert.False(diagnostics.UsedTriangles);

            var note = Assert.Single(diagnostics.Notes);

            // One sentence, and it is the seat's. "n elements would not give triangles" for a seat
            // that can read none of them says the model is the problem when the seat is.
            Assert.Contains("No triangles on this seat", note, StringComparison.Ordinal);
            Assert.Contains("primitive reader is not installed", note, StringComparison.Ordinal);
            Assert.Contains("bounding-box top", note, StringComparison.Ordinal);
            Assert.DoesNotContain("would not give triangles", note, StringComparison.Ordinal);

            // The box's top face, which is the answer the check gave before it read triangles.
            Assert.Single(patches);
            Assert.Equal(30.0, patches[0].AreaSquareMetres, 6);
            Assert.Equal(0.2, patches[0].TopMetres, 6);
        }

        [Fact]
        public void A_seat_that_gives_up_part_way_through_is_the_seat_and_not_the_elements()
        {
            // Nothing can know a seat has no primitive reader until something has been refused, so
            // the whole-seat answer arrives during the sweep. A run that read CanReadTriangles once
            // at the top would spend the rest of that sweep counting per-element refusals for a
            // seat-wide fault, and the whole-seat path would never be reached in production.
            var document = new FakeDocument { RefusalsBeforeTheSeatIsUnable = 3 };
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");

            for (var i = 0; i < 6; i++)
            {
                var item = document.AddItem(model, "Element " + i,
                                            bounds: new BoundingBox(i * 10, 0, 3, (i * 10) + 4, 4, 3.2));

                document.RefuseTriangles(item.Key);
            }

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                                new WalkableOptions(), diagnostics);

            Assert.False(document.CanReadTriangles);
            Assert.False(diagnostics.UsedTriangles);
            Assert.Contains(diagnostics.Notes,
                            note => note.Contains("No triangles on this seat"));
            Assert.DoesNotContain(diagnostics.Notes,
                                  note => note.Contains("would not give triangles"));

            // Every element still produced its box top, which is the coarse answer, not nothing.
            Assert.Equal(6, patches.Count);
        }

        [Fact]
        public void Boxes_are_scaled_into_metres_on_the_way_in()
        {
            // The host reports geometry in the document's own unit; triangles cross the seam in
            // metres already, and boxes do not.
            var document = new FakeDocument { CanReadTriangles = false };
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");

            document.AddItem(model, "Slab", bounds: new BoundingBox(0, 0, 0, 4000, 4000, 200));

            var patches = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 0.001,
                                                new WalkableOptions());

            Assert.Single(patches);
            Assert.Equal(16.0, patches[0].AreaSquareMetres, 6);
            Assert.Equal(0.2, patches[0].TopMetres, 6);
        }
    }
}
