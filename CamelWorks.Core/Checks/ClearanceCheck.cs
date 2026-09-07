using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// The clear height over a walking surface, as a check.
    ///
    /// A duct 1.9 m over a corridor intersects nothing, so no clash test at any tolerance reports
    /// it. What comes out of this check is a list of places to argue about, assign and close, which
    /// is a clash list in everything but how it was found — so it goes on the same board, and this
    /// is what puts it there.
    ///
    /// Three capabilities are null and each is the honest answer. There is no clash data in the
    /// document to echo a decision into, no host groups to nest in, and no naming window.
    ///
    /// The runs are the project's own, in <see cref="CheckRunStore"/>. Re-running is pressing Run on
    /// a saved run, and the findings a run produces carry that run's id as their identity, so two
    /// runs over different corridors that meet at the same place stay two findings.
    /// </summary>
    public sealed class ClearanceCheck : ICheck
    {
        private readonly FindingRegister _register;
        private readonly CheckRunStore _runs;
        private readonly IModelDocument? _document;
        private readonly ITriangleSource? _triangles;
        private readonly double _metresPerUnit;
        private readonly Func<string?>? _save;
        private readonly Func<LevelPlan?>? _levels;
        private readonly ProjectStore? _project;

        private readonly List<string> _notes = new List<string>();

        /// <summary>The words the surface guess matches, on an element's category or type.</summary>
        public static IReadOnlyList<string> WalkingWords { get; } =
            new[] { "floor", "slab", "stair", "landing", "ramp", "platform", "deck" };

        /// <summary>Read the project's clearance runs as a check.</summary>
        /// <param name="register">Where the findings are held. A clearance run has nowhere else to write.</param>
        /// <param name="runs">The project's saved runs, of every check.</param>
        /// <param name="document">The open document, or null when nothing can be measured.</param>
        /// <param name="triangles">Triangles, for the runs that detect their own surfaces.</param>
        /// <param name="metresPerUnit">How many metres one model unit is.</param>
        /// <param name="save">
        /// Writes the findings and the project file, returning the problem or null. The one save
        /// door; null does not save, which is what a test wants and what a seat with no project
        /// file gets.
        /// </param>
        /// <param name="levels">
        /// The project's storeys, for a run scoped to some of them. Called once per run rather
        /// than held, because Levels &amp; Zones edits the plan while the document stays open and a
        /// plan captured when the document opened would scope the run by yesterday's storeys.
        /// </param>
        /// <param name="project">
        /// The project file the saved runs are written into. Null keeps them in memory, which is
        /// what a test wants and what a seat with no project file gets.
        /// </param>
        public ClearanceCheck(FindingRegister register, CheckRunStore runs,
                              IModelDocument? document = null, ITriangleSource? triangles = null,
                              double metresPerUnit = 1.0, Func<string?>? save = null,
                              Func<LevelPlan?>? levels = null, ProjectStore? project = null)
        {
            _register = register ?? throw new ArgumentNullException(nameof(register));
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _document = document;
            _triangles = triangles;
            _metresPerUnit = metresPerUnit > 0 ? metresPerUnit : 1.0;
            _save = save;
            _levels = levels;
            _project = project;

            Columns = ClearanceColumns.Declare(WantedOf);
            Runner = new Measure(this);
            Naming = new Names(this);
            Geometry = new Sight();
            Properties = document == null ? null : new DocumentProperties(document);
        }

        /// <inheritdoc />
        public string Key => FindingSource.Headroom;

        /// <inheritdoc />
        public string Name => "Clearance";

        /// <inheritdoc />
        public string RunWord => "run";

        /// <inheritdoc />
        public string ResultWord => "breach";

        /// <summary>
        /// True whenever a document is open. The check needs geometry and not a clash engine, so a
        /// Simulate seat runs it exactly as a Manage seat does.
        /// </summary>
        public bool IsAvailable => _document != null && !_document.IsEmpty;

        /// <inheritdoc />
        public string? Unavailable =>
            IsAvailable ? null : "No model is open, so there is nothing to measure the height over.";

        /// <summary>The four columns a breach fills: the surface, what is over it, the height and the shortfall.</summary>
        public IReadOnlyList<ResultColumn> Columns { get; }

        /// <summary>Nothing to echo into: no clash engine found these, so the document holds no record of them.</summary>
        public ICheckHostEcho? HostEcho => null;

        /// <summary>No host groups to nest into.</summary>
        public ICheckGrouping? Grouping => null;

        /// <inheritdoc />
        public ICheckRunner? Runner { get; }

        /// <inheritdoc />
        public ICheckNaming? Naming { get; }

        /// <summary>
        /// Element properties off the surface and the obstruction, through the document.
        ///
        /// A breach names two ordinary elements, so the columns somebody adds to the board are
        /// answered by resolving those keys and reading what the host holds against them. Null on
        /// a seat with no document, where there is nothing to resolve them in.
        /// </summary>
        public ICheckProperties? Properties { get; }

        /// <inheritdoc />
        public ICheckGeometry? Geometry { get; }

        /// <summary>
        /// How far under the minimum a breach is, normalised: 1.0 is exactly at the minimum.
        ///
        /// Against the run's own minimum, read off the finding, because a 2.100 m corridor run and a
        /// 2.400 m plant-room run do not share a definition of bad.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public double? Severity(Finding finding)
        {
            var wanted = ClearanceFindings.MetresOf(finding, ClearanceFindings.Wanted);
            var measured = ClearanceFindings.MetresOf(finding, ClearanceFindings.Measured);

            if (wanted == null || wanted.Value <= 0 || measured == null) return null;

            // Clamped at zero rather than left to run negative: a measured height below zero is a
            // clash and not a clearance breach, and the producer does not report one.
            return Math.Max(0, wanted.Value - measured.Value) / wanted.Value + 1.0;
        }

        /// <summary>
        /// What a band is called, in this check's own words.
        /// </summary>
        /// <param name="severity">The severity, as <see cref="Severity"/> returned it.</param>
        public string SeverityBand(double? severity) =>
            severity == null ? "no measure"
            : severity.Value >= 1.25 ? "far under the minimum"
            : severity.Value > 1.0 ? "under the minimum"
            : "at the minimum";

        /// <summary>
        /// No aliases. A clearance finding is matched by its own id, and a breach whose geometry
        /// moved far enough to change its cell is reported as one gone and one new.
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

        /// <summary>
        /// The saved run with this id, or null.
        /// </summary>
        /// <param name="runId">The run id.</param>
        public CheckRunSpec? Spec(string? runId) => _runs.Find(Key, runId);

        /// <summary>
        /// Save a run, replacing what was held under its id, and write it into the project file.
        /// </summary>
        /// <param name="spec">The run.</param>
        /// <returns>Why nothing was written, or null when it landed.</returns>
        public string? Put(CheckRunSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            _runs.Put(spec);

            return Kept();
        }

        /// <summary>
        /// Detect the walkable surfaces a run would measure from, without running it.
        ///
        /// Offered so the set-up screen can show the list and let somebody drop what is not a
        /// floor before the run costs a federation traversal. Detection cannot tell a floor from a
        /// desk top, so the list is read rather than trusted.
        /// </summary>
        /// <param name="settings">The run's settings.</param>
        /// <param name="diagnostics">Where the counts and the caveats go, or null to drop them.</param>
        /// <param name="onProgress">Called with how many elements have been read; false stops it.</param>
        public IReadOnlyList<WalkablePatch> Detect(ClearanceSettings settings,
                                                   WalkableDiagnostics? diagnostics = null,
                                                   Func<int, bool>? onProgress = null)
        {
            _notes.Clear();

            return Swept(settings, diagnostics, onProgress);
        }

        /// <summary>
        /// The same detection without clearing the notes, for a run that has already written some.
        /// </summary>
        /// <param name="settings">The run's settings.</param>
        /// <param name="diagnostics">Where the counts and the caveats go, or null to drop them.</param>
        /// <param name="onProgress">Called with how many elements have been read; false stops it.</param>
        private IReadOnlyList<WalkablePatch> Swept(ClearanceSettings settings,
                                                   WalkableDiagnostics? diagnostics,
                                                   Func<int, bool>? onProgress)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (_document == null || _triangles == null) return Array.Empty<WalkablePatch>();

            var options = new WalkableOptions
            {
                ClearHeightMetres = settings.MinimumMetres,
                MinPatchAreaSquareMetres = settings.MinPatchAreaSquareMetres,
                PreferFloorCategories = settings.PreferFloorCategories,
                ExcludeStairs = settings.ExcludeStairs,
            };

            return WalkableSurfaces.Read(_document, _triangles, TraversalScope.WholeDocument,
                                         _metresPerUnit, options, diagnostics, onProgress);
        }

        /// <summary>What the last run or detection could not do. Cleared when either starts.</summary>
        public IReadOnlyList<string> Notes => _notes;

        /// <summary>Write the saved runs into the project file and save the work.</summary>
        private string? Kept()
        {
            if (_project != null) _runs.Write(_project);

            return _save?.Invoke();
        }

        /// <summary>
        /// What the register holds for these runs.
        ///
        /// A clearance result lives nowhere but the register — no clash engine found it and the
        /// document has no record of it — so a read is what was kept, not a fresh measurement.
        /// Measuring again is <see cref="ICheckRunner.Run"/>, which is a press somebody makes.
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

            var notes = new List<string>(_notes);

            foreach (var run in runs.Where(run => run.LastRunTicks == 0))
                notes.Add(run.Name + " has not been run yet.");

            return new CheckRead(runs, found, CheckFacts.ScopeOf(ids), notes);
        }

        /// <summary>The minimum the run a finding came from asked for, in metres, or null.</summary>
        /// <param name="finding">The finding.</param>
        private double? WantedOf(Finding finding) =>
            ClearanceFindings.MetresOf(finding, ClearanceFindings.Wanted);

        /// <summary>One saved run, as the rail draws it.</summary>
        /// <param name="spec">The saved run.</param>
        private CheckRun Run(CheckRunSpec spec)
        {
            var settings = ClearanceSettings.From(spec);

            var held = _register.From(Key)
                                .Count(one => one.IsPresent && CheckFacts.IsIn(one, spec.Id));

            return new CheckRun(spec.Id, spec.Name, "Clearance", settings.Between, spec.Folder,
                                settings.Threshold, Array.Empty<string>(), held,
                                spec.LastRunTicks, isOurs: true);
        }

        /// <summary>
        /// Measure one saved run and record what it found.
        ///
        /// Every step is stated rather than assumed: a run whose surface side resolves to nothing
        /// reports that in the read's notes and marks nothing absent, because a run that measured
        /// nothing is not evidence that yesterday's breaches are fixed.
        /// </summary>
        /// <param name="runId">Which run.</param>
        /// <param name="onProgress">Called with how far it has got; returning false stops it.</param>
        private bool Measured(string runId, Func<int, bool>? onProgress)
        {
            _notes.Clear();

            var spec = _runs.Find(Key, runId);

            if (spec == null)
            {
                _notes.Add("That run is no longer saved in this project.");
                return false;
            }

            if (_document == null || _document.IsEmpty)
            {
                _notes.Add(Unavailable ?? "There is nothing to measure.");
                return false;
            }

            var settings = ClearanceSettings.From(spec);
            var zone = Zone(settings);

            var patches = settings.IsDetecting ? Detected(settings, onProgress) : null;

            if (patches != null && patches.Count == 0)
                _notes.Add("Detection found no walkable surface this run could measure from.");

            var whole = Whole(settings, onProgress);

            if (whole == null) return false;

            var surfaces = settings.IsDetecting
                ? patches!.Select(patch => Standing(patch)).ToList()
                : Side(settings.Surfaces, whole, null, zone, onProgress);

            if (surfaces == null) return false;

            // A run that measured nothing is not evidence that yesterday's breaches are fixed, so
            // it reports what happened and marks nothing absent. An empty zone, a detection that
            // found no patch and a named set the document no longer holds all land here.
            if (surfaces.Count == 0)
            {
                _notes.Add(spec.Name + " resolved to no surface to measure from, so nothing was "
                           + "measured and nothing was marked as fixed. Check the run's surface "
                           + "side and its zone.");

                return false;
            }

            var already = settings.IsDetecting
                ? new HashSet<string>(patches!.Select(patch => patch.Source.ToString()), StringComparer.Ordinal)
                : new HashSet<string>(surfaces.Select(one => one.Id), StringComparer.Ordinal);

            var above = Side(settings.Targets, whole, already, zone, onProgress);

            if (above == null) return false;

            // The same rule on the other side, for a side that named something. A set the document
            // no longer holds and an empty selection resolve to nothing, and a run that measured
            // up to nothing has not shown that anything is fixed either. The three whole-model
            // answers are not covered: over a model with nothing above the floors they honestly
            // measure nothing.
            if (above.Count == 0 && !NeedsWhole(settings.Targets))
            {
                _notes.Add(spec.Name + " resolved to nothing over the surfaces, so nothing was "
                           + "measured and nothing was marked as fixed. Check what the run "
                           + "measures up to.");

                return false;
            }

            above = above.Where(one => !settings.Ignores(one.Category)).ToList();

            var boxes = new Dictionary<string, Piece>(StringComparer.Ordinal);

            foreach (var piece in surfaces) boxes[piece.Id] = piece;
            foreach (var piece in above) boxes[piece.Id] = piece;

            // The reported height, not the required one: the run reports a breach only when it is
            // more than the tolerance under the minimum, and the minimum is still what the finding
            // records as asked for.
            var reportUnder = settings.ReportUnderMetres;

            var result = settings.IsDetecting
                ? Headroom.Check(patches!.Where(patch => settings.CoversLevel(patch.Level)),
                                 above.Select(one => one.Box), reportUnder,
                                 Headroom.DefaultCell, settings.WorstPerPatch)
                : Headroom.Check(surfaces.Select(one => one.Box), above.Select(one => one.Box),
                                 reportUnder, Headroom.DefaultCell, settings.WorstPerPatch);

            var found = new List<Finding>();
            var plan = _levels?.Invoke();
            var at = 0;

            foreach (var span in result.Spans)
            {
                if (!boxes.TryGetValue(span.Floor.Id, out var floor)) continue;
                if (!boxes.TryGetValue(span.Obstruction.Id, out var over)) continue;
                if (!settings.CoversLevel(Level(floor, plan))) continue;

                found.Add(ClearanceFindings.From(
                    span, floor.Key, over.Key, settings.MinimumMetres,
                    spec.Name + " " + (++at).ToString(CultureInfo.InvariantCulture),
                    ClearanceFindings.DefaultCell, spec.Id, spec.Name));
            }

            // Changing the surface side re-derives every id under this run: a detected surface is
            // keyed by where it is and a named one by its element, so the delta reports the whole
            // run as gone and reported again. Said here, because nothing downstream can tell that
            // from a model that moved.
            if (spec.Settings.TryGetValue(ClearanceSettings.LastSurfacesKey, out var last)
                && !string.IsNullOrWhiteSpace(last)
                && !string.Equals(last, settings.Surfaces, StringComparison.OrdinalIgnoreCase))
                _notes.Add("This run measured from \"" + last + "\" last time and from \""
                           + settings.Surfaces + "\" now. A surface is identified differently "
                           + "either side of that change, so every breach under this run reads as "
                           + "gone and reported again rather than as the same breach.");

            spec.Settings[ClearanceSettings.LastSurfacesKey] = settings.Surfaces;

            _register.Observe(Key, found, DateTime.UtcNow.Ticks,
                              scope: CheckFacts.ScopeOf(new[] { spec.Id }));

            spec.LastRunTicks = DateTime.UtcNow.Ticks;

            var problem = Kept();

            if (problem != null) _notes.Add(problem);

            return true;
        }

        /// <summary>The detected surfaces this run measures from, less the ones somebody dropped.</summary>
        /// <param name="settings">The run's settings.</param>
        /// <param name="onProgress">Called with how many elements have been read.</param>
        private List<WalkablePatch> Detected(ClearanceSettings settings, Func<int, bool>? onProgress)
        {
            if (_document == null || _triangles == null)
            {
                _notes.Add("This seat gives no triangles, so the surfaces cannot be detected. "
                           + "Name a set or use the guess instead.");

                return new List<WalkablePatch>();
            }

            var diagnostics = new WalkableDiagnostics();

            var found = Swept(settings, diagnostics, onProgress);

            foreach (var note in diagnostics.Notes) _notes.Add(note);

            if (diagnostics.UpAxis != 2)
                _notes.Add("Most of this model's flat area faces "
                           + (diagnostics.UpAxis == 0 ? "X" : "Y")
                           + ", not Z. The clear height is measured up the Z axis, so what comes "
                           + "back will be wrong.");

            return found.Where(patch => !settings.Excluded.Contains(patch.Id)).ToList();
        }

        /// <summary>
        /// Every element with geometry, when a side asks for one of the three whole-model answers.
        ///
        /// Null when the walk was stopped. A named set walks its own members instead, which on a
        /// federation is the difference between a second and a minute.
        /// </summary>
        /// <param name="settings">The run's settings.</param>
        /// <param name="onProgress">Called with how many elements have been read.</param>
        private List<Piece>? Whole(ClearanceSettings settings, Func<int, bool>? onProgress)
        {
            if (!NeedsWhole(settings.Surfaces) && !NeedsWhole(settings.Targets))
                return new List<Piece>();

            var pieces = new List<Piece>();
            var read = 0;

            foreach (var item in _document!.Traverse(TraversalScope.WholeDocument))
            {
                if (onProgress != null && !onProgress(++read)) return null;

                var piece = Make(item);

                if (piece != null) pieces.Add(piece);
            }

            return pieces;
        }

        /// <summary>The keys of the saved set a run is confined to, or null when it is not.</summary>
        /// <param name="settings">The run's settings.</param>
        private HashSet<string>? Zone(ClearanceSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Zone) || _document == null) return null;

            var inside = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in _document.Traverse(TraversalScope.Set(settings.Zone!)))
                inside.Add(item.Key.ToString());

            if (inside.Count == 0)
                _notes.Add("The zone \"" + settings.Zone + "\" holds nothing this document can "
                           + "resolve, so this run measured nothing.");

            return inside;
        }

        /// <summary>One side's elements, or null when the walk was stopped.</summary>
        /// <param name="choice">What the side is set to.</param>
        /// <param name="whole">Everything loaded, where the choice needs it.</param>
        /// <param name="exclude">The ids the other side has already taken.</param>
        /// <param name="zone">The keys the run is confined to, or null.</param>
        /// <param name="onProgress">Called with how many elements have been read.</param>
        private List<Piece>? Side(string choice, List<Piece> whole, HashSet<string>? exclude,
                                  HashSet<string>? zone, Func<int, bool>? onProgress)
        {
            List<Piece>? pieces;

            if (string.Equals(choice, ClearanceSettings.Guessed, StringComparison.OrdinalIgnoreCase))
                pieces = whole.Where(one => one.Walkable).ToList();
            else if (string.Equals(choice, ClearanceSettings.Loaded, StringComparison.OrdinalIgnoreCase))
                pieces = whole.ToList();
            else if (string.Equals(choice, ClearanceSettings.NotTheSurfaces, StringComparison.OrdinalIgnoreCase))
                pieces = whole.Where(one => exclude == null || !exclude.Contains(one.Id)).ToList();
            else
            {
                var scope = string.Equals(choice, ClearanceSettings.Selected,
                                          StringComparison.OrdinalIgnoreCase)
                    ? TraversalScope.CurrentSelection
                    : TraversalScope.Set(choice);

                pieces = new List<Piece>();
                var read = 0;

                foreach (var item in _document!.Traverse(scope))
                {
                    if (onProgress != null && !onProgress(++read)) return null;

                    var piece = Make(item);

                    if (piece != null) pieces.Add(piece);
                }
            }

            return zone == null ? pieces : pieces.Where(one => zone.Contains(one.Id)).ToList();
        }

        /// <summary>Whether a side's choice can only be answered by walking the whole document.</summary>
        /// <param name="choice">What the side is set to.</param>
        private static bool NeedsWhole(string choice) =>
            string.Equals(choice, ClearanceSettings.Guessed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(choice, ClearanceSettings.Loaded, StringComparison.OrdinalIgnoreCase)
            || string.Equals(choice, ClearanceSettings.NotTheSurfaces, StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether an element reads as something people walk on.</summary>
        /// <param name="item">The element.</param>
        public static bool IsWalkable(IModelItem? item)
        {
            if (item == null) return false;

            var category = item.Category ?? string.Empty;
            var type = item.TypeName ?? string.Empty;

            foreach (var word in WalkingWords)
                if (category.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }

        /// <summary>An element as the check sees it, or null when it carries no geometry.</summary>
        /// <param name="item">The element.</param>
        private Piece? Make(IModelItem item)
        {
            if (!item.HasGeometry) return null;

            var bounds = item.Bounds;
            var id = item.Key.ToString();
            var scale = _metresPerUnit;

            var box = new HeadroomElement(id, item.DisplayName)
            {
                MinX = bounds.MinX * scale, MaxX = bounds.MaxX * scale,
                MinY = bounds.MinY * scale, MaxY = bounds.MaxY * scale,
                MinZ = bounds.MinZ * scale, MaxZ = bounds.MaxZ * scale,
            };

            return new Piece(item.Key, id, item.Category, IsWalkable(item), box);
        }

        /// <summary>A detected surface as the rest of the run sees an element.</summary>
        /// <param name="patch">The detected surface.</param>
        private static Piece Standing(WalkablePatch patch)
        {
            var box = new HeadroomElement(patch.Id, patch.Level == null ? "Surface" : patch.Level + " surface")
            {
                MinX = patch.Footprint.MinX, MaxX = patch.Footprint.MaxX,
                MinY = patch.Footprint.MinY, MaxY = patch.Footprint.MaxY,
                MinZ = patch.Footprint.MinZ, MaxZ = patch.TopMetres,
            };

            return new Piece(patch.Source, patch.Id, patch.Level, true, box) { Level = patch.Level };
        }

        /// <summary>Which storey a surface stands on, or null when the project declares none.</summary>
        /// <param name="piece">The surface.</param>
        /// <param name="plan">The project's storeys as this run read them, or null.</param>
        private static string? Level(Piece piece, LevelPlan? plan) =>
            piece.Level ?? plan?.LevelAt(piece.Box.MaxZ);

        /// <summary>One element as both sides of the run see it.</summary>
        private sealed class Piece
        {
            internal Piece(ElementKey key, string id, string? category, bool walkable, HeadroomElement box)
            {
                Key = key;
                Id = id;
                Category = category ?? string.Empty;
                Walkable = walkable;
                Box = box;
            }

            internal ElementKey Key { get; }

            internal string Id { get; }

            internal string Category { get; }

            internal bool Walkable { get; }

            internal HeadroomElement Box { get; }

            /// <summary>The storey a detected surface was found on, where detection named one.</summary>
            internal string? Level { get; set; }
        }

        /// <summary>Running a saved run again, and the two ways of forgetting one.</summary>
        private sealed class Measure : ICheckRunner
        {
            private readonly ClearanceCheck _check;

            internal Measure(ClearanceCheck check) => _check = check;

            /// <inheritdoc />
            public bool CanRun => _check.IsAvailable;

            /// <inheritdoc />
            public string? CannotRun => _check.Unavailable;

            /// <inheritdoc />
            public bool Run(string runId, Func<int, bool>? onProgress) =>
                _check.Measured(runId, onProgress);

            /// <summary>
            /// Forget the runs, and take what they found off the board.
            ///
            /// The findings are marked absent rather than removed, for the reason
            /// <see cref="Compact"/> gives: a finding somebody closed is the record of the fix and
            /// an issue may point at it. Nothing will report them again — a breach's identity is
            /// its run and the run is gone — so they stay off the board, and the issues that name
            /// them still resolve.
            /// </summary>
            /// <param name="runIds">Which runs.</param>
            public bool Delete(IReadOnlyList<string> runIds)
            {
                var gone = false;

                foreach (var id in runIds ?? Array.Empty<string>())
                {
                    foreach (var finding in _check._register.From(_check.Key))
                        if (finding.IsPresent && CheckFacts.IsIn(finding, id))
                            finding.IsPresent = false;

                    gone |= _check._runs.Remove(_check.Key, id);
                }

                _check.Kept();

                return gone;
            }

            /// <summary>
            /// Drop the runs' resolved results, keeping the runs and everything still open.
            ///
            /// Resolved only, which is what the verb offers: the rest are outstanding work, and a
            /// compact that took them would empty a board somebody is halfway through.
            ///
            /// Marked absent rather than removed, because a finding somebody closed is the record
            /// of the fix and an issue may point at it.
            /// </summary>
            /// <param name="runIds">Which runs.</param>
            public bool Compact(IReadOnlyList<string> runIds)
            {
                var touched = false;

                foreach (var id in runIds ?? Array.Empty<string>())
                    foreach (var finding in _check._register.From(_check.Key))
                        if (finding.IsPresent
                            && finding.Status == FindingStatus.Resolved
                            && CheckFacts.IsIn(finding, id))
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
            private readonly ClearanceCheck _check;

            internal Names(ClearanceCheck check) => _check = check;

            /// <summary>No naming window: only the clash matrix has one.</summary>
            public bool HasBook => false;

            /// <summary>
            /// Call a run something.
            ///
            /// The name is not in a finding's identity, so nothing is re-derived and no decision is
            /// lost. The findings already held keep the old name as their rule until the run is run
            /// again, which is when the producer writes the new one.
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
        /// Pointing at a breach, through the element locator.
        ///
        /// Nothing to select and no viewpoint to apply: no engine holds these results, and the run
        /// that found them recorded a place rather than a camera. What it does answer is where the
        /// two elements are, which is what focus, isolate and a marker all need.
        /// </summary>
        private sealed class Sight : ICheckGeometry
        {
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

            /// <summary>No picture: nothing here renders one.</summary>
            /// <param name="finding">The finding.</param>
            /// <param name="width">Image width in pixels.</param>
            /// <param name="height">Image height in pixels.</param>
            public byte[]? Snapshot(Finding finding, int width, int height)
            {
                _ = finding;
                _ = width;
                _ = height;

                return null;
            }
        }
    }
}
