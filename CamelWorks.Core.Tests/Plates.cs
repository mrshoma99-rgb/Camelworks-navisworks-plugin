using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Tessellated floor plates, as a model holds them: a grid of cells, two triangles each,
    /// sharing their vertices the way a welded mesh does.
    ///
    /// Synthetic meshes rather than a host. Navisworks cannot run here, and the surface detector
    /// reads nothing but triangles, so every assertion about it can be made without one.
    /// </summary>
    internal static class Plates
    {
        /// <summary>
        /// The same triangles with every corner given its own vertex, as the host emits them
        /// before anything welds.
        /// </summary>
        /// <param name="mesh">The welded mesh.</param>
        internal static TriangleMesh Loose(TriangleMesh mesh)
        {
            var positions = new List<double>();
            var indices = new List<int>();

            for (var i = 0; i < mesh.Indices.Count; i++)
            {
                var vertex = mesh.Indices[i];

                indices.Add(positions.Count / 3);
                positions.Add(mesh.Positions[(vertex * 3) + 0]);
                positions.Add(mesh.Positions[(vertex * 3) + 1]);
                positions.Add(mesh.Positions[(vertex * 3) + 2]);
            }

            return new TriangleMesh(positions, indices);
        }

        /// <summary>A flat rectangular plate, facing up.</summary>
        internal static TriangleMesh Flat(double x0, double y0, double x1, double y1, double z,
                                          double cell = 1.0) =>
            Shaped(x0, y0, x1, y1, z, cell, null);

        /// <summary>
        /// A flat plate with some of its cells left out — an L, a ring, a floor around a stair
        /// void.
        /// </summary>
        /// <param name="x0">Low corner, X.</param>
        /// <param name="y0">Low corner, Y.</param>
        /// <param name="x1">High corner, X.</param>
        /// <param name="y1">High corner, Y.</param>
        /// <param name="z">The surface elevation.</param>
        /// <param name="cell">How big one cell is.</param>
        /// <param name="keep">Whether a cell, given its centre, is part of the plate.</param>
        internal static TriangleMesh Shaped(double x0, double y0, double x1, double y1, double z,
                                            double cell, Func<double, double, bool>? keep)
        {
            var across = (int)Math.Round((x1 - x0) / cell);
            var along = (int)Math.Round((y1 - y0) / cell);

            var positions = new List<double>();

            for (var i = 0; i <= across; i++)
                for (var j = 0; j <= along; j++)
                {
                    positions.Add(x0 + (i * cell));
                    positions.Add(y0 + (j * cell));
                    positions.Add(z);
                }

            var indices = new List<int>();

            for (var i = 0; i < across; i++)
                for (var j = 0; j < along; j++)
                {
                    var centreX = x0 + ((i + 0.5) * cell);
                    var centreY = y0 + ((j + 0.5) * cell);

                    if (keep != null && !keep(centreX, centreY)) continue;

                    var a = (i * (along + 1)) + j;
                    var b = ((i + 1) * (along + 1)) + j;
                    var c = ((i + 1) * (along + 1)) + j + 1;
                    var d = (i * (along + 1)) + j + 1;

                    // Wound so the face points up. The detector's normal test is signed, which is
                    // the whole reason a slab's underside is not a walkable surface.
                    indices.Add(a); indices.Add(b); indices.Add(c);
                    indices.Add(a); indices.Add(c); indices.Add(d);
                }

            return new TriangleMesh(positions, indices);
        }
    }
}
