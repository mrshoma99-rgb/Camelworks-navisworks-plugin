using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// Openings somebody could fall through, and floor edges with no handrail, as a check.
    ///
    /// A hole in a slab intersects nothing, so no clash test reports it, and neither does the
    /// clearance check: there is no surface over it to measure up from. What the check produces is
    /// a list of places to cover or rail, which is worked on the same board as everything else.
    ///
    /// Two capabilities are null and both are the honest answer: no clash engine found these, so
    /// the document holds no record to echo a decision into, and there are no host groups to nest
    /// in. Naming is the project's own run store, the runner rasterises the plan again, and the
    /// geometry points at the elements the owner grid named.
    ///
    /// Adding this check was one class, one <see cref="CheckCatalogue.Add"/> call and a set-up
    /// screen. The board, the register, the columns, the delta and the pack writer took no edit at
    /// all, which is the claim the check seam was built for.
    /// </summary>
    public sealed class FallCheck : ICheck
    {
        private readonly FindingRegister _register;
        private readonly CheckRunStore _runs;
        private readonly IModelDocument? _document;
        private readonly ITriangleSource? _triangles;
        private readonly double _metresPerUnit;
        private readonly Func<string?>? _save;
        private readonly ProjectStore? _project;

        private readonly List<string> _notes = new List<string>();

        private Drawn? _drawn;

        /// <summary>Read the project's fall-hazard runs as a check.</summary>
        /// <param name="register">Where the findings are held. A fall hazard has nowhere else to go.</param>
        /// <param name="runs">The project's saved runs, of every check.</param>
        /// <param name="document">The open document, or null when there is nothing to read.</param>
        /// <param name="triangles">
        /// Triangles. The plan is rasterised from them, so a seat that cannot read them cannot run
        /// this check at all — unlike clearance, there is no bounding-box answer to fall back to:
        /// a box has no hole in it.
        /// </param>
        /// <param name="metresPerUnit">How many metres one model unit is.</param>
        /// <param name="save">
        /// Writes the findings and the project file, returning the problem or null. The one save
        /// door; null does not save, which is what a test wants and what a seat with no project
        /// file gets.
        /// </param>
        /// <param name="project">The project file the saved runs are written into, or null.</param>
        public FallCheck(FindingRegister register, CheckRunStore runs,
                         IModelDocument? document = null, ITriangleSource? triangles = null,
                         double metresPerUnit = 1.0, Func<string?>? save = null,
                         ProjectStore? project = null)
        {
            _register = register ?? throw new ArgumentNullException(nameof(register));
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _document = document;
            _triangles = triangles;
            _metresPerUnit = metresPerUnit > 0 ? metresPerUnit : 1.0;
            _save = save;
            _project = project;

            Columns = FallColumns.Declare();
            Properties = document == null ? null : new DocumentProperties(document);
            Runner = new Sweep(this);
            Naming = new Names(this);
            Geometry = new Sight(this);
        }

        /// <inheritdoc />
        public string Key => FindingSource.FallHazard;

        /// <inheritdoc />
        public string Name => "Fall hazard";

        /// <summary>
        /// "test", the same word every kind uses.
        ///
        /// It was "run", which was the word this check used when it had a tab of its own. The
        /// Create tab lists every kind in one table and cannot say two words for one column, and
        /// the Manage board reads this back for the same objects: a coordinator set up a test on
        /// one tab and was told how many runs there were on the next.
        /// </summary>
        public string RunWord => "test";

        /// <inheritdoc />
        public string ResultWord => "hazard";

        /// <summary>A gap in a floor, which is what both this check's findings are about.</summary>
        public string Symbol => CheckSymbols.Opening;

        /// <summary>True when a document is open and this seat gives triangles.</summary>
        public bool IsAvailable =>
            _document != null && !_document.IsEmpty
            && _triangles != null && _triangles.CanReadTriangles;

        /// <inheritdoc />
        public string? Unavailable
        {
            get
            {
                if (_document == null || _document.IsEmpty)
                    return "No model is open, so there is no floor to look for openings in.";

                if (_triangles == null || !_triangles.CanReadTriangles)
                    return "This seat gives no triangles"
                           + (_triangles?.Unavailable == null
                               ? "." : ": " + _triangles.Unavailable!.TrimEnd('.') + ".")
                           + " A plan of the floor cannot be drawn from bounding boxes, because a "
                           + "box has no hole in it.";

                return null;
            }
        }

        /// <summary>The three columns a hazard fills: the opening, the gap and the unrailed edge.</summary>
        public IReadOnlyList<ResultColumn> Columns { get; }

        /// <summary>Nothing to echo into: no clash engine found these.</summary>
        public ICheckHostEcho? HostEcho => null;

        /// <summary>No host groups to nest into.</summary>
        public ICheckGrouping? Grouping => null;

        /// <inheritdoc />
        public ICheckRunner? Runner { get; }

        /// <inheritdoc />
        public ICheckNaming? Naming { get; }

        /// <summary>
        /// Property columns, read off the elements the owner grid named.
        ///
        /// The same reader clearance uses: a hazard names ordinary elements, so resolving the keys
        /// in the document is all a property column needs. This was null, and the board gates the
        /// column on the capability, so every added column stayed blank on a fall-hazard board.
        /// Null when no document is open, which is the only state with nothing to resolve against.
        /// </summary>
        public ICheckProperties? Properties { get; }

        /// <inheritdoc />
        public ICheckGeometry? Geometry { get; }

        /// <summary>
        /// What the run just pressed could not do. Cleared when a run starts.
        ///
        /// The set-up screen's own view of the last press. What a read reports comes off each run
        /// instead — see <see cref="CheckRunSpec.Notes"/> — so a caveat stays with the run it
        /// qualifies.
        /// </summary>
        public IReadOnlyList<string> Notes => _notes;

        /// <summary>
        /// How far over the limit the gap is, normalised: 1.0 is exactly at the limit.
        ///
        /// Against the limit the run itself asked for, read off the finding, because a 200 mm
        /// plant-room run and a 500 mm shell-and-core run do not share a definition of bad.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public double? Severity(Finding finding)
        {
            var gap = FallHazardFindings.MetresOf(finding, FallHazardFindings.WidestGap);
            var limit = FallHazardFindings.MetresOf(finding, FallHazardFindings.Limit);

            if (gap == null || limit == null || limit.Value <= 0) return null;

            return gap.Value / limit.Value;
        }

        /// <summary>
        /// What a band is called, in this check's own words.
        ///
        /// Every hazard reported is over the gap allowed — that is what got it reported — so the
        /// bands split the overage and do not test whether there is one. The lowest band said "at
        /// the gap allowed" and no finding could reach it.
        /// </summary>
        /// <param name="severity">The severity, as <see cref="Severity"/> returned it.</param>
        public string SeverityBand(double? severity) =>
            severity == null ? "no measure"
            : severity.Value >= 4.0 ? "far over the gap allowed"
            : severity.Value >= 2.0 ? "over the gap allowed"
            : "just over the gap allowed";

        /// <summary>
        /// No aliases. A hazard is matched by its own id, and three things change one: crossing a
        /// cell boundary, which a millimetre of drift can do; a change in which elements are the
        /// commonest around it, because <see cref="FallHazardFindings.MostElements"/> of them are
        /// in the id; and a change in how many hazards share its cell. Each is reported as one
        /// gone and one new.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public IEnumerable<string> Aliases(Finding finding)
        {
            _ = finding;

            return Array.Empty<string>();
        }

        /// <inheritdoc />
        public IReadOnlyList<CheckRun> Runs()
        {
            var found = new List<CheckRun>();

            foreach (var spec in _runs.For(Key).OrderBy(one => one.Name, StringComparer.OrdinalIgnoreCase))
                found.Add(Run(spec));

            return found;
        }

        /// <summary>The saved run with this id, or null.</summary>
        /// <param name="runId">The run id.</param>
        public CheckRunSpec? Spec(string? runId) => _runs.Find(Key, runId);

        /// <summary>Save a run, replacing what was held under its id, and write the project file.</summary>
        /// <param name="spec">The run.</param>
        /// <returns>Why nothing was written, or null when it landed.</returns>
        public string? Put(CheckRunSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            _runs.Put(spec);

            return Kept();
        }

        /// <summary>
        /// What the register holds for these runs.
        ///
        /// A fall hazard lives nowhere but the register, so a read is what was kept and not a
        /// fresh rasterisation. Rasterising again is <see cref="ICheckRunner.Run"/>, which is a
        /// press somebody makes.
        /// </summary>
        /// <param name="runIds">Which runs, or null for every saved run.</param>
        /// <param name="onProgress">Called with how many findings have been read; false stops it.</param>
        public CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress)
        {
            var runs = Runs();

            if (runIds != null)
            {
                var wanted = new HashSet<string>(runIds, StringComparer.OrdinalIgnoreCase);

                runs = runs.Where(run => wanted.Contains(run.Id)).ToList();
            }

            var ids = new HashSet<string>(runs.Select(run => run.Id), StringComparer.Ordinal);
            var found = new List<Finding>();
            var seen = 0;

            foreach (var finding in _register.From(Key))
            {
                if (!finding.IsPresent) continue;
                if (!CheckFacts.RunsOf(finding).Any(ids.Contains)) continue;

                if (onProgress != null && !onProgress(++seen)) break;

                found.Add(finding);
            }

            // Off each run, not off the last press: a caveat belongs to the answer it qualifies,
            // so reading run B does not show run A's, and A's stays on A.
            var notes = new List<string>();

            foreach (var run in runs)
            {
                var spec = _runs.Find(Key, run.Id);

                if (spec != null) notes.AddRange(spec.Notes);

                if (run.LastRunTicks == 0) notes.Add(run.Name + " has not been run yet.");
            }

            return new CheckRead(runs, found, CheckFacts.ScopeOf(ids), notes);
        }

        /// <summary>Write the saved runs into the project file and save the work.</summary>
        private string? Kept()
        {
            if (_project != null) _runs.Write(_project);

            return _save?.Invoke();
        }

        /// <summary>One saved run, as the rail draws it.</summary>
        /// <param name="spec">The saved run.</param>
        private CheckRun Run(CheckRunSpec spec)
        {
            var settings = FallSettings.From(spec);

            var held = _register.From(Key)
                                .Count(one => one.IsPresent && CheckFacts.IsIn(one, spec.Id));

            return new CheckRun(spec.Id, spec.Name, "Fall hazard", settings.Between, spec.Folder,
                                settings.Threshold, Array.Empty<string>(), held,
                                spec.LastRunTicks, isOurs: true);
        }

        /// <summary>
        /// Rasterise one saved run's plan and record what it found.
        ///
        /// Every step that could not be taken is stated: a run whose floor side resolves to no
        /// triangles reports that and marks nothing absent, because a run that read nothing is not
        /// evidence that yesterday's openings have been covered. Whatever it could not do is
        /// written onto the run, so a read reports it against the run it belongs to.
        /// </summary>
        /// <param name="runId">Which run.</param>
        /// <param name="onProgress">Called with how far it has got; returning false stops it.</param>
        private bool Swept(string runId, Func<int, bool>? onProgress)
        {
            _notes.Clear();

            var spec = _runs.Find(Key, runId);

            if (spec == null)
            {
                _notes.Add("That run is no longer saved in this project.");
                return false;
            }

            if (!IsAvailable) return Told(spec, Unavailable ?? "There is nothing to read.");

            var settings = FallSettings.From(spec);
            var wrong = settings.Wrong();

            if (wrong != null) return Told(spec, wrong);

            var read = 0;
            var floor = new Side(this, settings.LevelMetres, settings.BandMetres);

            if (!floor.Take(settings.Floors, true, ref read, onProgress)) return Stopped(spec);

            if (floor.Triangles.Count == 0)
                return Told(spec,
                            "Nothing under \"" + settings.Floors + "\" gave triangles within "
                            + Printed(settings.BandMetres) + " m of " + Printed(settings.LevelMetres)
                            + " m, so there is no plan to analyse. " + floor.Why());

            var plugs = new Side(this, settings.LevelMetres, settings.BandMetres);

            if (settings.HasObstructions && !plugs.Take(settings.Obstructions, true, ref read, onProgress))
                return Stopped(spec);

            // Handrails are not band-filtered: they sit above the floor and not through it, so
            // a band around the level leaves every one of them out. Everything under what was
            // picked is read and projected flat, which is also why a rail 300 mm across the void
            // protects an edge as well as one on it.
            var rails = new Side(this, settings.LevelMetres, settings.BandMetres);

            if (settings.HasHandrails && !rails.Take(settings.Handrails, false, ref read, onProgress))
                return Stopped(spec);

            var pad = settings.CellMetres * 2.0;
            var span = floor.Span().Box;

            FloorGapGrid grid;

            try
            {
                grid = FloorGaps.Analyze(span.MinX - pad, span.MinY - pad, span.MaxX + pad,
                                         span.MaxY + pad, settings.CellMetres, floor.Triangles,
                                         plugs.Triangles, floor.Owners, settings.LimitMetres);
            }
            catch (InvalidOperationException over)
            {
                return Told(spec, over.Message);
            }

            // The edge pass is where the time goes, so Stop reaches into it: the count carries on
            // from the elements read and then counts cells.
            var edges = FloorGaps.AnalyzeEdges(grid, rails.Triangles, settings.LimitMetres,
                                               settings.HandrailToleranceMetres,
                                               settings.MinPassageMetres,
                                               onProgress == null
                                                   ? (Func<int, bool>?)null
                                                   : cells => onProgress(read + cells));

            if (edges == null) return Stopped(spec);

            foreach (var note in edges.Notes) _notes.Add(note);

            foreach (var note in floor.Notes) _notes.Add(note);

            var found = FallHazardFindings.For(grid, edges, floor.Keys, floor.Names,
                                               settings.LevelMetres, settings.LimitMetres,
                                               FallHazardFindings.DefaultCell, spec.Id, spec.Name);

            // Changing the floor side or the cut elevation re-derives every id under this run: a
            // hazard is keyed by the run, the cell it sits in and the elements around it, so the
            // delta reports the whole run as gone and reported again. Said here, because nothing
            // downstream can tell that from a floor that was covered over.
            var floors = settings.Floors + " at " + Printed(settings.LevelMetres) + " m";

            if (spec.Settings.TryGetValue(FallSettings.LastFloorsKey, out var last)
                && !string.IsNullOrWhiteSpace(last)
                && !string.Equals(last, floors, StringComparison.OrdinalIgnoreCase))
                _notes.Add("This run read the floor from \"" + last + "\" last time and from \""
                           + floors + "\" now. A hazard is identified differently either side of "
                           + "that change, so every hazard under this run reads as gone and "
                           + "reported again rather than as the same hazard, and the status, the "
                           + "assignee and the comments on it do not carry over.");

            spec.Settings[FallSettings.LastFloorsKey] = floors;

            _register.Observe(Key, found, DateTime.UtcNow.Ticks,
                              scope: CheckFacts.ScopeOf(new[] { spec.Id }));

            // Only the last run's plan is kept. A grid at the cell cap is six million cells, so
            // holding one per run would be the largest thing in the session.
            _drawn = new Drawn(spec.Id, grid, edges, settings.LimitMetres);

            spec.LastRunTicks = DateTime.UtcNow.Ticks;

            // Onto the run before the save, so the project file carries what this run could not do.
            spec.Said(_notes);

            var problem = Kept();

            // The save's own problem is about the save and not about the run, so it stays on the
            // press and is not written onto the run as one of its caveats.
            if (problem != null) _notes.Add(problem);

            return true;
        }

        /// <summary>Say why the run stopped short, on the run itself, and give up on it.</summary>
        /// <param name="spec">The run.</param>
        /// <param name="note">Why.</param>
        private bool Told(CheckRunSpec spec, string note)
        {
            _notes.Add(note);

            return Stopped(spec);
        }

        /// <summary>Keep what the run said about itself, and report that it did not finish.</summary>
        /// <param name="spec">The run.</param>
        private bool Stopped(CheckRunSpec spec)
        {
            // Only where this press had something to say. A run somebody stopped observed nothing,
            // so the last run's findings are still what this run stands on and its caveats still
            // qualify them.
            if (_notes.Count == 0) return false;

            spec.Said(_notes);
            Kept();

            return false;
        }



        /// <summary>A length as a sentence prints it.</summary>
        /// <param name="metres">The length.</param>
        private static string Printed(double metres) =>
            metres.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// The triangles of one side of a run, projected into plan, with an owner per triangle.
        ///
        /// The owner index is what the finding's elements come out of, so the projection and the
        /// element list are built in one pass and cannot disagree.
        /// </summary>
        private sealed class Side
        {
            private readonly FallCheck _check;
            private readonly double _level;
            private readonly double _band;
            private readonly List<Tri2> _triangles = new List<Tri2>();
            private readonly List<int> _owners = new List<int>();
            private readonly List<ElementKey> _keys = new List<ElementKey>();
            private readonly List<string?> _names = new List<string?>();
            private readonly List<string> _notes = new List<string>();

            private int _seen;
            private int _refused;
            private int _outside;

            internal Side(FallCheck check, double levelMetres, double bandMetres)
            {
                _check = check;
                _level = levelMetres;
                _band = bandMetres;
            }

            /// <summary>The triangles read, in plan and in metres.</summary>
            internal IReadOnlyList<Tri2> Triangles => _triangles;

            /// <summary>One owner index per triangle.</summary>
            internal IReadOnlyList<int> Owners => _owners;

            /// <summary>The element behind each owner index.</summary>
            internal IReadOnlyList<ElementKey> Keys => _keys;

            /// <summary>What each of those elements is called.</summary>
            internal IReadOnlyList<string?> Names => _names;

            /// <summary>What this side could not read.</summary>
            internal IReadOnlyList<string> Notes => _notes;

            /// <summary>
            /// Read one side. False when the walk was stopped.
            /// </summary>
            /// <param name="side">What the side is set to: a saved set, the selection, everything.</param>
            /// <param name="inBand">
            /// True to keep only the elements whose box reaches the band. False reads everything
            /// under what was picked, which is what handrails need.
            /// </param>
            /// <param name="read">How many elements have been walked, across every side.</param>
            /// <param name="onProgress">Called with that count; returning false stops the walk.</param>
            internal bool Take(string side, bool inBand, ref int read, Func<int, bool>? onProgress)
            {
                var document = _check._document;
                var triangles = _check._triangles;

                if (document == null || triangles == null) return true;

                foreach (var item in document.Traverse(Scope(side)))
                {
                    if (onProgress != null && !onProgress(++read)) return false;

                    if (!item.HasGeometry || item.Key.IsEmpty) continue;

                    _seen++;

                    if (inBand && !Spans(item.Bounds))
                    {
                        _outside++;
                        continue;
                    }

                    if (!triangles.TryRead(item.Key, out var mesh) || mesh == null)
                    {
                        _refused++;
                        continue;
                    }

                    Project(item.Key, item.DisplayName, mesh);
                }

                if (_refused > 0)
                    _notes.Add(_refused.ToString("N0", CultureInfo.InvariantCulture)
                               + " elements would not give triangles and were left out of the plan.");

                return true;
            }

            /// <summary>Why a side read nothing, in the numbers that say which step lost it.</summary>
            internal string Why() =>
                _seen.ToString("N0", CultureInfo.InvariantCulture) + " elements were looked at, "
                + _outside.ToString("N0", CultureInfo.InvariantCulture) + " sat outside the band and "
                + _refused.ToString("N0", CultureInfo.InvariantCulture) + " would not give triangles.";

            /// <summary>The rectangle the triangles cover, in metres.</summary>
            internal BoxFit Span()
            {
                var box = new BoxFit(_triangles[0].Ax, _triangles[0].Ay, 0);

                foreach (var triangle in _triangles)
                {
                    box.Add(triangle.Ax, triangle.Ay, 0);
                    box.Add(triangle.Bx, triangle.By, 0);
                    box.Add(triangle.Cx, triangle.Cy, 0);
                }

                return box;
            }

            /// <summary>What one side's words mean as a traversal.</summary>
            /// <param name="side">What the side is set to.</param>
            private static TraversalScope Scope(string side) =>
                string.Equals(side, FallSettings.Loaded, StringComparison.OrdinalIgnoreCase)
                    ? TraversalScope.WholeDocument
                    : string.Equals(side, FallSettings.Selected, StringComparison.OrdinalIgnoreCase)
                        ? TraversalScope.CurrentSelection
                        : TraversalScope.Set(side);

            /// <summary>Whether an element's box reaches the band around the level.</summary>
            /// <param name="bounds">The element's box, in model units.</param>
            private bool Spans(BoundingBox bounds)
            {
                var scale = _check._metresPerUnit;

                return bounds.MinZ * scale <= _level + _band
                       && bounds.MaxZ * scale >= _level - _band;
            }

            /// <summary>One element's triangles, flattened into plan under a new owner index.</summary>
            /// <param name="key">The element.</param>
            /// <param name="name">What it is called.</param>
            /// <param name="mesh">Its triangles, in metres.</param>
            private void Project(ElementKey key, string? name, TriangleMesh mesh)
            {
                if (mesh.Triangles == 0) return;

                var owner = _keys.Count;

                _keys.Add(key);
                _names.Add(name);

                for (var t = 0; t < mesh.Indices.Count; t += 3)
                {
                    var a = mesh.Indices[t] * 3;
                    var b = mesh.Indices[t + 1] * 3;
                    var c = mesh.Indices[t + 2] * 3;

                    _triangles.Add(new Tri2(mesh.Positions[a], mesh.Positions[a + 1],
                                            mesh.Positions[b], mesh.Positions[b + 1],
                                            mesh.Positions[c], mesh.Positions[c + 1]));

                    _owners.Add(owner);
                }
            }
        }

        /// <summary>The plan the last run drew, kept for a picture of one of its findings.</summary>
        private sealed class Drawn
        {
            internal Drawn(string runId, FloorGapGrid grid, FloorEdgeResult edges, double limitMetres)
            {
                RunId = runId;
                Grid = grid;
                Edges = edges;
                LimitMetres = limitMetres;
            }

            internal string RunId { get; }

            internal FloorGapGrid Grid { get; }

            internal FloorEdgeResult Edges { get; }

            internal double LimitMetres { get; }
        }

        /// <summary>Running a saved run again, and the two ways of forgetting one.</summary>
        private sealed class Sweep : ICheckRunner
        {
            private readonly FallCheck _check;

            internal Sweep(FallCheck check) => _check = check;

            /// <inheritdoc />
            public bool CanRun => _check.IsAvailable;

            /// <inheritdoc />
            public string? CannotRun => _check.Unavailable;

            /// <inheritdoc />
            public bool Run(string runId, Func<int, bool>? onProgress) =>
                _check.Swept(runId, onProgress);

            /// <summary>
            /// Forget the runs and everything they found.
            ///
            /// The findings go with the run because nothing else names them: a hazard's identity is
            /// its run, so a finding left behind by a deleted run could never be reported again.
            /// </summary>
            /// <param name="runIds">Which runs.</param>
            public bool Delete(IReadOnlyList<string> runIds)
            {
                var gone = false;

                foreach (var id in runIds ?? Array.Empty<string>())
                {
                    foreach (var finding in _check._register.From(_check.Key)
                                                  .Where(one => CheckFacts.IsIn(one, id))
                                                  .ToList())
                        _check._register.Remove(finding.Id);

                    gone |= _check._runs.Remove(_check.Key, id);
                }

                _check.Kept();

                return gone;
            }

            /// <summary>
            /// Drop what the runs found, keeping the runs.
            ///
            /// Marked absent and not removed, because a finding somebody closed is the record
            /// of the fix and an issue may point at it.
            /// </summary>
            /// <param name="runIds">Which runs.</param>
            public bool Compact(IReadOnlyList<string> runIds)
            {
                var touched = false;

                foreach (var id in runIds ?? Array.Empty<string>())
                    foreach (var finding in _check._register.From(_check.Key))
                        if (finding.IsPresent && CheckFacts.IsIn(finding, id))
                        {
                            finding.IsPresent = false;
                            touched = true;
                        }

                _check.Kept();

                return touched;
            }
        }

        /// <summary>Renaming and refiling a saved run, through the project's own run store.</summary>
        private sealed class Names : ICheckNaming
        {
            private readonly FallCheck _check;

            internal Names(FallCheck check) => _check = check;

            /// <summary>No naming window: only the clash matrix has one.</summary>
            public bool HasBook => false;

            /// <summary>
            /// Call a run something. The name is not in a finding's identity, so nothing is
            /// re-derived and no decision is lost.
            /// </summary>
            /// <param name="runId">Which run.</param>
            /// <param name="name">The name.</param>
            public bool Rename(string runId, string name)
            {
                var spec = _check._runs.Find(_check.Key, runId);

                if (spec == null || string.IsNullOrWhiteSpace(name)) return false;

                spec.Name = name.Trim();

                _check.Kept();

                return true;
            }

            /// <inheritdoc />
            public bool Refile(string runId, string? folder)
            {
                var spec = _check._runs.Find(_check.Key, runId);

                if (spec == null) return false;

                spec.Folder = string.IsNullOrWhiteSpace(folder) ? null : folder!.Trim();

                _check.Kept();

                return true;
            }
        }

        /// <summary>
        /// Pointing at a hazard.
        ///
        /// Nothing to select and no viewpoint to apply: no engine holds these results, and the run
        /// that found them recorded a place and not a camera. What it does answer is where the
        /// elements are, and a picture — which for this check is the plan itself and not a camera
        /// view, and is the one check in the product that can produce one on a seat with no
        /// renderer.
        /// </summary>
        private sealed class Sight : ICheckGeometry
        {
            private readonly FallCheck _check;

            internal Sight(FallCheck check) => _check = check;

            /// <summary>Nothing selects these: the caller falls back to <see cref="Locate"/>.</summary>
            /// <param name="findings">The findings.</param>
            /// <param name="add">True to add to the selection rather than replace it.</param>
            public int? Select(IReadOnlyList<Finding> findings, bool add)
            {
                _ = findings;
                _ = add;

                return null;
            }

            /// <inheritdoc />
            public IReadOnlyList<ElementLocator> Locate(IReadOnlyList<Finding> findings)
            {
                var found = new List<ElementLocator>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var finding in findings ?? Array.Empty<Finding>())
                    foreach (var key in finding.AllElements())
                        if (!key.IsEmpty && seen.Add(key.ToString()))
                            found.Add(new ElementLocator(key, null, null));

                return found;
            }

            /// <summary>No viewpoint: the run recorded a place, and the board frames it itself.</summary>
            /// <param name="finding">The finding.</param>
            public bool ApplyView(Finding finding)
            {
                _ = finding;

                return false;
            }

            /// <summary>
            /// The plan of the run this finding came from, as a PNG.
            ///
            /// Only for the run last swept in this session: the raster is what a run drew and it is
            /// not kept in the project file, so a finding read back from a previous session has no
            /// picture until its run is run again. Null is the honest answer for that, and the
            /// board and the pack writer already draw a finding with no snapshot.
            /// </summary>
            /// <param name="finding">The finding.</param>
            /// <param name="width">How wide the picture may be, in pixels.</param>
            /// <param name="height">How tall the picture may be, in pixels.</param>
            public byte[]? Snapshot(Finding finding, int width, int height)
            {
                var drawn = _check._drawn;

                if (finding == null || drawn == null) return null;
                if (!CheckFacts.IsIn(finding, drawn.RunId)) return null;

                var across = width / Math.Max(1, drawn.Grid.Cols);
                var along = height / Math.Max(1, drawn.Grid.Rows);
                var pixels = Math.Max(1, Math.Min(across, along));

                var raster = FallHazardFindings.IsEdge(finding)
                    ? FallPlan.Edges(drawn.Edges, pixels)
                    : FallPlan.Heat(drawn.Grid, drawn.LimitMetres, pixels);

                return PngWriter.Encode(raster.Width, raster.Height, raster.Rgba);
            }
        }
    }
}
