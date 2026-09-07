using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The fall-hazard raster, as its own project tested it.
    ///
    /// Ported with the engine. Every assertion here pins a defect that was found on a real model
    /// and fixed: a distance transform that collapsed to one cell so the answer moved with the
    /// resolution, a slot judged by its length instead of its width, the corners of a wide opening
    /// reading safe while its sides read dangerous, and a break in the handrails nobody could fit
    /// through counted as unprotected edge.
    ///
    /// What the port adds on top is asserted in <see cref="FallHazardTests"/>, and listed once,
    /// on <see cref="FloorGaps"/> itself.
    /// </summary>
    public class FloorGapsTests
    {
        /// <summary>Two triangles covering an axis-aligned rectangle.</summary>
        private static IEnumerable<Tri2> Rect(double x0, double y0, double x1, double y1)
        {
            yield return new Tri2(x0, y0, x1, y0, x1, y1);
            yield return new Tri2(x0, y0, x1, y1, x0, y1);
        }

        /// <summary>A 10×10 slab with a 2×2 hole in the middle, as a frame of four strips.</summary>
        internal static List<Tri2> DonutSlab()
        {
            var tris = new List<Tri2>();

            tris.AddRange(Rect(0, 0, 10, 4));    // bottom
            tris.AddRange(Rect(0, 6, 10, 10));   // top
            tris.AddRange(Rect(0, 4, 4, 6));     // left
            tris.AddRange(Rect(6, 4, 10, 6));    // right

            return tris;                         // hole = [4,6] × [4,6]
        }

        /// <summary>The analysis with nothing owning the floor, which is what these tests read.</summary>
        private static FloorGapGrid Analyze(double minX, double minY, double maxX, double maxY,
                                            double cell, IReadOnlyList<Tri2> floor,
                                            IReadOnlyList<Tri2> plugs, double minGap) =>
            FloorGaps.Analyze(minX, minY, maxX, maxY, cell, floor, plugs, null, minGap);

        // ------------------------------------------------------------------- Hole detection

        [Fact]
        public void Analyze_finds_the_enclosed_hole()
        {
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), 0.0);

            var hole = Assert.Single(grid.Openings);

            Assert.Equal(5.0, hole.CentreX, 1);
            Assert.Equal(5.0, hole.CentreY, 1);
            Assert.Equal(4.0, hole.AreaSquareMetres, 1);          // 2 m × 2 m
            Assert.InRange(hole.WidestGapMetres, 1.5, 2.5);       // about the inscribed diameter
        }

        [Fact]
        public void Analyze_does_not_flag_the_exterior()
        {
            // A solid slab with padding all round: the empty margin is outside, not a hole.
            var grid = Analyze(-2, -2, 12, 12, 0.5, Rect(0, 0, 10, 10).ToList(),
                               Array.Empty<Tri2>(), 0.0);

            Assert.Empty(grid.Openings);
        }

        [Fact]
        public void Analyze_treats_an_obstruction_as_plugging_the_hole()
        {
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Rect(4, 4, 6, 6).ToList(), 0.0);

            Assert.Empty(grid.Openings);
        }

        [Fact]
        public void Analyze_leaves_the_gap_a_partial_obstruction_does_not_cover()
        {
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Rect(4, 4, 5, 5).ToList(), 0.0);

            var hole = Assert.Single(grid.Openings);

            Assert.True(hole.AreaSquareMetres < 4.0);
        }

        [Theory]
        [InlineData(0.5, true)]   // a 2 m hole against a 0.5 m gap allowed
        [InlineData(1.5, true)]   // and against a 1.5 m one: still wider than the gap allowed
        [InlineData(3.0, false)]  // a 3 m gap allowed is wider than the hole
        public void Analyze_flags_an_opening_by_the_threshold(double minGap, bool expectFlagged)
        {
            // CHANGED FROM THE SOURCE. This case came over as (1.5, false), because the source's
            // two functions read their thresholds differently: the openings pass compared the
            // clearance — half the gap — against the whole limit, so it took a 2 m hole to be
            // safe under a 1.5 m limit while the edge pass called its rim dangerous. Both passes
            // now flag on FloorGaps.CoreThreshold, and the assertion below holds them together.
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), minGap);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), minGap, 0.3)!;

            Assert.Equal(expectFlagged, Assert.Single(grid.Openings).Flagged);

            // The one setting, read the same way by both passes: an opening that is flagged has a
            // dangerous rim, and one that is not has none.
            Assert.Equal(expectFlagged, edges!.DangerousLengthMetres > 0);
        }

        [Fact]
        public void Analyze_and_AnalyzeEdges_agree_about_a_gap_over_the_limit()
        {
            // A 0.7 m x 3 m slot against a 0.5 m limit. The openings pass required twice the
            // limit, so this read flagged = false while the edge pass reported 7.46 m of
            // dangerous edge around it: every opening between one and two times the limit was
            // missing from the board with its own edge reported as a hazard.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 4, 10));          // left of the slot
            slab.AddRange(Rect(4.7, 0, 10, 10));       // right of it
            slab.AddRange(Rect(4, 0, 4.7, 3));         // below it
            slab.AddRange(Rect(4, 6, 4.7, 10));        // above it, leaving [4,4.7] x [3,6]

            var grid = Analyze(0, 0, 10, 10, 0.05, slab, Array.Empty<Tri2>(), 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 0.5, 0.3)!;

            var slot = Assert.Single(grid.Openings);

            Assert.InRange(slot.WidestGapMetres, 0.6, 0.8);
            Assert.True(slot.Flagged, "a 0.7 m gap is over the 0.5 m allowed");
            Assert.True(edges!.DangerousLengthMetres > 0);
        }

        [Fact]
        public void AnalyzeEdges_stops_when_it_is_asked_to()
        {
            var grid = Analyze(0, 0, 10, 10, 0.05, DonutSlab(), Array.Empty<Tri2>(), 0.5);

            var seen = 0;

            // Stop at the first report. Nothing is returned, because a half-classified plan is
            // not an answer about anything.
            Assert.Null(FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 0.5, 0.3, 0.0,
                                               cells =>
                                               {
                                                   seen = cells;
                                                   return false;
                                               }));

            Assert.True(seen > 0, "it reported how far it had got before it was stopped");

            // And it runs to the end when nothing stops it, reporting as it goes.
            var reported = 0;

            Assert.NotNull(FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 0.5, 0.3, 0.0,
                                                  cells =>
                                                  {
                                                      reported = cells;
                                                      return true;
                                                  }));

            Assert.True(reported >= grid.Cols * grid.Rows);
        }

        [Fact]
        public void Analyze_counts_two_separate_holes_separately()
        {
            var tris = new List<Tri2>();

            tris.AddRange(Rect(0, 0, 20, 4));
            tris.AddRange(Rect(0, 6, 20, 10));
            tris.AddRange(Rect(0, 4, 4, 6));
            tris.AddRange(Rect(6, 4, 14, 6));    // the bridge between the two holes
            tris.AddRange(Rect(16, 4, 20, 6));

            var grid = Analyze(0, 0, 20, 10, 0.5, tris, Array.Empty<Tri2>(), 0.0);

            Assert.Equal(2, grid.Openings.Count);
        }

        [Theory]
        [InlineData(0.1)]
        [InlineData(0.25)]
        [InlineData(0.5)]
        public void Analyze_measures_the_same_clearance_at_every_cell_size(double cell)
        {
            // A 6 m × 6 m slab with a 4 m × 4 m hole: the hole's deepest point is about 2 m from
            // any edge, whatever the resolution. The distance transform used to collapse to about
            // one cell, so the clearance — and every colour and threshold read off it — moved with
            // the cell size.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 6, 1));
            slab.AddRange(Rect(0, 5, 6, 6));
            slab.AddRange(Rect(0, 1, 1, 5));
            slab.AddRange(Rect(5, 1, 6, 5));     // hole [1,5] × [1,5]

            var grid = Analyze(0, 0, 6, 6, cell, slab, Array.Empty<Tri2>(), 0.2);

            Assert.Single(grid.Openings);
            Assert.InRange(grid.MaxClearanceMetres, 1.8, 2.2);
            Assert.InRange(grid.Openings[0].WidestGapMetres, 3.6, 4.4);
        }

        [Fact]
        public void Analyze_refuses_a_grid_over_the_cell_cap()
        {
            Assert.Throws<InvalidOperationException>(() =>
                Analyze(0, 0, 10000, 10000, 0.001, DonutSlab(), Array.Empty<Tri2>(), 0.0));
        }

        // ------------------------------------------------------------------ Edges and rails

        [Fact]
        public void AnalyzeEdges_calls_every_edge_of_an_unrailed_wide_void_dangerous()
        {
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), 0.2);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 0.5, 0.3)!;

            Assert.True(edges.DangerousLengthMetres > 0);   // a 2 m gap against a 0.5 m limit
            Assert.Equal(0.0, edges.ProtectedLengthMetres);
            Assert.Equal(0.0, edges.SafeLengthMetres);
        }

        [Fact]
        public void AnalyzeEdges_calls_every_edge_safe_when_the_limit_is_over_the_gap()
        {
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), 0.2);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 3.0, 0.3)!;

            Assert.True(edges.SafeLengthMetres > 0);        // a 2 m hole against a 3 m limit
            Assert.Equal(0.0, edges.DangerousLengthMetres);
            Assert.Equal(0.0, edges.ProtectedLengthMetres);
        }

        [Fact]
        public void AnalyzeEdges_protects_only_the_side_the_handrail_runs_along()
        {
            var grid = Analyze(0, 0, 10, 10, 0.5, DonutSlab(), Array.Empty<Tri2>(), 0.2);

            // A rail along the hole's bottom edge, spanning its width.
            var rail = Rect(4, 3.7, 6, 4.0).ToList();
            var edges = FloorGaps.AnalyzeEdges(grid, rail, 0.5, 0.4)!;

            Assert.True(edges.ProtectedLengthMetres > 0);   // the railed edge
            Assert.True(edges.DangerousLengthMetres > 0);   // the other three sides
        }

        [Theory]
        [InlineData(0.5, false)]  // a 0.2 m slot under a 0.5 m limit: safe, ends included
        [InlineData(0.15, true)]  // a 0.2 m slot over a 0.15 m limit: dangerous
        public void AnalyzeEdges_judges_a_slot_by_its_width_and_not_its_length(double limit,
                                                                              bool expectDanger)
        {
            // A 0.2 m × 3 m slot in a 10 m × 10 m slab. Marching one axis from the slot's end
            // edges measured its length — 3 m, over any limit — when the gap that matters is its
            // width: nobody falls through 0.2 m however long it runs.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 3, 10));        // left of the slot
            slab.AddRange(Rect(3.2, 0, 10, 10));     // right of it
            slab.AddRange(Rect(3, 0, 3.2, 3));       // below it
            slab.AddRange(Rect(3, 6, 3.2, 10));      // above it, leaving [3,3.2] × [3,6]

            var grid = Analyze(0, 0, 10, 10, 0.05, slab, Array.Empty<Tri2>(), limit);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), limit, 0.3)!;

            if (expectDanger)
            {
                Assert.True(edges.DangerousLengthMetres > 0);
            }
            else
            {
                Assert.Equal(0.0, edges.DangerousLengthMetres);
                Assert.True(edges.SafeLengthMetres > 0);
            }
        }

        [Theory]
        [InlineData(0.15, "DDDD")]  // every strip is at least 0.15 m
        [InlineData(0.20, "SDDD")]  // the right strip, 0.184 m, is under it
        [InlineData(0.30, "SSDD")]  // the top strip, 0.275 m, is under it
        [InlineData(0.45, "SSDD")]  // the left strip, 0.516 m, is still over it
        [InlineData(0.70, "SSSD")]  // the left strip is now under it, the bottom is not
        [InlineData(1.10, "SSSS")]  // the bottom strip, 1.025 m, is under it
        public void AnalyzeEdges_flips_a_strip_exactly_at_its_own_gap_width(double limit,
                                                                           string expected)
        {
            // A real model, distilled: an opening with equipment through it leaving four strips —
            // right 0.184 m, top 0.275 m, left 0.516 m, bottom 1.025 m. Each strip's edge must
            // flip from dangerous to safe as the limit passes its width, within a cell. A 0.184 m
            // gap used to read dangerous at a 0.3 m limit.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 10, 2));        // the opening is [2,6.4] × [2,6.5]
            slab.AddRange(Rect(0, 6.5, 10, 10));
            slab.AddRange(Rect(0, 2, 2, 6.5));
            slab.AddRange(Rect(6.4, 2, 10, 6.5));

            var box = Rect(2.516, 3.025, 6.216, 6.225).ToList();   // the equipment

            const double cell = 0.05;

            var grid = Analyze(0, 0, 10, 10, cell, slab, box, limit);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), limit, 0.3)!;

            string Probe(double x, double y)
            {
                var c = (int)((x - grid.OriginX) / cell);
                var r = (int)((y - grid.OriginY) / cell);

                // Outward from the probe until a classified edge cell turns up.
                for (var ring = 0; ring <= 4; ring++)
                    for (var dr = -ring; dr <= ring; dr++)
                        for (var dc = -ring; dc <= ring; dc++)
                        {
                            var rr = r + dr;
                            var cc = c + dc;

                            if (rr < 0 || rr >= grid.Rows || cc < 0 || cc >= grid.Cols) continue;

                            var said = edges.Edges[(rr * grid.Cols) + cc];

                            if (said == EdgeClass.Dangerous) return "D";
                            if (said == EdgeClass.SafeGap) return "S";
                        }

                return "?";
            }

            // Mid-strip probes on the floor just outside each edge of the opening.
            var actual =
                Probe(6.4 + cell, 4.5) +    // right, 0.184 m
                Probe(4.5, 6.5 + cell) +    // top, 0.275 m
                Probe(2 - cell, 4.5) +      // left, 0.516 m
                Probe(4.5, 2 - cell);       // bottom, 1.025 m

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void AnalyzeEdges_calls_the_corners_of_a_dangerous_opening_dangerous()
        {
            // The core of a wide opening sits diagonally at √2 times half the limit from a corner,
            // which slipped past a reach written for a straight wall: every corner of a square
            // opening read safe while its sides read dangerous.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 10, 2));        // the opening is [2,8] × [2,8]
            slab.AddRange(Rect(0, 8, 10, 10));
            slab.AddRange(Rect(0, 2, 2, 8));
            slab.AddRange(Rect(8, 2, 10, 8));

            const double cell = 0.05;

            var grid = Analyze(0, 0, 10, 10, cell, slab, Array.Empty<Tri2>(), 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 0.5, 0.3)!;

            foreach (var at in new[] { (2 - cell, 2 - cell), (2 + cell, 2 - cell), (2 - cell, 2 + cell) })
            {
                var c = (int)((at.Item1 - grid.OriginX) / cell);
                var r = (int)((at.Item2 - grid.OriginY) / cell);
                var said = edges.Edges[(r * grid.Cols) + c];

                Assert.True(said == EdgeClass.Dangerous,
                            "the corner cell at (" + at.Item1 + "," + at.Item2 + ") was " + said);
            }

            Assert.Equal(0.0, edges.SafeLengthMetres);
        }

        [Theory]
        [InlineData(0.5, true)]   // a 0.4 m break under a 0.5 m passage: nobody fits through it
        [InlineData(0.1, false)]  // the same break over a 0.1 m passage: still dangerous
        public void AnalyzeEdges_calls_a_short_break_between_handrails_safe(double minPassage,
                                                                           bool expectSafe)
        {
            // The opening is [2,8] × [2,6] and rails cover its whole rim but for a 0.6 m break in
            // the top side, which the tolerance narrows to about 0.4 m of unprotected edge.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 10, 2));
            slab.AddRange(Rect(0, 6, 10, 10));
            slab.AddRange(Rect(0, 2, 2, 6));
            slab.AddRange(Rect(8, 2, 10, 6));

            var rails = new List<Tri2>();

            rails.AddRange(Rect(1.9, 1.9, 8.1, 2.0));    // south rim
            rails.AddRange(Rect(1.9, 6.0, 4.7, 6.1));    // north rim, west part
            rails.AddRange(Rect(5.3, 6.0, 8.1, 6.1));    // north rim, east part
            rails.AddRange(Rect(1.9, 1.9, 2.0, 6.1));    // west rim
            rails.AddRange(Rect(8.0, 1.9, 8.1, 6.1));    // east rim

            var grid = Analyze(0, 0, 10, 10, 0.05, slab, Array.Empty<Tri2>(), 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, rails, 0.5, 0.1, minPassage)!;

            if (expectSafe)
            {
                Assert.Equal(0.0, edges.DangerousLengthMetres);
                Assert.True(edges.SafeLengthMetres > 0);
            }
            else
            {
                Assert.InRange(edges.DangerousLengthMetres, 0.2, 0.7);
            }

            Assert.True(edges.ProtectedLengthMetres > 10);   // most of the 20 m rim is railed
        }

        [Fact]
        public void AnalyzeEdges_records_a_run_with_the_gap_it_faces()
        {
            // A 6 m × 6 m opening against a 0.5 m limit: one dangerous run round the rim, facing
            // a gap about 6 m wide.
            var slab = new List<Tri2>();

            slab.AddRange(Rect(0, 0, 10, 2));
            slab.AddRange(Rect(0, 8, 10, 10));
            slab.AddRange(Rect(0, 2, 2, 8));
            slab.AddRange(Rect(8, 2, 10, 8));

            var grid = Analyze(0, 0, 10, 10, 0.05, slab, Array.Empty<Tri2>(), 0.5);
            var edges = FloorGaps.AnalyzeEdges(grid, Array.Empty<Tri2>(), 0.5, 0.3)!;

            var run = Assert.Single(edges.Runs);

            Assert.InRange(run.OverageMetres, 5.1, 5.9);     // a 6 m gap less the 0.5 m limit
            Assert.InRange(run.CentreX, 4, 6);               // the rim's centroid is the opening's
            Assert.InRange(run.CentreY, 4, 6);
        }
    }
}
