using System;
using System.Collections.Generic;
using System.Globalization;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// One opening in a floor, as the raster found it. Every length is metres.
    /// </summary>
    public sealed class FloorOpening
    {
        /// <summary>Record an opening.</summary>
        /// <param name="centreX">Where its cell centroid is, across.</param>
        /// <param name="centreY">Where its cell centroid is, along.</param>
        /// <param name="areaSquareMetres">The plan area of its cells.</param>
        /// <param name="widestGapMetres">The widest clear span across it.</param>
        /// <param name="minX">Its cell-snapped box, low across.</param>
        /// <param name="minY">Its cell-snapped box, low along.</param>
        /// <param name="maxX">Its cell-snapped box, high across.</param>
        /// <param name="maxY">Its cell-snapped box, high along.</param>
        /// <param name="cellCount">How many cells it holds.</param>
        /// <param name="owners">The floor triangles' owner indices around it, commonest first.</param>
        public FloorOpening(double centreX, double centreY, double areaSquareMetres,
                            double widestGapMetres, double minX, double minY, double maxX,
                            double maxY, int cellCount, IReadOnlyList<int>? owners = null)
        {
            CentreX = centreX;
            CentreY = centreY;
            AreaSquareMetres = areaSquareMetres;
            WidestGapMetres = widestGapMetres;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            CellCount = cellCount;
            Owners = owners ?? Array.Empty<int>();
        }

        /// <summary>Where its cell centroid is, across, in metres.</summary>
        public double CentreX { get; }

        /// <summary>Where its cell centroid is, along, in metres.</summary>
        public double CentreY { get; }

        /// <summary>The plan area of its cells, in square metres.</summary>
        public double AreaSquareMetres { get; }

        /// <summary>
        /// The widest clear span across it, in metres — about the diameter of the largest circle
        /// that fits inside it.
        /// </summary>
        public double WidestGapMetres { get; }

        /// <summary>Its cell-snapped box, low across, in metres.</summary>
        public double MinX { get; }

        /// <summary>Its cell-snapped box, low along, in metres.</summary>
        public double MinY { get; }

        /// <summary>Its cell-snapped box, high across, in metres.</summary>
        public double MaxX { get; }

        /// <summary>Its cell-snapped box, high along, in metres.</summary>
        public double MaxY { get; }

        /// <summary>How many cells it holds.</summary>
        public int CellCount { get; }

        /// <summary>
        /// The owner indices of the floor triangles around it, commonest first.
        ///
        /// What names the slabs an opening is cut in. Without it an opening is a coordinate and
        /// nothing else — no element to focus, isolate, attach an issue to or send in a pack.
        /// </summary>
        public IReadOnlyList<int> Owners { get; }

        /// <summary>True when its widest gap reaches the limit the run asked for.</summary>
        public bool Flagged { get; internal set; }

        /// <inheritdoc />
        public override string ToString() =>
            AreaSquareMetres.ToString("0.##", CultureInfo.InvariantCulture) + " m², widest "
            + WidestGapMetres.ToString("0.###", CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>What one floor cell facing a void is, once the edges have been classified.</summary>
    public enum EdgeClass
    {
        /// <summary>Not a floor edge facing a void.</summary>
        None = 0,

        /// <summary>The void it faces is never locally wide enough to fall through.</summary>
        SafeGap = 1,

        /// <summary>Over the limit, and a handrail runs along it within the tolerance.</summary>
        Protected = 2,

        /// <summary>Over the limit, with no handrail along it.</summary>
        Dangerous = 3,
    }

    /// <summary>
    /// One connected run of dangerous floor edge — the natural per-edge result.
    ///
    /// The source computed these and kept nothing but three aggregate lengths, so an edge that
    /// needed a handrail could be reported as a number of metres and never as a place.
    /// </summary>
    public sealed class EdgeRun
    {
        /// <summary>Record a run.</summary>
        /// <param name="centreX">Where its cell centroid is, across, in metres.</param>
        /// <param name="centreY">Where its cell centroid is, along, in metres.</param>
        /// <param name="lengthMetres">How long it is, measured along its cells.</param>
        /// <param name="facedGapMetres">The widest gap of the void it borders, in metres.</param>
        /// <param name="limitMetres">The limit it was judged against, in metres.</param>
        /// <param name="cellCount">How many cells it holds.</param>
        /// <param name="owners">The floor triangles' owner indices along it, commonest first.</param>
        public EdgeRun(double centreX, double centreY, double lengthMetres, double facedGapMetres,
                       double limitMetres, int cellCount, IReadOnlyList<int>? owners = null)
        {
            CentreX = centreX;
            CentreY = centreY;
            LengthMetres = lengthMetres;
            FacedGapMetres = facedGapMetres;
            LimitMetres = limitMetres;
            CellCount = cellCount;
            Owners = owners ?? Array.Empty<int>();
        }

        /// <summary>Where its cell centroid is, across, in metres.</summary>
        public double CentreX { get; }

        /// <summary>Where its cell centroid is, along, in metres.</summary>
        public double CentreY { get; }

        /// <summary>How long it is, in metres, measured along its cells rather than counted.</summary>
        public double LengthMetres { get; }

        /// <summary>The widest gap of the void it borders, in metres.</summary>
        public double FacedGapMetres { get; }

        /// <summary>The limit it was judged against, in metres.</summary>
        public double LimitMetres { get; }

        /// <summary>How many cells it holds.</summary>
        public int CellCount { get; }

        /// <summary>The owner indices of its floor cells, commonest first.</summary>
        public IReadOnlyList<int> Owners { get; }

        /// <summary>How far the gap it faces exceeds the limit, in metres. Zero when it does not.</summary>
        public double OverageMetres => Math.Max(0.0, FacedGapMetres - LimitMetres);

        /// <inheritdoc />
        public override string ToString() =>
            LengthMetres.ToString("0.##", CultureInfo.InvariantCulture) + " m of edge over a "
            + FacedGapMetres.ToString("0.##", CultureInfo.InvariantCulture) + " m gap";
    }

    /// <summary>
    /// One plan grid, rasterised and analysed: what is floor, what is obstruction, what is an
    /// enclosed void, how far each void cell is from the nearest solid, and which element owns
    /// each floor cell. Row-major, index = row * <see cref="Cols"/> + column.
    /// </summary>
    public sealed class FloorGapGrid
    {
        /// <summary>Record a grid.</summary>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="originX">Where cell (0,0) starts, across, in metres.</param>
        /// <param name="originY">Where cell (0,0) starts, along, in metres.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        /// <param name="floor">Per-cell floor coverage.</param>
        /// <param name="plug">Per-cell obstruction coverage.</param>
        /// <param name="hazard">Per-cell enclosed void.</param>
        /// <param name="clearanceMetres">Per-cell distance from a void cell to the nearest solid.</param>
        /// <param name="maxClearanceMetres">The largest of those distances.</param>
        /// <param name="owner">Per-cell owner index for floor cells, -1 elsewhere.</param>
        /// <param name="openings">The openings found, widest gap first.</param>
        /// <param name="openingIndex">Per-cell index into <paramref name="openings"/>, -1 elsewhere.</param>
        public FloorGapGrid(int cols, int rows, double originX, double originY, double cellMetres,
                            bool[] floor, bool[] plug, bool[] hazard, double[] clearanceMetres,
                            double maxClearanceMetres, int[] owner,
                            IReadOnlyList<FloorOpening> openings, int[] openingIndex)
        {
            Cols = cols;
            Rows = rows;
            OriginX = originX;
            OriginY = originY;
            CellMetres = cellMetres;
            Floor = floor ?? Array.Empty<bool>();
            Plug = plug ?? Array.Empty<bool>();
            Hazard = hazard ?? Array.Empty<bool>();
            ClearanceMetres = clearanceMetres ?? Array.Empty<double>();
            MaxClearanceMetres = maxClearanceMetres;
            Owner = owner ?? Array.Empty<int>();
            Openings = openings ?? Array.Empty<FloorOpening>();
            OpeningIndex = openingIndex ?? Array.Empty<int>();
        }

        /// <summary>Grid width in cells.</summary>
        public int Cols { get; }

        /// <summary>Grid height in cells.</summary>
        public int Rows { get; }

        /// <summary>Where cell (0,0) starts, across, in metres.</summary>
        public double OriginX { get; }

        /// <summary>Where cell (0,0) starts, along, in metres.</summary>
        public double OriginY { get; }

        /// <summary>How wide one cell is, in metres.</summary>
        public double CellMetres { get; }

        /// <summary>Per-cell floor coverage.</summary>
        public bool[] Floor { get; }

        /// <summary>Per-cell obstruction coverage: equipment, duct and pipe.</summary>
        public bool[] Plug { get; }

        /// <summary>Per-cell enclosed void: no floor, no obstruction, and not the exterior.</summary>
        public bool[] Hazard { get; }

        /// <summary>Per-cell distance from a void cell to the nearest solid, in metres. Zero elsewhere.</summary>
        public double[] ClearanceMetres { get; }

        /// <summary>The largest clearance found, in metres.</summary>
        public double MaxClearanceMetres { get; }

        /// <summary>
        /// Per-cell owner index for floor cells, -1 elsewhere.
        ///
        /// Indexes the owner list handed to <see cref="FloorGaps.Analyze"/>, which is how a
        /// finding names the elements it was found on.
        /// </summary>
        public int[] Owner { get; }

        /// <summary>The openings found, widest gap first.</summary>
        public IReadOnlyList<FloorOpening> Openings { get; }

        /// <summary>Per-cell index into <see cref="Openings"/> for void cells, -1 elsewhere.</summary>
        public int[] OpeningIndex { get; }

        /// <summary>The cell one point falls in, or -1 when it is off the grid.</summary>
        /// <param name="x">Across, in metres.</param>
        /// <param name="y">Along, in metres.</param>
        public int CellAt(double x, double y)
        {
            var c = (int)Math.Floor((x - OriginX) / CellMetres);
            var r = (int)Math.Floor((y - OriginY) / CellMetres);

            return c < 0 || c >= Cols || r < 0 || r >= Rows ? -1 : (r * Cols) + c;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Cols + "×" + Rows + " at " + CellMetres.ToString("0.###", CultureInfo.InvariantCulture)
            + " m, " + Openings.Count + " opening(s)";
    }

    /// <summary>
    /// The floor edges of a <see cref="FloorGapGrid"/>, classified: safe by gap, protected by a
    /// handrail, or dangerous. Every length is metres.
    /// </summary>
    public sealed class FloorEdgeResult
    {
        /// <summary>Record the classification.</summary>
        /// <param name="grid">The grid the edges were classified on.</param>
        /// <param name="edges">Per-cell classification, same grid.</param>
        /// <param name="dangerousLengthMetres">How much unprotected dangerous edge there is.</param>
        /// <param name="protectedLengthMetres">How much dangerous edge a handrail covers.</param>
        /// <param name="safeLengthMetres">How much edge is safe by gap alone.</param>
        /// <param name="runs">The dangerous runs that survived the minimum passage.</param>
        /// <param name="notes">What the classification could not do.</param>
        public FloorEdgeResult(FloorGapGrid grid, EdgeClass[] edges, double dangerousLengthMetres,
                               double protectedLengthMetres, double safeLengthMetres,
                               IReadOnlyList<EdgeRun>? runs = null,
                               IReadOnlyList<string>? notes = null)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Edges = edges ?? Array.Empty<EdgeClass>();
            DangerousLengthMetres = dangerousLengthMetres;
            ProtectedLengthMetres = protectedLengthMetres;
            SafeLengthMetres = safeLengthMetres;
            Runs = runs ?? Array.Empty<EdgeRun>();
            Notes = notes ?? Array.Empty<string>();
        }

        /// <summary>The grid the edges were classified on.</summary>
        public FloorGapGrid Grid { get; }

        /// <summary>Per-cell classification, on the same grid.</summary>
        public EdgeClass[] Edges { get; }

        /// <summary>How much unprotected dangerous edge there is, in metres.</summary>
        public double DangerousLengthMetres { get; }

        /// <summary>How much dangerous edge a handrail covers, in metres.</summary>
        public double ProtectedLengthMetres { get; }

        /// <summary>How much edge is safe by the gap alone, in metres.</summary>
        public double SafeLengthMetres { get; }

        /// <summary>The dangerous runs that survived the minimum passage, longest first.</summary>
        public IReadOnlyList<EdgeRun> Runs { get; }

        /// <summary>
        /// What the classification could not do — today, only the sweep cap.
        ///
        /// The distance through the void is swept to convergence, and a domain convoluted enough
        /// to still be changing at the cap leaves distances that read as safe. It used to stop at
        /// sixteen sweeps and say nothing at all, so the answer was wrong and looked right.
        /// </summary>
        public IReadOnlyList<string> Notes { get; }

        /// <inheritdoc />
        public override string ToString() =>
            "dangerous " + DangerousLengthMetres.ToString("0.##", CultureInfo.InvariantCulture)
            + " m, protected " + ProtectedLengthMetres.ToString("0.##", CultureInfo.InvariantCulture)
            + " m, safe " + SafeLengthMetres.ToString("0.##", CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>
    /// Openings in a floor, and the edges around them, from triangles projected into plan.
    ///
    /// Ported from the fall-hazard nodes of the owner's own graph product, whose pure half was
    /// already host-free. Floor and obstruction triangles are rasterised onto a plan grid, the
    /// voids fully enclosed by floor are isolated so the building's exterior is not read as a
    /// hole, each void is graded by how far its deepest point sits from the nearest solid, and
    /// the floor cells facing a void are classified as safe, handrail-protected or dangerous.
    ///
    /// Five things the port fixes, each of which the source got wrong:
    ///
    ///   * Every length is metres. The source worked in document units and printed the same
    ///     opening's gap two different ways in two of its own outputs.
    ///   * <see cref="Rasterize"/> takes a per-triangle owner index and fills a parallel owner
    ///     grid, so an opening names the slabs around it and a run names the floor along it. The
    ///     source lost element identity the moment triangles arrived as one undifferentiated list.
    ///   * The per-opening and per-run records survive. The source computed both and kept three
    ///     aggregate numbers.
    ///   * A run's length is measured along the void it faces rather than counted in cells. A
    ///     45-degree rim is a staircase two cells wide, so counting its cells over-reports by
    ///     about √2 and stepping along them one at a time under-reports by the same factor. The
    ///     minimum passage is judged on the same quantity, so the error was in both numbers.
    ///   * The distance through the void is swept to convergence and says so when it hits the
    ///     cap. The source stopped at sixteen sweeps and left stale distances that read as safe.
    ///
    /// Recorded and deferred, in the order they matter. Each is a limit of the algorithm rather
    /// than of this port, and every one of them is visible in what the check reports:
    ///
    ///   * Deferred — the plan silhouette is every triangle of every spanning element. A
    ///     column, a riser or a stair whose box merely reaches into the band projects its full
    ///     height as solid floor. This is the largest fidelity problem here, and it was a
    ///     deliberate trade: reading only the triangles inside a thin Z band left equipment cap
    ///     faces out, and equipment then read as open floor rather than as a plug. Fixing it
    ///     needs a real Z band on triangles with an explicit cap-face rule, which is a change to
    ///     the algorithm and not a port of it.
    ///   * Deferred — bounding-box spanning selects elements far too coarsely. One grouped
    ///     "Floors" item spanning a building passes the band test and drags every floor in it onto
    ///     one plan.
    ///   * Deferred — cells are sampled at their centres. Anything thinner than a cell —
    ///     a slab edge, a rail, a thin plate — is a coin flip, which at a 250 mm cell is most
    ///     railings.
    ///   * Deferred — an opening is only found when it is fully enclosed, by a
    ///     four-connected fill from the grid border. A stair void at a slab edge, a mezzanine
    ///     perimeter and a double-height overlook are classed as exterior and never analysed, and
    ///     one rasterisation gap in a perimeter deletes an interior void from the results. The
    ///     edge pass partly covers this, which is why the check ships both and says which pass
    ///     found what.
    ///   * Deferred — the chamfer is anisotropic, and the two hand-tuned constants in
    ///     <see cref="AnalyzeEdges"/> exist to cover it. An exact distance transform would remove
    ///     both; the constants are pinned by tests and left.
    ///   * Deferred — handrails are neither height- nor band-filtered, and matching is
    ///     proximity only: a rail 300 mm across the void protects an edge as well as one on it.
    ///   * Deferred — a run is measured against the void it faces and not against the handrail
    ///     it wants. Two edges either side of a one-cell floor strip between two voids are two
    ///     runs and are measured as two, which is right for the rail count and wrong for anyone
    ///     reading the total as a length of floor.
    ///   * Deferred — the cell cap is a flat six million (<see cref="MaxCells"/>), which at
    ///     a 100 mm cell is a 245 m square. Over it the analysis refuses rather than degrading.
    /// </summary>
    public static class FloorGaps
    {
        /// <summary>The largest grid the analysis will build, as a guard against a tiny cell.</summary>
        public const int MaxCells = 6_000_000;

        /// <summary>
        /// How many sweeps the distance through the void may take before it gives up.
        ///
        /// Raised from the source's sixteen, which a plan with a few re-entrant voids reaches
        /// routinely. Hitting it is reported rather than swallowed.
        /// </summary>
        public const int SweepCap = 64;

        private static readonly int[] NeighbourRow = { -1, 1, 0, 0, -1, -1, 1, 1 };
        private static readonly int[] NeighbourCol = { 0, 0, -1, 1, -1, 1, -1, 1 };

        /// <summary>
        /// Rasterise a plan and find the openings enclosed by floor.
        /// </summary>
        /// <param name="minX">The region, low across, in metres.</param>
        /// <param name="minY">The region, low along, in metres.</param>
        /// <param name="maxX">The region, high across, in metres.</param>
        /// <param name="maxY">The region, high along, in metres.</param>
        /// <param name="cellMetres">How wide one cell is, in metres. Smaller is finer and slower.</param>
        /// <param name="floor">The floor triangles, projected into plan.</param>
        /// <param name="plugs">The obstruction triangles, projected into plan. May be empty.</param>
        /// <param name="owners">
        /// One owner index per floor triangle, parallel to <paramref name="floor"/>. Shorter than
        /// the triangles, or null, leaves the cells it does not reach unowned; nothing here reads
        /// what an index means.
        /// </param>
        /// <param name="minGapMetres">An opening whose widest gap reaches this is flagged.</param>
        /// <exception cref="ArgumentOutOfRangeException">The cell size is not positive.</exception>
        /// <exception cref="ArgumentException">The region is empty on an axis.</exception>
        /// <exception cref="InvalidOperationException">The grid would be over <see cref="MaxCells"/>.</exception>
        public static FloorGapGrid Analyze(double minX, double minY, double maxX, double maxY,
                                           double cellMetres,
                                           IReadOnlyList<Tri2>? floor,
                                           IReadOnlyList<Tri2>? plugs,
                                           IReadOnlyList<int>? owners,
                                           double minGapMetres)
        {
            if (cellMetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(cellMetres), "the cell size must be positive");

            if (maxX <= minX || maxY <= minY)
                throw new ArgumentException("the analysis region is empty on an axis");

            floor = floor ?? Array.Empty<Tri2>();
            plugs = plugs ?? Array.Empty<Tri2>();

            var cols = Math.Max(1, (int)Math.Ceiling((maxX - minX) / cellMetres));
            var rows = Math.Max(1, (int)Math.Ceiling((maxY - minY) / cellMetres));

            if ((long)cols * rows > MaxCells)
                throw new InvalidOperationException(
                    "The grid would be " + cols + "×" + rows + " = "
                    + ((long)cols * rows).ToString("N0", CultureInfo.InvariantCulture)
                    + " cells, over the " + MaxCells.ToString("N0", CultureInfo.InvariantCulture)
                    + "-cell limit. Use a larger cell size, or a smaller region.");

            var count = cols * rows;
            var floorMask = new bool[count];
            var plugMask = new bool[count];
            var owner = new int[count];

            for (var i = 0; i < count; i++) owner[i] = -1;

            Rasterize(floor, owners, minX, minY, cellMetres, cols, rows, floorMask, owner);
            Rasterize(plugs, null, minX, minY, cellMetres, cols, rows, plugMask, null);

            // Open is neither floor nor obstruction. An open cell the border cannot reach through
            // other open cells is a hole inside the slab rather than the world outside it.
            var hazard = EnclosedOpens(floorMask, plugMask, cols, rows);

            var clearance = ChamferClearance(hazard, cols, rows, cellMetres, out var maxClearance);

            var openings = LabelOpenings(hazard, clearance, floorMask, owner, minX, minY,
                                         cellMetres, cols, rows, minGapMetres, out var openingIndex);

            return new FloorGapGrid(cols, rows, minX, minY, cellMetres, floorMask, plugMask, hazard,
                                    clearance, maxClearance, owner, openings, openingIndex);
        }

        /// <summary>
        /// Classify every floor cell that faces a void.
        ///
        /// An edge is dangerous when the void it faces is locally wide enough to fall through —
        /// when it holds a point whose distance to the nearest solid on every side reaches half
        /// the limit, within reach of that edge and through the void. Measuring it this way rather
        /// than marching one axis until the next solid keeps the ends of a long narrow slot safe:
        /// the void there is long and never wide enough to fall through, and its length is beside
        /// the point.
        ///
        /// A dangerous edge becomes protected when a handrail runs within the tolerance of it,
        /// judged per cell, so a short handrail covers only the length it spans. A connected
        /// dangerous stretch shorter than the minimum passage is reclassified safe, because
        /// nobody fits through a break that small.
        /// </summary>
        /// <param name="grid">The plan to classify the edges of.</param>
        /// <param name="handrails">The handrail triangles, projected into plan. May be empty.</param>
        /// <param name="limitMetres">A void locally this wide makes the edge facing it a hazard.</param>
        /// <param name="toleranceMetres">A handrail this close to a dangerous edge protects it.</param>
        /// <param name="minPassageMetres">
        /// A dangerous stretch shorter than this is safe. Zero disables it.
        /// </param>
        public static FloorEdgeResult AnalyzeEdges(FloorGapGrid grid,
                                                   IReadOnlyList<Tri2>? handrails,
                                                   double limitMetres,
                                                   double toleranceMetres,
                                                   double minPassageMetres = 0.0)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            var cols = grid.Cols;
            var rows = grid.Rows;
            var cell = grid.CellMetres;
            var count = cols * rows;
            var hazard = grid.Hazard;

            var handrail = new bool[count];

            if (handrails != null && handrails.Count > 0)
                Rasterize(handrails, null, grid.OriginX, grid.OriginY, cell, cols, rows, handrail, null);

            var handrailDistance = ChamferFromSeeds(handrail, cols, rows, cell);

            // The fall-through core: the void cells where the void is locally wide enough to fall
            // through. The chamfer measures centre to centre, which overstates the distance to the
            // solid boundary by half a cell, and the half-cell on the threshold cancels it: a gap
            // narrower than the limit never reads dangerous. A gap within one cell above the limit
            // may read safe, which is the resolution the cell size buys.
            var core = new bool[count];
            var coreThreshold = (limitMetres / 2.0) + (0.5 * cell);

            for (var i = 0; i < count; i++)
                core[i] = hazard[i] && grid.ClearanceMetres[i] >= coreThreshold;

            var notes = new List<string>();
            var coreDistance = ChamferThroughMask(core, hazard, cols, rows, cell, out var hitCap);

            if (hitCap)
                notes.Add("The distance through the voids was still changing after " + SweepCap
                          + " sweeps, so some edges may read safer than they are. A larger cell "
                          + "size or a smaller region settles it.");

            // Along a straight wall the first core cell sits about half the limit beyond the
            // edge's face. At the corner of an opening the core is pulled back from both walls, so
            // the nearest core sits diagonally at √2 times that, and a reach that covers only the
            // straight-wall case leaves every corner of a wide opening reading safe while its
            // sides read dangerous. The 1.75-cell tail covers the half cell to the edge cell's own
            // centre plus discretisation. Both constants are hand-tuned to the chamfer's
            // anisotropy and are pinned by tests.
            var reach = (Math.Sqrt(2.0) * (limitMetres / 2.0)) + (1.75 * cell);

            var edges = new EdgeClass[count];
            var diagonalStep = cell * Math.Sqrt(2.0);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var idx = (r * cols) + c;

                    if (!grid.Floor[idx]) continue;

                    var facesVoid = false;
                    var coreDist = double.PositiveInfinity;

                    for (var d = 0; d < 8; d++)
                    {
                        var nr = r + NeighbourRow[d];
                        var nc = c + NeighbourCol[d];

                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;

                        var nIdx = (nr * cols) + nc;

                        if (!hazard[nIdx]) continue;

                        // Diagonal contact counts: the floor cell at the exact corner of a
                        // rectangular opening touches the void only diagonally, and leaving it
                        // unclassified left an unclassified notch at every corner.
                        facesVoid = true;

                        var reached = coreDistance[nIdx] + (d < 4 ? cell : diagonalStep);

                        if (reached < coreDist) coreDist = reached;
                    }

                    if (!facesVoid) continue;

                    edges[idx] = coreDist > reach ? EdgeClass.SafeGap
                        : handrailDistance[idx] <= toleranceMetres ? EdgeClass.Protected
                        : EdgeClass.Dangerous;
                }
            }

            var runs = Dangerous(grid, edges, limitMetres, minPassageMetres);

            // The three lengths from the final classification, so a run reclassified by the
            // minimum passage leaves no residue in the totals.
            var dangerous = 0.0;

            foreach (var run in runs) dangerous += run.LengthMetres;

            return new FloorEdgeResult(grid, edges, dangerous,
                                       Measured(grid, edges, EdgeClass.Protected),
                                       Measured(grid, edges, EdgeClass.SafeGap), runs, notes);
        }

        /// <summary>
        /// Walk the dangerous runs, drop the ones nobody fits through, and record the rest.
        /// </summary>
        /// <param name="grid">The plan.</param>
        /// <param name="edges">The classification, edited in place where a run is dropped.</param>
        /// <param name="limitMetres">The limit the runs were judged against.</param>
        /// <param name="minPassageMetres">A run shorter than this is reclassified safe.</param>
        private static IReadOnlyList<EdgeRun> Dangerous(FloorGapGrid grid, EdgeClass[] edges,
                                                        double limitMetres, double minPassageMetres)
        {
            var found = new List<EdgeRun>();

            foreach (var run in Runs(edges, EdgeClass.Dangerous, grid.Cols, grid.Rows))
            {
                var length = Along(run, grid.Hazard, grid.Cols, grid.Rows, grid.CellMetres);

                if (minPassageMetres > 0.0 && length < minPassageMetres)
                {
                    foreach (var idx in run) edges[idx] = EdgeClass.SafeGap;

                    continue;
                }

                double sumX = 0, sumY = 0, faced = 0;
                var owners = new Dictionary<int, int>();

                foreach (var idx in run)
                {
                    var r = idx / grid.Cols;
                    var c = idx % grid.Cols;

                    sumX += grid.OriginX + ((c + 0.5) * grid.CellMetres);
                    sumY += grid.OriginY + ((r + 0.5) * grid.CellMetres);

                    Count(owners, grid.Owner[idx]);

                    // The widest gap of the void this run borders, taken off the openings the
                    // cells around it belong to.
                    for (var d = 0; d < 8; d++)
                    {
                        var nr = r + NeighbourRow[d];
                        var nc = c + NeighbourCol[d];

                        if (nr < 0 || nr >= grid.Rows || nc < 0 || nc >= grid.Cols) continue;

                        var opening = grid.OpeningIndex[(nr * grid.Cols) + nc];

                        if (opening >= 0 && opening < grid.Openings.Count
                            && grid.Openings[opening].WidestGapMetres > faced)
                            faced = grid.Openings[opening].WidestGapMetres;
                    }
                }

                found.Add(new EdgeRun(sumX / run.Count, sumY / run.Count, length, faced,
                                      limitMetres, run.Count, Commonest(owners)));
            }

            found.Sort((a, b) => b.LengthMetres.CompareTo(a.LengthMetres));

            return found;
        }

        /// <summary>How much edge one class holds, in metres, measured run by run.</summary>
        /// <param name="grid">The plan.</param>
        /// <param name="edges">The classification.</param>
        /// <param name="wanted">Which class.</param>
        private static double Measured(FloorGapGrid grid, EdgeClass[] edges, EdgeClass wanted)
        {
            var total = 0.0;

            foreach (var run in Runs(edges, wanted, grid.Cols, grid.Rows))
                total += Along(run, grid.Hazard, grid.Cols, grid.Rows, grid.CellMetres);

            return total;
        }

        /// <summary>Every eight-connected run of one class, in grid order.</summary>
        /// <param name="edges">The classification.</param>
        /// <param name="wanted">Which class.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        private static IEnumerable<List<int>> Runs(EdgeClass[] edges, EdgeClass wanted,
                                                   int cols, int rows)
        {
            var count = cols * rows;
            var visited = new bool[count];
            var stack = new Stack<int>();

            for (var start = 0; start < count; start++)
            {
                if (edges[start] != wanted || visited[start]) continue;

                var run = new List<int>();

                visited[start] = true;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    var idx = stack.Pop();

                    run.Add(idx);

                    var r = idx / cols;
                    var c = idx % cols;

                    for (var d = 0; d < 8; d++)
                    {
                        var nr = r + NeighbourRow[d];
                        var nc = c + NeighbourCol[d];

                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;

                        var nIdx = (nr * cols) + nc;

                        if (edges[nIdx] != wanted || visited[nIdx]) continue;

                        visited[nIdx] = true;
                        stack.Push(nIdx);
                    }
                }

                yield return run;
            }
        }

        /// <summary>
        /// How long a run is, measured along the void it faces.
        ///
        /// Two things have to be got right and counting cells gets both wrong. A 45-degree run is
        /// a staircase two cells wide — the cell beside the void and the cell diagonally off it —
        /// so counting cells reports about √2 times its true length, and stepping along it one
        /// cell at a time reports about 1/√2 of it. So: keep the cells that touch the void through
        /// a face rather than only at a corner, which is exactly one cell per step of the
        /// staircase and every cell of a straight run, then take the cheapest tree that joins
        /// them, orthogonal steps one cell and diagonal steps √2, plus one cell for the two half
        /// cells at the ends.
        ///
        /// The filtered cells can fall into more than one piece where a run turns a tight corner,
        /// so the tree is grown per piece and each piece pays its own end allowance.
        /// </summary>
        /// <param name="run">The run's cells.</param>
        /// <param name="hazard">Per-cell enclosed void, for which cells face it through a face.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        private static double Along(List<int> run, bool[] hazard, int cols, int rows,
                                    double cellMetres)
        {
            var cells = Facing(run, hazard, cols, rows);

            if (cells.Count <= 1) return cells.Count * cellMetres;

            var at = new Dictionary<int, int>(cells.Count);

            for (var i = 0; i < cells.Count; i++) at[cells[i]] = i;

            var joined = new bool[cells.Count];
            var best = new double[cells.Count];

            for (var i = 0; i < cells.Count; i++) best[i] = double.PositiveInfinity;

            var total = 0.0;
            var pieces = 0;
            var diagonal = Math.Sqrt(2.0);

            // Prim over the run's own cells. The graph is at most eight-regular, so the plain
            // scan is linear in its edges and needs no heap.
            for (var taken = 0; taken < cells.Count; taken++)
            {
                var next = -1;

                for (var i = 0; i < cells.Count; i++)
                    if (!joined[i] && (next < 0 || best[i] < best[next]))
                        next = i;

                if (next < 0) break;

                if (double.IsPositiveInfinity(best[next]))
                {
                    best[next] = 0.0;
                    pieces++;
                }

                joined[next] = true;
                total += best[next];

                var idx = cells[next];
                var r = idx / cols;
                var c = idx % cols;

                for (var d = 0; d < 8; d++)
                {
                    var nr = r + NeighbourRow[d];
                    var nc = c + NeighbourCol[d];

                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;

                    if (!at.TryGetValue((nr * cols) + nc, out var neighbour) || joined[neighbour])
                        continue;

                    var step = d < 4 ? 1.0 : diagonal;

                    if (step < best[neighbour]) best[neighbour] = step;
                }
            }

            return (total + pieces) * cellMetres;
        }

        /// <summary>
        /// The cells of a run that touch the void through a face rather than only at a corner.
        ///
        /// The whole run when none of them does, which is a run against a void it only meets
        /// diagonally: one or two cells, and dropping them all would measure it as nothing.
        /// </summary>
        /// <param name="run">The run's cells.</param>
        /// <param name="hazard">Per-cell enclosed void.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        private static List<int> Facing(List<int> run, bool[] hazard, int cols, int rows)
        {
            var facing = new List<int>(run.Count);

            foreach (var idx in run)
            {
                var r = idx / cols;
                var c = idx % cols;

                for (var d = 0; d < 4; d++)
                {
                    var nr = r + NeighbourRow[d];
                    var nc = c + NeighbourCol[d];

                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;

                    if (!hazard[(nr * cols) + nc]) continue;

                    facing.Add(idx);
                    break;
                }
            }

            return facing.Count > 0 ? facing : run;
        }

        /// <summary>Add one to what an owner index has been seen on, ignoring the unowned.</summary>
        /// <param name="counts">Where the counts are kept.</param>
        /// <param name="owner">The owner index, or -1.</param>
        private static void Count(IDictionary<int, int> counts, int owner)
        {
            if (owner < 0) return;

            counts[owner] = counts.TryGetValue(owner, out var already) ? already + 1 : 1;
        }

        /// <summary>The owner indices, commonest first, then by index so a tie is stable.</summary>
        /// <param name="counts">What each owner was seen on.</param>
        private static IReadOnlyList<int> Commonest(IDictionary<int, int> counts)
        {
            var found = new List<KeyValuePair<int, int>>(counts);

            found.Sort((a, b) => a.Value == b.Value ? a.Key.CompareTo(b.Key)
                                                    : b.Value.CompareTo(a.Value));

            var owners = new List<int>(found.Count);

            foreach (var pair in found) owners.Add(pair.Key);

            return owners;
        }

        /// <summary>
        /// Mark every cell whose centre falls inside a triangle, and record which triangle's owner
        /// got there first.
        /// </summary>
        /// <param name="triangles">The triangles, in plan.</param>
        /// <param name="owners">One owner index per triangle, or null when nothing owns them.</param>
        /// <param name="originX">Where cell (0,0) starts, across, in metres.</param>
        /// <param name="originY">Where cell (0,0) starts, along, in metres.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="mask">The coverage mask to fill.</param>
        /// <param name="owner">The owner grid to fill, or null when there is none.</param>
        private static void Rasterize(IReadOnlyList<Tri2> triangles, IReadOnlyList<int>? owners,
                                      double originX, double originY, double cellMetres,
                                      int cols, int rows, bool[] mask, int[]? owner)
        {
            for (var t = 0; t < triangles.Count; t++)
            {
                var triangle = triangles[t];
                var mine = owners != null && t < owners.Count ? owners[t] : -1;

                var loX = Math.Min(triangle.Ax, Math.Min(triangle.Bx, triangle.Cx));
                var hiX = Math.Max(triangle.Ax, Math.Max(triangle.Bx, triangle.Cx));
                var loY = Math.Min(triangle.Ay, Math.Min(triangle.By, triangle.Cy));
                var hiY = Math.Max(triangle.Ay, Math.Max(triangle.By, triangle.Cy));

                var c0 = Math.Max(0, (int)Math.Floor((loX - originX) / cellMetres));
                var c1 = Math.Min(cols - 1, (int)Math.Floor((hiX - originX) / cellMetres));
                var r0 = Math.Max(0, (int)Math.Floor((loY - originY) / cellMetres));
                var r1 = Math.Min(rows - 1, (int)Math.Floor((hiY - originY) / cellMetres));

                for (var r = r0; r <= r1; r++)
                {
                    var py = originY + ((r + 0.5) * cellMetres);

                    for (var c = c0; c <= c1; c++)
                    {
                        var idx = (r * cols) + c;

                        if (mask[idx]) continue;

                        var px = originX + ((c + 0.5) * cellMetres);

                        if (!triangle.Holds(px, py)) continue;

                        mask[idx] = true;

                        if (owner != null) owner[idx] = mine;
                    }
                }
            }
        }

        /// <summary>
        /// The open cells the grid border cannot reach through other open cells.
        ///
        /// A four-connected fill from the border marks the exterior; every open cell it never
        /// reaches is a hole inside the slab.
        /// </summary>
        /// <param name="floor">Per-cell floor coverage.</param>
        /// <param name="plug">Per-cell obstruction coverage.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        private static bool[] EnclosedOpens(bool[] floor, bool[] plug, int cols, int rows)
        {
            var count = cols * rows;
            var open = new bool[count];

            for (var i = 0; i < count; i++) open[i] = !floor[i] && !plug[i];

            var exterior = new bool[count];
            var stack = new Stack<int>();

            void Seed(int idx)
            {
                if (!open[idx] || exterior[idx]) return;

                exterior[idx] = true;
                stack.Push(idx);
            }

            for (var c = 0; c < cols; c++)
            {
                Seed(c);
                Seed(((rows - 1) * cols) + c);
            }

            for (var r = 0; r < rows; r++)
            {
                Seed(r * cols);
                Seed((r * cols) + cols - 1);
            }

            while (stack.Count > 0)
            {
                var idx = stack.Pop();
                var r = idx / cols;
                var c = idx % cols;

                if (c > 0) Seed(idx - 1);
                if (c < cols - 1) Seed(idx + 1);
                if (r > 0) Seed(idx - cols);
                if (r < rows - 1) Seed(idx + cols);
            }

            var hazard = new bool[count];

            for (var i = 0; i < count; i++) hazard[i] = open[i] && !exterior[i];

            return hazard;
        }

        /// <summary>
        /// How far each void cell is from the nearest solid, in metres, by a two-pass chamfer.
        /// </summary>
        /// <param name="hazard">Per-cell enclosed void.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        /// <param name="maxClearanceMetres">The largest distance found.</param>
        private static double[] ChamferClearance(bool[] hazard, int cols, int rows,
                                                 double cellMetres, out double maxClearanceMetres)
        {
            var count = cols * rows;
            var dist = new double[count];
            const double big = 1e18;

            for (var i = 0; i < count; i++) dist[i] = hazard[i] ? big : 0.0;

            const double straight = 1.0;
            var diagonal = Math.Sqrt(2.0);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var idx = (r * cols) + c;

                    if (dist[idx] == 0.0) continue;

                    var best = dist[idx];

                    if (c > 0) best = Math.Min(best, dist[idx - 1] + straight);
                    if (r > 0) best = Math.Min(best, dist[idx - cols] + straight);
                    if (r > 0 && c > 0) best = Math.Min(best, dist[idx - cols - 1] + diagonal);
                    if (r > 0 && c < cols - 1) best = Math.Min(best, dist[idx - cols + 1] + diagonal);

                    dist[idx] = best;
                }
            }

            // Still in cell units. Scaling here instead would mix world units with the cell-unit
            // step weights on every neighbour already visited and collapse the distance to about
            // one cell, which is the bug the cell-size-independence test pins.
            for (var r = rows - 1; r >= 0; r--)
            {
                for (var c = cols - 1; c >= 0; c--)
                {
                    var idx = (r * cols) + c;

                    if (dist[idx] == 0.0) continue;

                    var best = dist[idx];

                    if (c < cols - 1) best = Math.Min(best, dist[idx + 1] + straight);
                    if (r < rows - 1) best = Math.Min(best, dist[idx + cols] + straight);
                    if (r < rows - 1 && c < cols - 1) best = Math.Min(best, dist[idx + cols + 1] + diagonal);
                    if (r < rows - 1 && c > 0) best = Math.Min(best, dist[idx + cols - 1] + diagonal);

                    dist[idx] = best;
                }
            }

            maxClearanceMetres = 0.0;

            for (var i = 0; i < count; i++)
            {
                dist[i] *= cellMetres;

                if (dist[i] > maxClearanceMetres) maxClearanceMetres = dist[i];
            }

            return dist;
        }

        /// <summary>
        /// How far each cell is from the nearest seed, in metres, going only through the passable
        /// cells — the distance through the void, so danger cannot leak across floor or equipment.
        ///
        /// Swept to convergence rather than a fixed number of times: a masked, non-convex domain
        /// needs as many sweeps as it has re-entrant turns, and a sweep count that runs out leaves
        /// stale distances that read as safe.
        /// </summary>
        /// <param name="seeds">The cells distance is measured from.</param>
        /// <param name="passable">The cells distance may travel through.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        /// <param name="hitCap">True when it was still changing at <see cref="SweepCap"/>.</param>
        private static double[] ChamferThroughMask(bool[] seeds, bool[] passable, int cols, int rows,
                                                   double cellMetres, out bool hitCap)
        {
            var count = cols * rows;
            var dist = new double[count];
            const double big = 1e18;
            var anySeed = false;

            hitCap = false;

            for (var i = 0; i < count; i++)
            {
                if (seeds[i])
                {
                    dist[i] = 0.0;
                    anySeed = true;
                }
                else
                {
                    dist[i] = big;
                }
            }

            if (!anySeed)
            {
                for (var i = 0; i < count; i++) dist[i] = double.PositiveInfinity;

                return dist;
            }

            const double straight = 1.0;
            var diagonal = Math.Sqrt(2.0);
            var sweeps = 0;
            bool changed;

            do
            {
                changed = false;
                sweeps++;

                for (var r = 0; r < rows; r++)
                {
                    for (var c = 0; c < cols; c++)
                    {
                        var idx = (r * cols) + c;

                        if (!passable[idx] || dist[idx] == 0.0) continue;

                        var best = dist[idx];

                        if (c > 0 && passable[idx - 1]) best = Math.Min(best, dist[idx - 1] + straight);
                        if (r > 0 && passable[idx - cols]) best = Math.Min(best, dist[idx - cols] + straight);
                        if (r > 0 && c > 0 && passable[idx - cols - 1]) best = Math.Min(best, dist[idx - cols - 1] + diagonal);
                        if (r > 0 && c < cols - 1 && passable[idx - cols + 1]) best = Math.Min(best, dist[idx - cols + 1] + diagonal);

                        if (best >= dist[idx]) continue;

                        dist[idx] = best;
                        changed = true;
                    }
                }

                for (var r = rows - 1; r >= 0; r--)
                {
                    for (var c = cols - 1; c >= 0; c--)
                    {
                        var idx = (r * cols) + c;

                        if (!passable[idx] || dist[idx] == 0.0) continue;

                        var best = dist[idx];

                        if (c < cols - 1 && passable[idx + 1]) best = Math.Min(best, dist[idx + 1] + straight);
                        if (r < rows - 1 && passable[idx + cols]) best = Math.Min(best, dist[idx + cols] + straight);
                        if (r < rows - 1 && c < cols - 1 && passable[idx + cols + 1]) best = Math.Min(best, dist[idx + cols + 1] + diagonal);
                        if (r < rows - 1 && c > 0 && passable[idx + cols - 1]) best = Math.Min(best, dist[idx + cols - 1] + diagonal);

                        if (best >= dist[idx]) continue;

                        dist[idx] = best;
                        changed = true;
                    }
                }
            }
            while (changed && sweeps < SweepCap);

            hitCap = changed;

            for (var i = 0; i < count; i++)
                dist[i] = dist[i] >= big ? double.PositiveInfinity : dist[i] * cellMetres;

            return dist;
        }

        /// <summary>How far each cell is from the nearest seed, in metres, by a two-pass chamfer.</summary>
        /// <param name="seeds">The cells distance is measured from.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        private static double[] ChamferFromSeeds(bool[] seeds, int cols, int rows, double cellMetres)
        {
            var count = cols * rows;
            var dist = new double[count];
            const double big = 1e18;
            var anySeed = false;

            for (var i = 0; i < count; i++)
            {
                dist[i] = seeds[i] ? 0.0 : big;
                anySeed |= seeds[i];
            }

            if (!anySeed)
            {
                for (var i = 0; i < count; i++) dist[i] = double.PositiveInfinity;

                return dist;
            }

            const double straight = 1.0;
            var diagonal = Math.Sqrt(2.0);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var idx = (r * cols) + c;

                    if (dist[idx] == 0.0) continue;

                    var best = dist[idx];

                    if (c > 0) best = Math.Min(best, dist[idx - 1] + straight);
                    if (r > 0) best = Math.Min(best, dist[idx - cols] + straight);
                    if (r > 0 && c > 0) best = Math.Min(best, dist[idx - cols - 1] + diagonal);
                    if (r > 0 && c < cols - 1) best = Math.Min(best, dist[idx - cols + 1] + diagonal);

                    dist[idx] = best;
                }
            }

            for (var r = rows - 1; r >= 0; r--)
            {
                for (var c = cols - 1; c >= 0; c--)
                {
                    var idx = (r * cols) + c;

                    if (dist[idx] == 0.0) continue;

                    var best = dist[idx];

                    if (c < cols - 1) best = Math.Min(best, dist[idx + 1] + straight);
                    if (r < rows - 1) best = Math.Min(best, dist[idx + cols] + straight);
                    if (r < rows - 1 && c < cols - 1) best = Math.Min(best, dist[idx + cols + 1] + diagonal);
                    if (r < rows - 1 && c > 0) best = Math.Min(best, dist[idx + cols - 1] + diagonal);

                    dist[idx] = best;
                }
            }

            for (var i = 0; i < count; i++) dist[i] *= cellMetres;

            return dist;
        }

        /// <summary>
        /// The connected voids, as openings, widest gap first.
        /// </summary>
        /// <param name="hazard">Per-cell enclosed void.</param>
        /// <param name="clearance">Per-cell distance to the nearest solid, in metres.</param>
        /// <param name="floor">Per-cell floor coverage, for the owners around an opening.</param>
        /// <param name="owner">Per-cell owner index.</param>
        /// <param name="originX">Where cell (0,0) starts, across, in metres.</param>
        /// <param name="originY">Where cell (0,0) starts, along, in metres.</param>
        /// <param name="cellMetres">How wide one cell is, in metres.</param>
        /// <param name="cols">Grid width in cells.</param>
        /// <param name="rows">Grid height in cells.</param>
        /// <param name="minGapMetres">An opening whose widest gap reaches this is flagged.</param>
        /// <param name="openingIndex">Per-cell index into the openings returned, -1 elsewhere.</param>
        private static IReadOnlyList<FloorOpening> LabelOpenings(
            bool[] hazard, double[] clearance, bool[] floor, int[] owner,
            double originX, double originY, double cellMetres, int cols, int rows,
            double minGapMetres, out int[] openingIndex)
        {
            var count = cols * rows;
            var visited = new bool[count];
            var openings = new List<FloorOpening>();
            var stack = new Stack<int>();

            openingIndex = new int[count];

            for (var i = 0; i < count; i++) openingIndex[i] = -1;

            for (var start = 0; start < count; start++)
            {
                if (!hazard[start] || visited[start]) continue;

                var componentId = openings.Count;

                visited[start] = true;
                stack.Push(start);

                long cells = 0;
                double sumX = 0, sumY = 0, peak = 0;
                int minC = cols, minR = rows, maxC = -1, maxR = -1;
                var owners = new Dictionary<int, int>();

                while (stack.Count > 0)
                {
                    var idx = stack.Pop();

                    openingIndex[idx] = componentId;

                    var r = idx / cols;
                    var c = idx % cols;

                    cells++;
                    sumX += originX + ((c + 0.5) * cellMetres);
                    sumY += originY + ((r + 0.5) * cellMetres);

                    if (clearance[idx] > peak) peak = clearance[idx];
                    if (c < minC) minC = c;
                    if (c > maxC) maxC = c;
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;

                    for (var d = 0; d < 8; d++)
                    {
                        var nr = r + NeighbourRow[d];
                        var nc = c + NeighbourCol[d];

                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;

                        var nIdx = (nr * cols) + nc;

                        // The floor around the opening is what names it. A void cell owns
                        // nothing: no triangle covered it.
                        if (floor[nIdx]) Count(owners, owner[nIdx]);

                        if (!hazard[nIdx] || visited[nIdx]) continue;

                        visited[nIdx] = true;
                        stack.Push(nIdx);
                    }
                }

                // The peak clearance is how far the opening's deepest point sits from any solid;
                // the widest clear span across it is about twice that. It is flagged when that
                // deepest point is further than the limit from anything solid.
                openings.Add(new FloorOpening(
                    sumX / cells, sumY / cells, cells * cellMetres * cellMetres, peak * 2.0,
                    originX + (minC * cellMetres), originY + (minR * cellMetres),
                    originX + ((maxC + 1) * cellMetres), originY + ((maxR + 1) * cellMetres),
                    (int)cells, Commonest(owners))
                {
                    Flagged = peak >= minGapMetres,
                });
            }

            // Widest first for the caller, with the per-cell indices remapped to match.
            var order = new List<int>();

            for (var i = 0; i < openings.Count; i++) order.Add(i);

            order.Sort((a, b) => openings[b].WidestGapMetres.CompareTo(openings[a].WidestGapMetres));

            var moved = new int[openings.Count];
            var sorted = new List<FloorOpening>(openings.Count);

            for (var at = 0; at < order.Count; at++)
            {
                moved[order[at]] = at;
                sorted.Add(openings[order[at]]);
            }

            for (var i = 0; i < count; i++)
                if (openingIndex[i] >= 0) openingIndex[i] = moved[openingIndex[i]];

            return sorted;
        }
    }
}
