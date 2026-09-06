using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;

namespace CamelWorks.Nav
{
    /// <summary>
    /// What one element's geometry read did.
    ///
    /// Refusal and emptiness are different answers and the caller acts differently on them: a
    /// refused element degrades to its bounding box and is counted, and an element that carries no
    /// triangles is simply an element with no triangles. Answering the same for both turns every
    /// grouping node, line and annotation in the model into a box-top surface.
    /// </summary>
    public enum GeometryRead
    {
        /// <summary>Triangles were read.</summary>
        Triangles,

        /// <summary>The element was walked and carries no triangles: a group, a line, an annotation.</summary>
        NoTriangles,

        /// <summary>The host would not walk this element. The caller degrades it to its box.</summary>
        Refused,
    }

    /// <summary>
    /// Triangles out of the model.
    ///
    /// GenerateSimplePrimitives is the only way in. Navisworks exposes no bulk vertex, triangle or
    /// mesh buffer in either the .NET API or the COM one, so this per-vertex callback is it, and it
    /// is most of the wall clock on any export. There is no faster path; the work is to make each
    /// callback cheap rather than to avoid the callbacks.
    ///
    /// The COM side is reached through <see cref="ComApiBridge"/>, the documented bridge between a
    /// <see cref="ModelItem"/> and the fragment tree underneath it.
    ///
    /// Main thread only, like everything that touches the document.
    /// </summary>
    public sealed class NavGeometry
    {
        private readonly NavDocument _document;
        private readonly double _scale;

        /// <summary>Wrap a document.</summary>
        /// <param name="document">The open document.</param>
        public NavGeometry(NavDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _scale = NavUnits.MetresPerUnit(document.Document.Units);
        }

        /// <summary>
        /// One element's triangles, in metres, in world coordinates.
        /// </summary>
        /// <param name="item">
        /// The element, as the seam hands it over. Taken as the seam type rather than the host's
        /// own, because that is what a traversal yields and what every caller already holds:
        /// asking a UI screen to produce an Autodesk.Navisworks.Api.ModelItem would push the host
        /// type back across the seam this whole assembly exists to keep on one side of it.
        /// </param>
        /// <param name="name">What the node should be called.</param>
        /// <returns>A mesh, or null when the element was refused or carries no triangles.</returns>
        public GltfMesh? Read(IModelItem? item, string name)
        {
            if (Walk(item, out var sink) != GeometryRead.Triangles || sink == null) return null;

            // Narrowed here and only here. glTF stores positions as floats, so the file format
            // asks for the narrowing; the seam does not, and at site coordinates a float ulp is
            // about 31 mm, which is wider than the weld tolerance and the elevation bucket that
            // surface detection works to.
            var positions = new float[sink.Positions.Count];

            for (var i = 0; i < positions.Length; i++) positions[i] = (float)sink.Positions[i];

            return new GltfMesh(name, positions, sink.Indices);
        }

        /// <summary>
        /// One element's triangles for the geometry seam, in metres, in world coordinates, welded.
        ///
        /// Separate from <see cref="Read"/> because the seam needs the three
        /// answers apart: a refusal degrades the element to its bounding box, an element with no
        /// triangles does not, and the glTF writer wants neither distinction and a float buffer.
        /// </summary>
        /// <param name="item">The element, as the seam hands it over.</param>
        /// <param name="mesh">
        /// The triangles, <see cref="TriangleMesh.Empty"/> when the element carries none, or null
        /// when the host refused it.
        /// </param>
        public GeometryRead ReadTriangles(IModelItem? item, out TriangleMesh? mesh)
        {
            var outcome = Walk(item, out var sink);

            if (outcome == GeometryRead.Refused) { mesh = null; return outcome; }

            // Welded on this side of the seam, which is what ITriangleSource asks for: everything
            // downstream clusters by shared vertex index and this sink emits a fresh vertex per
            // triangle corner.
            mesh = outcome == GeometryRead.NoTriangles
                ? TriangleMesh.Empty
                : new TriangleMesh(sink!.Positions, sink.Indices).Weld();

            return outcome;
        }

        /// <summary>
        /// Walk one element's fragments into a sink.
        /// </summary>
        /// <param name="item">The element, as the seam hands it over.</param>
        /// <param name="sink">What the walk collected, or null when there was nothing to walk.</param>
        private GeometryRead Walk(IModelItem? item, out TriangleSink? sink)
        {
            // Unwrapped rather than looked up by key: the traversal is already holding the host
            // item, and resolving it again through the document's index would be a dictionary hit
            // per element for something already in hand.
            var host = (item as NavModelItem)?.Item;

            sink = null;

            if (host == null) return GeometryRead.Refused;

            var collecting = new TriangleSink(_scale);

            try
            {
                var selection = new ModelItemCollection { host };
                var com = ComApiBridge.ToInwOpSelection(selection);

                foreach (InwOaPath3 path in com.Paths())
                {
                    foreach (InwOaFragment3 fragment in path.Fragments())
                    {
                        collecting.Transform = Matrix(fragment);

                        // eNONE: this sink reads only the coordinate, so asking for normals makes
                        // Navisworks generate and marshal a per-vertex field that is then thrown
                        // away — on a large model that is tens of millions of wasted marshals.
                        fragment.GenerateSimplePrimitives(nwEVertexProperty.eNONE, collecting);
                    }
                }
            }
            catch (Exception)
            {
                // One element the COM layer will not walk. Counted by the caller and degraded to a
                // box, rather than taking a whole export down at object 40,000 of 200,000.
                return GeometryRead.Refused;
            }

            sink = collecting;

            return collecting.Triangles == 0 ? GeometryRead.NoTriangles : GeometryRead.Triangles;
        }

        /// <summary>
        /// The fragment's local-to-world matrix, or null for identity.
        /// </summary>
        private static double[]? Matrix(InwOaFragment3 fragment)
        {
            try
            {
                var transform = (InwLTransform3f3)fragment.GetLocalToWorldMatrix();
                var values = (Array)transform.Matrix;
                var lower = values.GetLowerBound(0);

                var matrix = new double[16];

                for (var i = 0; i < 16; i++)
                    matrix[i] = Convert.ToDouble(values.GetValue(lower + i));

                return matrix;
            }
            catch (Exception)
            {
                // No transform is identity, which is right far more often than it is wrong: a
                // fragment with no placement sits where its coordinates say it does.
                return null;
            }
        }

        /// <summary>
        /// The callback Navisworks drives, one primitive at a time.
        ///
        /// Only triangles are kept. Lines, points and snap points are the model's annotation and
        /// snapping furniture, and a mesh format has nowhere to put them.
        /// </summary>
        private sealed class TriangleSink : InwSimplePrimitivesCB
        {
            private readonly double _scale;
            private readonly float[] _coordinates = new float[3];

            internal TriangleSink(double scale) => _scale = scale;

            /// <summary>
            /// Vertex positions, three doubles each, in metres.
            ///
            /// Doubles because these are world coordinates. At site coordinates a float ulp is
            /// about 31 mm, which is coarser than both the 0.1 mm weld tolerance and the 50 mm
            /// elevation bucket the surface detector bands on — on exactly the model class the
            /// site-coordinate tests are about. Core is doubles throughout; only the glTF writer
            /// narrows, because its file format is float.
            /// </summary>
            internal List<double> Positions { get; } = new List<double>();

            /// <summary>Triangle indices, three per triangle.</summary>
            internal List<int> Indices { get; } = new List<int>();

            /// <summary>How many triangles have arrived.</summary>
            internal int Triangles { get; private set; }

            /// <summary>The current fragment's local-to-world matrix, column-major, or null.</summary>
            internal double[]? Transform { get; set; }

            /// <inheritdoc />
            public void Triangle(InwSimpleVertex v1, InwSimpleVertex v2, InwSimpleVertex v3)
            {
                Indices.Add(Add(v1));
                Indices.Add(Add(v2));
                Indices.Add(Add(v3));
                Triangles++;
            }

            /// <inheritdoc />
            public void Line(InwSimpleVertex v1, InwSimpleVertex v2) { }

            /// <inheritdoc />
            public void Point(InwSimpleVertex v1) { }

            /// <inheritdoc />
            public void SnapPoint(InwSimpleVertex v1) { }

            private int Add(InwSimpleVertex vertex)
            {
                // v.coord arrives as a SAFEARRAY, usually of Single and usually one-based. A type
                // check rather than a try/catch: a sink is built per element, so an exception-driven
                // "learn once" flag never gets to stay learned and every element would pay a real
                // CLR exception on its first vertex.
                var raw = (Array)vertex.coord;
                var lower = raw.GetLowerBound(0);

                double x, y, z;

                if (raw is float[])
                {
                    Array.Copy(raw, lower, _coordinates, 0, 3);
                    x = _coordinates[0];
                    y = _coordinates[1];
                    z = _coordinates[2];
                }
                else
                {
                    x = Convert.ToDouble(raw.GetValue(lower));
                    y = Convert.ToDouble(raw.GetValue(lower + 1));
                    z = Convert.ToDouble(raw.GetValue(lower + 2));
                }

                var m = Transform;

                if (m != null)
                {
                    // Column-major: world = M * [x y z 1].
                    var wx = (m[0] * x) + (m[4] * y) + (m[8] * z) + m[12];
                    var wy = (m[1] * x) + (m[5] * y) + (m[9] * z) + m[13];
                    var wz = (m[2] * x) + (m[6] * y) + (m[10] * z) + m[14];

                    x = wx; y = wy; z = wz;
                }

                // Model units to metres, folded in here rather than in a second pass: a second pass
                // over a six-million-triangle model is eighteen million multiplications and a
                // second list to hold the answer.
                if (_scale != 1.0) { x *= _scale; y *= _scale; z *= _scale; }

                Positions.Add(x);
                Positions.Add(y);
                Positions.Add(z);

                return (Positions.Count / 3) - 1;
            }
        }
    }
}
