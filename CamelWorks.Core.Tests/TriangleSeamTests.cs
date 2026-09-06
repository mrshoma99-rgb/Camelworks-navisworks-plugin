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
        public void Without_the_weld_a_tessellated_floor_detects_as_nothing()
        {
            // The failure the weld exists to stop, stated as a test. A floor arrives as hundreds of
            // small triangles; unwelded, no two share an index, every cluster is one triangle,
            // every cluster is under the minimum area, and a building full of floors reports no
            // walkable surface at all.
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");
            var item = document.AddItem(model, "Slab");

            var plate = Plates.Loose(Plates.Flat(0, 0, 4, 4, 3.0, 0.2));

            document.SetMesh(item.Key, plate.Positions, plate.Indices);

            document.WeldsOnRead = false;
            Assert.True(document.TryRead(item.Key, out var raw));

            var unwelded = WalkableSurfaces.Detect(
                new[] { new KeyValuePair<ElementKey, TriangleMesh>(item.Key, raw!) },
                new WalkableOptions());

            Assert.Empty(unwelded);

            document.WeldsOnRead = true;
            Assert.True(document.TryRead(item.Key, out var welded));

            var found = WalkableSurfaces.Detect(
                new[] { new KeyValuePair<ElementKey, TriangleMesh>(item.Key, welded!) },
                new WalkableOptions());

            Assert.Single(found);
            Assert.Equal(16.0, found[0].AreaSquareMetres, 6);
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
        public void Singles_widen_into_a_mesh()
        {
            var mesh = TriangleMesh.FromSingles(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f },
                                                new[] { 0, 1, 2 });

            Assert.Equal(3, mesh.Vertices);
            Assert.Equal(1, mesh.Triangles);
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
        public void A_seat_with_no_triangles_degrades_to_boxes_and_says_so()
        {
            var document = new FakeDocument
            {
                CanReadTriangles = false,
                Unavailable = "this seat's primitive reader refused",
            };

            var model = document.AddModel("Structure", @"C:\jobs\str.nwc");
            var item = document.AddItem(model, "Slab", bounds: new BoundingBox(0, 0, 0, 6, 5, 0.2));

            document.SetSlab(item.Key, 0, 0, 0, 6, 5, 0.2);

            var diagnostics = new WalkableDiagnostics();

            var patches = WalkableSurfaces.Read(document, document, TraversalScope.WholeDocument, 1.0,
                                                new WalkableOptions(), diagnostics);

            Assert.False(diagnostics.UsedTriangles);
            Assert.Contains(diagnostics.Notes, note => note.Contains("No triangles on this seat"));
            Assert.Contains(diagnostics.Notes, note => note.Contains("refused"));

            // The box's top face, which is the answer the check gave before it read triangles.
            Assert.Single(patches);
            Assert.Equal(30.0, patches[0].AreaSquareMetres, 6);
            Assert.Equal(0.2, patches[0].TopMetres, 6);
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
