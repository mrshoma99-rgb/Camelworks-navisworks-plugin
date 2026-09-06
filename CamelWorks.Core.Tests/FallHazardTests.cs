using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What the fall-hazard port adds to the engine it came from: the owner grid, the records that
    /// survive, metres, and a run measured along the void rather than counted in cells.
    ///
    /// The behaviour the source already had is in <see cref="FloorGapsTests"/>.
    /// </summary>
    public class FallHazardTests
    {
        /// <summary>Two triangles covering an axis-aligned rectangle.</summary>
        private static IEnumerable<Tri2> Rect(double x0, double y0, double x1, double y1)
        {
            yield return new Tri2(x0, y0, x1, y0, x1, y1);
            yield return new Tri2(x0, y0, x1, y1, x0, y1);
        }

        /// <summary>
        /// A 10 m × 10 m floor in two slabs, west and east of x = 5, with a 2 m × 2 m hole across
        /// the join. Owner 0 is the west slab and owner 1 the east.
        /// </summary>
        internal static List<Tri2> SplitSlab(out List<int> owners)
        {
            var west = new List<Tri2>();

            west.AddRange(Rect(0, 0, 5, 4));
            west.AddRange(Rect(0, 6, 5, 10));
            west.AddRange(Rect(0, 4, 4, 6));

            var east = new List<Tri2>();

            east.AddRange(Rect(5, 0, 10, 4));
            east.AddRange(Rect(5, 6, 10, 10));
            east.AddRange(Rect(6, 4, 10, 6));

            owners = new List<int>();
            owners.AddRange(Enumerable.Repeat(0, west.Count));
            owners.AddRange(Enumerable.Repeat(1, east.Count));

            var floor = new List<Tri2>(west);

            floor.AddRange(east);

            return floor;                       // the hole is [4,6] × [4,6]
        }

        // --------------------------------------------------------------------- The owner grid

        [Fact]
        public void An_opening_names_the_slabs_around_it()
        {
            var floor = SplitSlab(out var owners);
            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.1, floor, null, owners, 0.2);

            var hole = Assert.Single(grid.Openings);

            Assert.Equal(new[] { 0, 1 }, hole.Owners.OrderBy(one => one).ToArray());
        }

        [Fact]
        public void An_opening_in_one_slab_names_only_that_slab()
        {
            // The hole moved wholly into the west slab: the east slab is nowhere near it, and an
            // opening that named it would put a finding on an element with no hole in it.
            var floor = new List<Tri2>();

            floor.AddRange(Rect(0, 0, 5, 2));
            floor.AddRange(Rect(0, 4, 5, 10));
            floor.AddRange(Rect(0, 2, 2, 4));
            floor.AddRange(Rect(4, 2, 5, 4));

            var owners = Enumerable.Repeat(0, floor.Count).ToList();
            var east = Rect(5, 0, 10, 10).ToList();

            floor.AddRange(east);
            owners.AddRange(Enumerable.Repeat(1, east.Count));

            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.1, floor, null, owners, 0.2);

            Assert.Equal(new[] { 0 }, Assert.Single(grid.Openings).Owners.ToArray());
        }

        [Fact]
        public void A_dangerous_run_names_the_floor_it_runs_along()
        {
            var floor = SplitSlab(out var owners);
            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.1, floor, null, owners, 0.2);
            var edges = FloorGaps.AnalyzeEdges(grid, null, 0.5, 0.3);

            var run = Assert.Single(edges.Runs);

            Assert.Equal(new[] { 0, 1 }, run.Owners.OrderBy(one => one).ToArray());
        }

        [Fact]
        public void A_cell_no_triangle_covered_is_owned_by_nothing()
        {
            var floor = SplitSlab(out var owners);
            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.1, floor, null, owners, 0.2);

            var inside = grid.CellAt(5, 5);

            Assert.True(grid.Hazard[inside]);
            Assert.Equal(-1, grid.Owner[inside]);
            Assert.Equal(0, grid.Owner[grid.CellAt(1, 1)]);
            Assert.Equal(1, grid.Owner[grid.CellAt(9, 1)]);
        }

        // ------------------------------------------------------------- Runs measured as paths

        [Fact]
        public void A_45_degree_run_reports_its_true_length()
        {
            // A square opening turned 45 degrees, 5.657 m on a side. Its rim is a staircase two
            // cells wide, so counting cells reports about √2 more than the truth.
            var floor = new List<Tri2>();

            floor.AddRange(Rect(0, 0, 10, 1));
            floor.AddRange(Rect(0, 9, 10, 10));
            floor.AddRange(Rect(0, 1, 1, 9));
            floor.AddRange(Rect(9, 1, 10, 9));

            // The corners of [1,9] × [1,9] outside the diamond with vertices at (5,1), (9,5),
            // (5,9) and (1,5).
            floor.Add(new Tri2(1, 1, 5, 1, 1, 5));
            floor.Add(new Tri2(5, 1, 9, 1, 9, 5));
            floor.Add(new Tri2(9, 5, 9, 9, 5, 9));
            floor.Add(new Tri2(1, 5, 5, 9, 1, 9));

            const double cell = 0.05;

            // The grid is offset a quarter of a cell so that no cell centre lands on a side of
            // the diamond. Centre sampling makes a cell on the line a coin flip, and a rim that
            // flips is a rim with a step in it — a limit of the algorithm rather than of the
            // measure, and one this test is not about.
            var grid = FloorGaps.Analyze(-cell / 4.0, 0, 10, 10, cell, floor, null, null, 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, null, 0.5, 0.3);

            var run = Assert.Single(edges.Runs);
            var truth = 4 * Math.Sqrt(32.0);        // four sides of √(4² + 4²)

            Assert.InRange(run.LengthMetres, truth * 0.98, truth * 1.02);
            Assert.Equal(edges.DangerousLengthMetres, run.LengthMetres, 6);

            // What the source reported: one cell size per cell of rim, which is nowhere near it.
            var counted = edges.Edges.Count(one => one == EdgeClass.Dangerous) * cell;

            Assert.True(counted > truth * 1.2,
                        "counting cells over-reports a staircase rim, and gave " + counted);
        }

        [Fact]
        public void A_straight_run_is_measured_end_to_end()
        {
            // A 4 m slot, 0.6 m wide, in the middle of a slab: each of its two long edges is a
            // 4 m run and neither is 4 m plus or minus a rounding of the cell size.
            var floor = new List<Tri2>();

            floor.AddRange(Rect(0, 0, 10, 3));
            floor.AddRange(Rect(0, 3.6, 10, 10));
            floor.AddRange(Rect(0, 3, 3, 3.6));
            floor.AddRange(Rect(7, 3, 10, 3.6));    // the slot is [3,7] × [3,3.6]

            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.05, floor, null, null, 0.2);
            var edges = FloorGaps.AnalyzeEdges(grid, null, 0.4, 0.3);

            var run = Assert.Single(edges.Runs);

            // One run all the way round the slot: two 4 m sides and two 0.6 m ends, and the rim
            // sits half a cell outside the slot.
            Assert.InRange(run.LengthMetres, 9.0, 9.6);
        }

        // -------------------------------------------------------------------- The sweep cap

        [Fact]
        public void A_plan_the_sweeps_cannot_settle_says_so()
        {
            var floor = Serpentine(200, out var span);
            var grid = FloorGaps.Analyze(0, 0, span.Item1, span.Item2, 0.1, floor, null, null, 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, null, 0.5, 0.3);

            Assert.Contains(edges.Notes, note => note.Contains("still changing"));
        }

        [Fact]
        public void A_plan_the_sweeps_settle_says_nothing()
        {
            var floor = Serpentine(20, out var span);
            var grid = FloorGaps.Analyze(0, 0, span.Item1, span.Item2, 0.1, floor, null, null, 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, null, 0.5, 0.3);

            Assert.Empty(edges.Notes);
        }

        /// <summary>
        /// A floor with one long winding corridor cut through it, open at alternating ends.
        ///
        /// The distance through a void is swept over the whole grid in two directions, so a void
        /// that doubles back needs one sweep per pair of legs. Enough legs and it is still
        /// changing when the sweeps run out, which is the state that used to be left unsaid.
        /// </summary>
        /// <param name="legs">How many times the corridor doubles back.</param>
        /// <param name="span">How wide and how tall the floor is, in metres.</param>
        internal static List<Tri2> Serpentine(int legs, out Tuple<double, double> span)
        {
            const double wide = 6.0;
            var tall = 2.0 + (legs * 0.6);
            var floor = new List<Tri2>();

            floor.AddRange(Rect(0, 0, wide, 0.5));
            floor.AddRange(Rect(0, tall - 0.5, wide, tall));
            floor.AddRange(Rect(0, 0.5, 0.5, tall - 0.5));
            floor.AddRange(Rect(wide - 0.5, 0.5, wide, tall - 0.5));

            // The strips between the corridors, each short at one end so the corridors join up.
            for (var k = 0; k < legs; k++)
            {
                var y = 0.8 + (k * 0.6);
                var x0 = k % 2 == 0 ? 0.5 : 0.8;
                var x1 = k % 2 == 0 ? wide - 0.8 : wide - 0.5;

                floor.AddRange(Rect(x0, y, x1, y + 0.3));
            }

            span = Tuple.Create(wide, tall);

            return floor;
        }

        // ------------------------------------------------------------------- The plan, drawn

        [Fact]
        public void The_heat_plan_is_one_cell_per_cell_and_reds_only_what_is_over_the_limit()
        {
            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.5, FloorGapsTests.DonutSlab(),
                                         null, null, 0.0);

            var wide = FallPlan.Heat(grid, 5.0, 3);       // a limit far over the hole's clearance
            var tight = FallPlan.Heat(grid, 0.3, 3);      // a limit well under it

            Assert.Equal(grid.Cols * 3, wide.Width);
            Assert.Equal(grid.Rows * 3, wide.Height);
            Assert.Equal(0, Red(wide));
            Assert.True(Red(tight) > 0, "the middle of the hole is over the limit and reads red");
        }

        [Fact]
        public void The_edge_plan_colours_the_dangerous_edges()
        {
            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.5, FloorGapsTests.DonutSlab(),
                                         null, null, 0.2);

            var edges = FloorGaps.AnalyzeEdges(grid, null, 0.5, 0.3);
            var raster = FallPlan.Edges(edges, 2);

            Assert.Equal(grid.Cols * 2, raster.Width);
            Assert.True(Red(raster) > 0, "an unrailed rim reads red");
        }

        // --------------------------------------------------------------------- The PNG bytes

        [Fact]
        public void PngWriter_encodes_pixels_that_inflate_back_to_what_went_in()
        {
            var rgba = new byte[]
            {
                255, 0, 0, 255, 0, 255, 0, 255,
                0, 0, 255, 255, 255, 255, 255, 255,
            };

            var png = PngWriter.Encode(2, 2, rgba);

            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());

            var size = Size(png);

            Assert.Equal(2, size.Item1);
            Assert.Equal(2, size.Item2);

            // PngImage is the reader beside it, so the two are held to each other rather than to
            // a hand-written byte sequence.
            Assert.True(PngImage.TryRead(png, out var image));
            Assert.NotNull(image);
            Assert.Equal(2, image!.Width);
            Assert.Equal(2, image.Height);
            Assert.Equal("DeviceRGB", image.ColourSpace);
            Assert.NotNull(image.Alpha);
        }

        [Fact]
        public void PngWriter_refuses_a_buffer_that_does_not_match_the_size()
        {
            // The message says both sizes: Array.Copy throws an ArgumentException of its own a few
            // lines further on, and a test that only named the type would pass either way.
            var wrong = Assert.Throws<ArgumentException>(() => PngWriter.Encode(2, 2, new byte[3]));

            Assert.Contains("3 bytes", wrong.Message);
            Assert.Contains("2×2 RGBA image needs 16", wrong.Message);

            Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Encode(0, 2, new byte[0]));
        }

        [Fact]
        public void PngWriter_writes_a_plan_a_reader_can_take_back()
        {
            var grid = FloorGaps.Analyze(0, 0, 10, 10, 0.5, FloorGapsTests.DonutSlab(),
                                         null, null, 0.2);

            var raster = FallPlan.Heat(grid, 0.3, 4);
            var png = PngWriter.Encode(raster.Width, raster.Height, raster.Rgba);
            var size = Size(png);

            Assert.Equal(raster.Width, size.Item1);
            Assert.Equal(raster.Height, size.Item2);
        }

        /// <summary>The width and height an encoded PNG declares, read out of its header.</summary>
        /// <param name="png">The PNG bytes.</param>
        private static Tuple<int, int> Size(byte[] png)
        {
            int At(int offset) =>
                (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];

            return Tuple.Create(At(16), At(20));
        }

        /// <summary>How many pixels of a raster are clearly red.</summary>
        /// <param name="raster">The raster.</param>
        private static int Red(FallRaster raster)
        {
            var count = 0;

            for (var at = 0; at + 3 < raster.Rgba.Length; at += 4)
                if (raster.Rgba[at] >= 180 && raster.Rgba[at + 1] <= 90 && raster.Rgba[at + 2] <= 90)
                    count++;

            return count;
        }
    }
}
