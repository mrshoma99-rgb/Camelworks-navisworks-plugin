using System;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// A drawn plan, as pixels: width, height and 8-bit RGBA, top row first.
    ///
    /// Neutral on purpose. <see cref="CamelWorks.Core.Report.PngWriter"/> turns one into a PNG for
    /// a snapshot or a report, and nothing here knows about a file format.
    /// </summary>
    public sealed class FallRaster
    {
        /// <summary>Hold a raster.</summary>
        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        /// <param name="rgba">The pixels: width × height × 4 bytes, top row first.</param>
        public FallRaster(int width, int height, byte[] rgba)
        {
            Width = width;
            Height = height;
            Rgba = rgba ?? throw new ArgumentNullException(nameof(rgba));
        }

        /// <summary>Width in pixels.</summary>
        public int Width { get; }

        /// <summary>Height in pixels.</summary>
        public int Height { get; }

        /// <summary>The pixels: red, green, blue and alpha per pixel, top row first.</summary>
        public byte[] Rgba { get; }

        /// <inheritdoc />
        public override string ToString() => Width + "×" + Height + " RGBA";
    }

    /// <summary>
    /// The fall-hazard plan, drawn.
    ///
    /// Two pictures, because the check ships two passes and they answer different questions: the
    /// heat map shows how far each point of a void is from anything solid, and the edge plan shows
    /// which floor edges need a handrail.
    ///
    /// Grid row 0 is the low-Y end of the plan and image row 0 is the top, so every draw flips
    /// vertically. Nothing is written on the picture: the source printed each opening's overage on
    /// the raster in a hand-built 5×7 font, and a number baked into pixels cannot be re-scaled,
    /// re-worded or translated by whatever places the image. The captions belong to the report
    /// writer, which has the openings and the runs as records.
    /// </summary>
    public static class FallPlan
    {
        /// <summary>
        /// The voids, coloured by how far each point is from the nearest solid.
        ///
        /// The ramp pivots on the gap the limit allows: a gap under it is blue through green, one
        /// at it is yellow, and a wider one runs orange to red and saturates at twice the limit. So
        /// a gap narrower than the limit never reads as red, which is the property the picture is
        /// read for.
        ///
        /// The ramp is drawn from the clearance, which is half the gap, so the pivot sits at half
        /// the limit. It pivoted on the whole limit, which put the yellow at a gap of twice the
        /// limit and left every gap between one and two times it reading green.
        /// </summary>
        /// <param name="grid">The plan.</param>
        /// <param name="limitMetres">The gap the ramp pivots on. Zero ramps over the range found.</param>
        /// <param name="pixelsPerCell">How many pixels one cell spans.</param>
        public static FallRaster Heat(FloorGapGrid grid, double limitMetres, int pixelsPerCell = 4)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            return Draw(grid, pixelsPerCell, (idx, paint) =>
            {
                if (grid.Hazard[idx])
                {
                    Ramp(grid.ClearanceMetres[idx], limitMetres, grid.MaxClearanceMetres,
                         out var r, out var g, out var b);

                    paint(r, g, b, 255);

                    return;
                }

                Ground(grid, idx, paint);
            });
        }

        /// <summary>
        /// The floor edges, coloured: green where the gap is under the limit, blue where a
        /// handrail covers it, red where it is over the limit and unprotected.
        /// </summary>
        /// <param name="result">The classified edges.</param>
        /// <param name="pixelsPerCell">How many pixels one cell spans.</param>
        public static FallRaster Edges(FloorEdgeResult result, int pixelsPerCell = 4)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var grid = result.Grid;

            return Draw(grid, pixelsPerCell, (idx, paint) =>
            {
                switch (result.Edges[idx])
                {
                    case EdgeClass.Dangerous: paint(220, 40, 40, 255); return;
                    case EdgeClass.Protected: paint(60, 140, 220, 255); return;
                    case EdgeClass.SafeGap: paint(70, 180, 90, 255); return;
                    default:
                        if (grid.Hazard[idx]) paint(44, 46, 58, 255);
                        else Ground(grid, idx, paint);

                        return;
                }
            });
        }

        /// <summary>What a cell that is not a void and not an edge is drawn as.</summary>
        /// <param name="grid">The plan.</param>
        /// <param name="idx">Which cell.</param>
        /// <param name="paint">Takes the colour for the cell.</param>
        private static void Ground(FloorGapGrid grid, int idx, Action<byte, byte, byte, byte> paint)
        {
            if (grid.Plug[idx]) paint(122, 126, 133, 255);        // equipment plugging a void
            else if (grid.Floor[idx]) paint(54, 56, 62, 255);     // floor
            else paint(0, 0, 0, 0);                               // outside the slab
        }

        /// <summary>Draw one cell per grid cell, flipped so image row 0 is the high-Y end.</summary>
        /// <param name="grid">The plan.</param>
        /// <param name="pixelsPerCell">How many pixels one cell spans.</param>
        /// <param name="colour">Called per cell with a painter for it.</param>
        private static FallRaster Draw(FloorGapGrid grid, int pixelsPerCell,
                                       Action<int, Action<byte, byte, byte, byte>> colour)
        {
            if (pixelsPerCell < 1)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerCell),
                                                      "there must be at least one pixel per cell");

            var width = grid.Cols * pixelsPerCell;
            var height = grid.Rows * pixelsPerCell;
            var rgba = new byte[width * height * 4];

            for (var r = 0; r < grid.Rows; r++)
            {
                var top = (grid.Rows - 1 - r) * pixelsPerCell;

                for (var c = 0; c < grid.Cols; c++)
                {
                    var column = c;

                    colour((r * grid.Cols) + c, (red, green, blue, alpha) =>
                    {
                        for (var py = 0; py < pixelsPerCell; py++)
                        {
                            var row = (((top + py) * width) + (column * pixelsPerCell)) * 4;

                            for (var px = 0; px < pixelsPerCell; px++)
                            {
                                var at = row + (px * 4);

                                rgba[at] = red;
                                rgba[at + 1] = green;
                                rgba[at + 2] = blue;
                                rgba[at + 3] = alpha;
                            }
                        }
                    });
                }
            }

            return new FallRaster(width, height, rgba);
        }

        /// <summary>
        /// A clearance on the ramp: blue, cyan, green, yellow at a gap of the limit, then orange
        /// and red.
        /// </summary>
        /// <param name="clearanceMetres">How far this point is from the nearest solid.</param>
        /// <param name="limitMetres">The gap the ramp pivots on, or zero for the range found.</param>
        /// <param name="maxClearanceMetres">The largest clearance, for the no-limit ramp.</param>
        /// <param name="red">The red channel.</param>
        /// <param name="green">The green channel.</param>
        /// <param name="blue">The blue channel.</param>
        private static void Ramp(double clearanceMetres, double limitMetres,
                                 double maxClearanceMetres,
                                 out byte red, out byte green, out byte blue)
        {
            double at;

            if (limitMetres > 1e-9)
            {
                // Against half the limit: the clearance is half the gap, and the limit is a gap.
                var ratio = clearanceMetres / (limitMetres / 2.0);

                at = ratio <= 1.0
                    ? 0.5 * Math.Max(0.0, ratio)
                    : 0.5 + (0.5 * Math.Min(1.0, ratio - 1.0));
            }
            else
            {
                at = maxClearanceMetres > 1e-9
                    ? Math.Min(1.0, clearanceMetres / maxClearanceMetres)
                    : 0.5;
            }

            at = at < 0 ? 0 : at > 1 ? 1 : at;

            for (var i = 1; i < Stops.Length; i++)
            {
                if (at > Stops[i][0]) continue;

                var low = Stops[i - 1];
                var high = Stops[i];
                var part = (at - low[0]) / (high[0] - low[0]);

                red = (byte)Math.Round(low[1] + ((high[1] - low[1]) * part));
                green = (byte)Math.Round(low[2] + ((high[2] - low[2]) * part));
                blue = (byte)Math.Round(low[3] + ((high[3] - low[3]) * part));

                return;
            }

            var last = Stops[Stops.Length - 1];

            red = (byte)last[1];
            green = (byte)last[2];
            blue = (byte)last[3];
        }

        /// <summary>The ramp's stops: where each sits, and its red, green and blue.</summary>
        private static readonly double[][] Stops =
        {
            new[] { 0.00, 40, 70, 190 },    // no clearance at all
            new[] { 0.25, 40, 180, 200 },
            new[] { 0.42, 80, 190, 80 },
            new[] { 0.50, 245, 225, 50 },   // a gap at the limit
            new[] { 0.75, 242, 120, 30 },
            new[] { 1.00, 210, 30, 30 },    // a gap of twice the limit or worse
        };
    }
}
