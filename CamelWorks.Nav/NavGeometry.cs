using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Report;

namespace CamelWorks.Nav
{
    /// <summary>
    /// Triangles out of the model.
    ///
    /// <b>GenerateSimplePrimitives is the only way in.</b> Navisworks exposes no bulk vertex,
    /// triangle or mesh buffer in either the .NET API or the COM one — this per-vertex callback is
    /// it, and it is most of the wall clock on any export. There is no faster path to find; the
    /// work is to make each callback cheap, not to avoid the callbacks.
    ///
    /// The COM side is reached through <see cref="ComApiBridge"/>, which is the documented bridge
    /// between a <see cref="ModelItem"/> and the fragment tree underneath it.
    ///
    /// <b>Main thread only</b>, like everything that touches the document.
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
        /// <returns>A mesh, or null when the element carries no triangles.</returns>
        public GltfMesh? Read(IModelItem? item, string name)
        {
            // Unwrapped rather than looked up by key: the traversal is already holding the host
            // item, and resolving it again through the document's index would be a dictionary hit
            // per element for something already in hand.
            var host = (item as NavModelItem)?.Item;

            if (host == null) return null;

            var sink = new TriangleSink(_scale);

            try
            {
                var selection = new ModelItemCollection { host };
                var com = ComApiBridge.ToInwOpSelection(selection);

                foreach (InwOaPath3 path in com.Paths())
                {
                    foreach (InwOaFragment3 fragment in path.Fragments())
                    {
                        sink.Transform = Matrix(fragment);

                        // eNONE: this sink reads only the coordinate, so asking for normals makes
                        // Navisworks generate and marshal a per-vertex field that is then thrown
                        // away — on a large model that is tens of millions of wasted marshals.
                        fragment.GenerateSimplePrimitives(nwEVertexProperty.eNONE, sink);
                    }
                }
            }
            catch (Exception)
            {
                // One element the COM layer will not walk. Reported as "no triangles" and counted
                // by the caller, rather than taking a whole export down at object 40,000 of 200,000.
                return null;
            }

            if (sink.Triangles == 0) return null;

            return new GltfMesh(name, sink.Positions, sink.Indices);
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

            /// <summary>Vertex positions, three floats each, in metres.</summary>
            internal List<float> Positions { get; } = new List<float>();

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

                Positions.Add((float)x);
                Positions.Add((float)y);
                Positions.Add((float)z);

                return (Positions.Count / 3) - 1;
            }
        }
    }
}
